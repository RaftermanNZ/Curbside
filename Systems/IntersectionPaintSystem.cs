using System;

using System.Collections.Generic;

using Colossal.Mathematics;

using Colossal.Serialization.Entities;

using Game;

using Game.Common;

using Game.Net;

using Game.Pathfind;

using Game.Prefabs;

using Game.Serialization;

using Unity.Collections;

using Unity.Entities;

using Unity.Mathematics;



namespace MedianGrassReplacement.Systems

{

    public struct MGRReplacedPaint : IComponentData, IEmptySerializable { }

    public struct MGRIntersectionPaint : IComponentData, IEmptySerializable { }

    public partial class IntersectionPaintSystem : GameSystemBase, IPreSerialize

    {

        private readonly PaintOwnerNotifications _ownerNotifications = new PaintOwnerNotifications { SharedInputDiscovery = true };
        private readonly PaintOwnerNotifications _geometryNotifications = new PaintOwnerNotifications();
        private EntityQuery _sources, _originals, _paint, _definitions, _changedOwners, _changedGeometry, _hiddenOwners, _revealedOriginals;
        private PaintSourceDiscoverySystem _sourceDiscovery;
        private NativeCrosswalkDragSystem _nativePreviewDrag;
        private List<Entity> _discoverySources;
        private readonly ManagedWorkIndex<Entity,Entity> _sourceIndex=new ManagedWorkIndex<Entity,Entity>();
        private readonly List<Entity> _auditSources=new List<Entity>(64), _ownerSources=new List<Entity>();
        private readonly HashSet<Entity> _maintenanceSources=new HashSet<Entity>(), _previousHiddenOwners=new HashSet<Entity>(), _currentHiddenOwners=new HashSet<Entity>();

        private static readonly ComponentType[] SourceChanges = { ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>() };
        // Copied values only; scoped to this reconciliation pass, never borrowed buffers.
        private readonly Dictionary<Entity,List<HighwayPaintAnchor>> _preservedAnchors=new Dictionary<Entity,List<HighwayPaintAnchor>>();
        private readonly Dictionary<Entity,List<NativeTrafficBoundary>> _preservedTraffic=new Dictionary<Entity,List<NativeTrafficBoundary>>();
        private byte PreservedTrafficRole(CurbLineEmissionSystem lines,Entity owner,Bezier4x3 curve)
        {
            if(!_preservedTraffic.TryGetValue(owner,out var lanes))_preservedTraffic[owner]=lanes=lines.NativeTrafficBoundaries(owner);
            return NativeTrafficBoundaryPolicy.Classify(curve,lanes);
        }
        private bool PreservedNodeBoundary(CurbLineEmissionSystem lines,Entity node,Bezier4x3 curve)
        {
            if(!_preservedAnchors.TryGetValue(node,out var anchors))_preservedAnchors[node]=anchors=lines.HighwayPaintAnchors(node,false);
            bool a=HighwayPaintAnchorPolicy.Find(anchors,curve.a,out _),b=HighwayPaintAnchorPolicy.Find(anchors,curve.d,out _);
            // A gore can have one attached end; a median return can use one road.
            return a||b?HighwayPaintAnchorPolicy.Yellow(curve,anchors):lines.IsYellowNativeNodeBoundary(node,curve);
        }
        private readonly System.Diagnostics.Stopwatch _updateWatch = new System.Diagnostics.Stopwatch();

        private PrefabSystem _prefabs;

        private bool _active, _initial = true;

        private Entity _white; private bool _previousYellow; private string _previousCenterStyle; private int _catalogVersion=-1;

        private static bool _refresh;

        private static readonly HashSet<Entity> _requestedSources = new HashSet<Entity>();

        private readonly HashSet<Entity> _backlog = new HashSet<Entity>();
        private readonly ManagedWorkQueue<Entity> _sourceOrder=new ManagedWorkQueue<Entity>();

        private readonly HashSet<Entity> _pending = new HashSet<Entity>(), _affected = new HashSet<Entity>();

        private readonly HashSet<Entity> _clipFailures = new HashSet<Entity>();

        private readonly ManagedWorkIndex<Entity,Entity> _crossingsAtNode = new ManagedWorkIndex<Entity,Entity>();
        private readonly List<Entity> _changedCrossings = new List<Entity>();

        internal bool HasCrosswalkEdges(Entity node) => _crossingsAtNode.HasOwner(node);

        internal void RefreshCrosswalkEdges(Entity node)
        {
            _changedCrossings.Clear();
            _crossingsAtNode.CopyOwner(node, _changedCrossings);
            foreach (var source in _changedCrossings) RequestSource(source);
        }

        private void TrackCrosswalk(Entity source, Entity node)
        {
            if (node == Entity.Null) _crossingsAtNode.Remove(source);
            else _crossingsAtNode.Add(node, source);
        }

        internal static void RequestSource(Entity source) { if (source != Entity.Null) _requestedSources.Add(source); }
        private static readonly ManagedWorkQueue<Entity> _clickedCrossings=new ManagedWorkQueue<Entity>();
        private readonly HashSet<Entity> _processedClicks=new HashSet<Entity>();
        internal static void RequestCrossing(Entity source){RequestSource(source);if(source!=Entity.Null)_clickedCrossings.Add(source);}

        internal static void RequestRefresh() { _refresh = true; }

        private sealed class Replacement

        {

            internal MarkingKey SourceKey;
            internal bool RoutedCrossing;
            internal Entity MedianPartner;

            internal readonly List<Entity> Paint = new List<Entity>();

            internal readonly List<MarkingKey> Keys = new List<MarkingKey>();

        }

        private readonly HashSet<Entity> _deferred = new HashSet<Entity>();
        private readonly Queue<Entity> _retryOrder=new Queue<Entity>();
        private readonly Dictionary<Entity,int> _clipRetries=new Dictionary<Entity,int>();
        private readonly Dictionary<Entity,float> _retryDue=new Dictionary<Entity,float>();
        private void Defer(Entity source,float delay=.1f){if(_deferred.Add(source)){_retryOrder.Enqueue(source);_retryDue[source]=UnityEngine.Time.realtimeSinceStartup+delay;}}
        private readonly JunctionReadBatch<Entity> _queuedOwnerBatch = new JunctionReadBatch<Entity>();
        private void QueueOwnerPaint(Entity owner,HashSet<Entity> pending)
        {
            // All callers are in this notification phase before reconciliation.
            if (!_queuedOwnerBatch.FirstOwner(owner)) return;
            if(!EntityManager.HasBuffer<Game.Net.SubLane>(owner))return;
            foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(owner,true))
            {
                if (OwnerPaintCandidate(entry.m_SubLane)) pending.Add(entry.m_SubLane);
                _clipRetries.Remove(entry.m_SubLane);
            }
        }

        private readonly Dictionary<Entity, Replacement> _replacements = new Dictionary<Entity, Replacement>();

        protected override void OnCreate()

        {

            base.OnCreate();

            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            var reads = new PaintLaneReadPolicy(_prefabs);
            _ownerNotifications.KnownNonPaintPrefab = reads.IsNonPaint;
            _geometryNotifications.KnownNonPaintPrefab = reads.IsNonPaint;
            var pipeline = CurbsideChangePipeline.For(World);
            pipeline.Connect(World, _prefabs, _ownerNotifications, _externalPaint);
            pipeline.Connect(World, _prefabs, _geometryNotifications, _externalPaint);

            var description = new EntityQueryDesc

            {

                All = new[] { ComponentType.ReadOnly<Lane>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>() },

                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>(), ComponentType.ReadOnly<MGRCurbMarking>(), ComponentType.ReadOnly<MGRIntersectionPaint>(), ComponentType.ReadOnly<Game.Net.CarLane>(), ComponentType.ReadOnly<Game.Net.ParkingLane>(), ComponentType.ReadOnly<Game.Net.TrackLane>() }

            };

            _sources = GetEntityQuery(description);
            _discoverySources = new List<Entity>();
            if (MarkingChangeNotifications.Supported) _sourceDiscovery = World.GetOrCreateSystemManaged<PaintSourceDiscoverySystem>();

            _changedOwners = GetEntityQuery(ComponentType.ReadOnly<Game.Net.SubLane>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());

            _changedOwners.SetChangedVersionFilter(ComponentType.ReadOnly<Game.Net.SubLane>());
            _changedGeometry=GetEntityQuery(ComponentType.ReadOnly<Road>(),ComponentType.ReadOnly<Game.Net.Edge>(),ComponentType.ReadOnly<EdgeGeometry>(),ComponentType.ReadOnly<Composition>(),ComponentType.ReadOnly<Game.Net.SubLane>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
            _changedGeometry.SetChangedVersionFilter(new[]{ComponentType.ReadOnly<EdgeGeometry>(),ComponentType.ReadOnly<Composition>()});

            _hiddenOwners=GetEntityQuery(ComponentType.ReadOnly<Game.Net.SubLane>(),ComponentType.ReadOnly<Game.Tools.Hidden>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
            _originals = GetEntityQuery(ComponentType.ReadOnly<MGRReplacedPaint>());
            _revealedOriginals=GetEntityQuery(ComponentType.ReadOnly<MGRReplacedPaint>(),ComponentType.ReadOnly<Lane>(),ComponentType.Exclude<Game.Tools.Hidden>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());


            _paint = GetEntityQuery(ComponentType.ReadOnly<MGRIntersectionPaint>(), ComponentType.Exclude<Deleted>());

            _definitions = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<NetLaneArchetypeData>(), ComponentType.ReadOnly<NetLaneGeometryData>(), ComponentType.Exclude<MGRPrivatePaintDefinition>());

        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode) { base.OnGamePreload(purpose, mode); savedCrossings.Clear(); _externalPaint.Clear(); _active = false; Restore(); }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)

        {

            base.OnGameLoadingComplete(purpose, mode); savedCrossings.Clear(); Restore();

            _active = (mode & GameMode.GameOrEditor) != GameMode.None;

        }

        internal void ReadRecordingReplacements(Entity owner,System.Action<Entity> read)
        {
            foreach(var pair in _replacements)if(EntityManager.Exists(pair.Key)&&EntityManager.HasComponent<Owner>(pair.Key)&&EntityManager.GetComponentData<Owner>(pair.Key).m_Owner==owner)foreach(var paint in pair.Value.Paint)read(paint);
        }
        private bool Alive(Entity entity) => EntityManager.Exists(entity) && !EntityManager.HasComponent<Deleted>(entity);

        private void RenderChanged(Entity entity)

        {

            if (Alive(entity) && !EntityManager.HasComponent<BatchesUpdated>(entity)) EntityManager.AddComponent<BatchesUpdated>(entity);

        }

        private void Remove(Entity source,bool retainRetry=false)

        {

            World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().RestoreSource(source);
            _deferred.Remove(source);_retryDue.Remove(source);
            if(!retainRetry){_clipRetries.Remove(source);_clipFailures.Remove(source);}

            if (_replacements.TryGetValue(source, out var entry))

            {

                foreach (var entity in entry.Paint) if (Alive(entity)) GeneratedVisualLifecycle.Retire(World,entity);

                _replacements.Remove(source);_sourceIndex.Remove(source);_crossingsAtNode.Remove(source);PreviewPaintInheritanceSystem.SourceChanged(World,source);

            }

            if (EntityManager.Exists(source) && EntityManager.HasComponent<MGRReplacedPaint>(source))

            {

                if (EntityManager.HasComponent<Game.Tools.Hidden>(source) && !EntityManager.HasComponent<MGRHiddenJunctionLine>(source)) EntityManager.RemoveComponent<Game.Tools.Hidden>(source);

                EntityManager.RemoveComponent<MGRReplacedPaint>(source); RenderChanged(source);

            }

        }

        internal void Restore()

        {

            { CompleteDependency(); Dependency = default; }RestorePreviewPaint();

            using (var sources = _originals.ToEntityArray(Allocator.Temp)) foreach (var source in sources) Remove(source);

            using (var paint = _paint.ToEntityArray(Allocator.Temp)) foreach (var entity in paint) GeneratedVisualLifecycle.Retire(World,entity);

            World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().RestoreAll();
            _ownerNotifications.Clear(); _geometryNotifications.Clear(); _changedOwnerPaintSources.Clear(); markingRoadChanges.Clear(); markingOwnerChanges.Clear();CurbsideChangePipeline.For(World).Clear(ChangeConsumer.IntersectionRoad);CurbsideChangePipeline.For(World).Clear(ChangeConsumer.IntersectionOwner);
            _deferred.Clear(); _retryDue.Clear(); _retryOrder.Clear();_clipRetries.Clear(); _backlog.Clear(); _sourceOrder.Clear(); _clickedCrossings.Clear(); _processedClicks.Clear(); _requestedSources.Clear(); _clipFailures.Clear(); _replacements.Clear();_sourceIndex.Clear();_crossingsAtNode.Clear();_previousHiddenOwners.Clear();_currentHiddenOwners.Clear();_maintenanceSources.Clear(); _initial = true; _white = Entity.Null;

        }

        public void PreSerialize(Context context) { PrepareRetainedSourcesForSerialization(); }

        private void FindWhite()

        {

            if (Alive(_white)) return;

            using (var definitions = _definitions.ToEntityArray(Allocator.Temp))

                foreach (var entity in definitions)

                    if (_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(entity, out var prefab) && prefab != null

                        && (prefab.name == "NA Parking Edge Line" || prefab.name == "EU Highway Edge Line")

                        && EntityManager.GetComponentData<NetLaneArchetypeData>(entity).m_LaneArchetype.Valid)

                    { _white = entity; return; }

        }

        private readonly HashSet<Entity> previewHiddenPaint=new HashSet<Entity>();
        internal bool HasHiddenPreviewPaint => previewHiddenPaint.Count != 0;
        internal void RestorePreviewPaint()
        {
            foreach(var entity in previewHiddenPaint)if(Alive(entity)&&EntityManager.HasComponent<Game.Tools.Hidden>(entity)&&(!EntityManager.HasComponent<Owner>(entity)||!EntityManager.HasComponent<Game.Tools.Hidden>(EntityManager.GetComponentData<Owner>(entity).m_Owner)))
            {EntityManager.RemoveComponent<Game.Tools.Hidden>(entity);RenderChanged(entity);}
            previewHiddenPaint.Clear();
        }
        internal void CollectPreviewPaint(Entity source,HashSet<Entity> output)
        {
            if(_replacements.TryGetValue(source,out var entry))foreach(var entity in entry.Paint)output.Add(entity);
        }
        internal void HidePreviewPaint(Entity source)
        {
            if(!_replacements.TryGetValue(source,out var entry))return;
            foreach(var entity in entry.Paint)if(Alive(entity)&&!EntityManager.HasComponent<Game.Tools.Hidden>(entity))
            {EntityManager.AddComponent<Game.Tools.Hidden>(entity);previewHiddenPaint.Add(entity);RenderChanged(entity);}
        }

        internal bool TryPreviewLines(Entity road,Entity node,Bezier4x3 curve,float nativeWidth,out Bezier4x3 first,out Bezier4x3 second,out float lineWidth)
        {
            first=second=default;lineWidth=0;FindWhite();if(_white==Entity.Null)return false;
            lineWidth=EntityManager.GetComponentData<NetLaneGeometryData>(_white).m_Size.x;
            if(!CrosswalkPlacement.TryLines(curve,Mod.Settings.CrosswalkUpgradeWidth,lineWidth,out first,out second))return false;
            var marking=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();
            bool fittedToCurbs = marking.TryFitCrosswalkRails(node,road,lineWidth,ref first,ref second);
            if (!fittedToCurbs)
            {
                if (marking.TryClipCrosswalk(node,first,out var a,road) && marking.TryClipCrosswalk(node,second,out var b,road))
                {
                    first = a;
                    second = b;
                }
                else if (!CrosswalkPlacement.TryNativeFootprint(curve,nativeWidth,Mod.Settings.CrosswalkUpgradeWidth,lineWidth,out first,out second))
                    return false;
            }
            marking.JoinVisibleCrosswalkEdges(node,road,fittedToCurbs,lineWidth,ref first,ref second);
            return true;
        }

        private Entity AddMedianCrosswalkBridges(Entity source, Entity road, Entity node, Bezier4x3 first, Bezier4x3 second,
            CrosswalkUpgradeSystem choices, CurbLineEmissionSystem marking, List<Bezier4x3> output)
        {
            if (!marking.TryAuthoredMedianCrossing(road, node, out float medianWidth)) return Entity.Null;
            float maximumSpan = math.max(5f, medianWidth * 1.5f + 1f);

            // Either source can be recreated first. The lower entity ID owns
            // the visual bridge, and both rails are computed from native inputs.
            Entity partner = Entity.Null;
            float best = float.PositiveInfinity;
            Bezier4x3 a = default, b = default;
            ReadPartner(node);
            if (road != node) ReadPartner(road);
            if (partner != Entity.Null && source.Index < partner.Index)
            {
                output.Add(a);
                output.Add(b);
                return partner;
            }
            return Entity.Null;

            void ReadPartner(Entity owner)
            {
                if (!EntityManager.HasBuffer<Game.Net.SubLane>(owner)) return;
                foreach (var entry in EntityManager.GetBuffer<Game.Net.SubLane>(owner, true))
                {
                    var other = entry.m_SubLane;
                    if (other == source || !Alive(other) || !EntityManager.HasComponent<Curve>(other)
                        || !EntityManager.HasComponent<PrefabRef>(other) || EntityManager.HasComponent<Game.Tools.Temp>(other)
                        || EntityManager.HasComponent<Game.Net.CarLane>(other) || EntityManager.HasComponent<Game.Net.TrackLane>(other)
                        || _externalPaint.Contains(EntityManager, other)) continue;
                    var definition = World.GetOrCreateSystemManaged<CrosswalkVisualSystem>()
                        .Original(EntityManager.GetComponentData<PrefabRef>(other).m_Prefab);
                    if (!EntityManager.HasComponent<NetLaneGeometryData>(definition) || marking.IsStopDefinition(definition)
                        || !_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(definition, out var prefab)
                        || !CrosswalkPaintPolicy.IsPaint(EntityManager.HasComponent<Game.Net.LaneGeometry>(other), prefab?.name)
                        || !choices.IsSourceSelected(other) || choices.IsSourceInvisible(other)
                        || !choices.TryResolveApproach(other, out var otherRoad, out var otherNode)
                        || otherRoad != road || otherNode != node) continue;
                    var curve = EntityManager.GetComponentData<Curve>(other).m_Bezier;
                    float width = EntityManager.GetComponentData<NetLaneGeometryData>(definition).m_Size.x;
                    if (!TryPreviewLines(road, node, curve, width, out var otherFirst, out var otherSecond, out _)
                        || !CrosswalkCurbGeometry.TryBridgeRails(first, second, otherFirst, otherSecond, out var firstBridge, out var secondBridge, maximumSpan)) continue;
                    float score = MathUtils.Length(firstBridge) + MathUtils.Length(secondBridge);
                    if (score > best || (score == best && other.Index >= partner.Index)) continue;
                    best = score;
                    partner = other;
                    a = firstBridge;
                    b = secondBridge;
                }
            }
        }

        private readonly ExternalPaintOwnership _externalPaint=new ExternalPaintOwnership();

        private void Reconcile(Entity source)

        {

            if (!Alive(source) || !EntityManager.HasComponent<Curve>(source) || !EntityManager.HasComponent<PrefabRef>(source)) { Remove(source); return; }

            if(_externalPaint.Contains(EntityManager,source)){Remove(source);return;}
            if(EntityManager.HasComponent<MGRHiddenJunctionLine>(source)){Remove(source);return;}
            if(EntityManager.HasComponent<Game.Net.CarLane>(source)||EntityManager.HasComponent<Game.Net.ParkingLane>(source)||EntityManager.HasComponent<Game.Net.TrackLane>(source))
            {World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>().NoteSource(source,"Paint replacement skipped: this entity also carries a vehicle lane.");Remove(source);return;}

            Entity owner = EntityManager.GetComponentData<Owner>(source).m_Owner;

            if (!Alive(owner)) { Remove(source); return; }

            if (EntityManager.HasComponent<Game.Tools.Hidden>(owner)) { Defer(source); return; }

            Entity prefabEntity = World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().Original(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab);

            if (!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(prefabEntity, out var prefab) || prefab == null) { Remove(source); return; }

            // Never replace/suppress a native RequireStop crossing as longitudinal paint.
            if (World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().IsStopDefinition(prefabEntity)) { Remove(source); return; }

            var choices = World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();

            bool invisible = choices.IsSourceInvisible(source);

            bool crossing = (choices.IsSourceSelected(source) || invisible)

                && CrosswalkPaintPolicy.IsPaint(EntityManager.HasComponent<Game.Net.LaneGeometry>(source),prefab.name);

            bool routedCrossing=EntityManager.HasComponent<Game.Net.PedestrianLane>(source);
            if(routedCrossing && (!crossing || (EntityManager.GetComponentData<Game.Net.PedestrianLane>(source).m_Flags & PedestrianLaneFlags.Crosswalk)==0)){Remove(source);return;}

            var lineSystem=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();

            bool roadLine=EntityManager.HasComponent<Road>(owner)&&NodeLinePolicy.IsLineGeometry(prefab);

            bool nodeTurn=EntityManager.HasComponent<Node>(owner)&&ExternalPaintPolicy.IsBoundary(prefab.name)&&lineSystem.NodeMovement(owner,EntityManager.GetComponentData<Curve>(source).m_Bezier)==NodePaintMovement.UTurn;
            bool turnOwned=nodeTurn&&lineSystem.OwnsNodeUTurnPaint(owner,EntityManager.GetComponentData<Curve>(source).m_Bezier);
            bool nodeCenter=EntityManager.HasComponent<Node>(owner)&&(!nodeTurn||CenterlineUpgradePolicy.Mode(EntityManager,owner)!=0)&&HighwayNodePaintPolicy.Role(prefab.name,lineSystem.IsYellowNodeBoundary(owner,EntityManager.GetComponentData<Curve>(source).m_Bezier))==1;
            bool nodeBoundary=nodeCenter||EntityManager.HasComponent<Node>(owner)&&ExternalPaintPolicy.IsBoundary(prefab.name)&&lineSystem.IsYellowNodeBoundary(owner,EntityManager.GetComponentData<Curve>(source).m_Bezier);
            bool center=roadLine&&prefab.name.IndexOf("Center Line",StringComparison.OrdinalIgnoreCase)>=0;
            bool opposing=roadLine&&lineSystem.IsOpposingBoundary(owner,EntityManager.GetComponentData<Curve>(source).m_Bezier);

            bool automatic=AutomaticMarkingPolicy.Adjusts(EntityManager,owner);
            bool yellow=automatic&&Mod.Settings.YellowCenterLines&&(nodeBoundary||opposing);

            bool medianOwned=roadLine&&(prefab.name.IndexOf("Parking",StringComparison.OrdinalIgnoreCase)<0||RoadSurfacePolicy.IsHighway(EntityManager,owner))&&(lineSystem.HasMedianReplacement(owner,EntityManager.GetComponentData<Curve>(source).m_Bezier));
            bool curbOwned=roadLine&&!center&&lineSystem.HasCurbReplacement(owner,EntityManager.GetComponentData<Curve>(source).m_Bezier);
            bool hideCenter=roadLine&&(center||opposing)&&CenterlineVisibility.Hidden(EntityManager,owner)
                ||nodeBoundary&&CenterlineVisibility.AffectsNodePaint(EntityManager,owner,EntityManager.GetComponentData<Curve>(source).m_Bezier);
            medianOwned|=roadLine&&(center||opposing)&&lineSystem.HasExplicitCenterMarks(owner)||turnOwned||curbOwned||hideCenter||nodeBoundary&&lineSystem.OwnsNodeCenterRole(owner,EntityManager.GetComponentData<Curve>(source).m_Bezier);

            bool outerEdge=roadLine&&!center&&prefab.name.IndexOf("Parking",StringComparison.OrdinalIgnoreCase)<0&&prefab.name.IndexOf("Bay",StringComparison.OrdinalIgnoreCase)<0
                &&(prefab.name.IndexOf("Edge Line",StringComparison.OrdinalIgnoreCase)>=0||(SegmentUpgradePolicy.Mode(EntityManager,owner)&SegmentUpgradePolicy.HideEdges)!=0&&lineSystem.IsOuterEdgePaint(owner,EntityManager.GetComponentData<Curve>(source).m_Bezier));
            bool segmentHidden=roadLine&&SegmentUpgradePolicy.Hides(EntityManager,owner,center||opposing&&!RoadSurfacePolicy.IsAlleyForEdges(EntityManager,owner),outerEdge)
                ||!crossing&&EntityManager.HasComponent<Node>(owner)&&NodeLinePolicy.IsLaneLine(prefab.name)&&SegmentUpgradePolicy.HidesNode(EntityManager,owner,EntityManager.GetComponentData<Curve>(source).m_Bezier,nodeBoundary,prefab.name.IndexOf("Edge Line",StringComparison.OrdinalIgnoreCase)>=0);
            medianOwned|=segmentHidden;
            bool removedEdge=roadLine&&SectionEdgePolicy.Mode(EntityManager,owner)==2&&lineSystem.IsOuterEdgePaint(owner,EntityManager.GetComponentData<Curve>(source).m_Bezier);
            bool directNodeHidden=EntityManager.HasComponent<Node>(owner)&&!crossing&&!NodeLinePolicy.IsProtected(prefab.name)&&(NodeLinePolicy.IsLineGeometry(prefab)||(SegmentUpgradePolicy.Mode(EntityManager,owner)&SegmentUpgradePolicy.HideNative)!=0&&NodeLinePolicy.IsPaintFill(prefab.name))&&((SegmentUpgradePolicy.Mode(EntityManager,owner)&SegmentUpgradePolicy.HideNative)!=0||SegmentUpgradePolicy.Hides(EntityManager,owner,nodeBoundary,prefab.name.IndexOf("Edge Line",StringComparison.OrdinalIgnoreCase)>=0)||nodeBoundary&&EntityManager.HasComponent<MGRHideCenterline>(owner));
            medianOwned|=directNodeHidden||nodeBoundary&&lineSystem.OwnsExplicitNodeCenter(owner);
            medianOwned|=removedEdge||roadLine&&lineSystem.OwnsExplicitEdge(owner,EntityManager.GetComponentData<Curve>(source).m_Bezier);
            bool dividerOwned=roadLine&&LaneDividerPolicy.Mode(EntityManager,owner)!=0&&lineSystem.IsInteriorLanePaint(owner,EntityManager.GetComponentData<Curve>(source).m_Bezier)
                &&(LaneDividerPolicy.Mode(EntityManager,owner)==2||lineSystem.HasDividerMarks(owner));
            bool individualIntent=EntityManager.HasBuffer<MGRIndividualDivider>(owner);
            bool individualOwned=individualIntent&&lineSystem.OwnsIndividualDivider(owner,source);
            bool individualNative=individualIntent&&lineSystem.KeepsIndividualDivider(owner,source);
            if(individualNative){Remove(source);return;}
            dividerOwned|=individualOwned;
            medianOwned|=dividerOwned||EntityManager.HasComponent<Node>(owner)&&lineSystem.OwnsNodeDivider(owner,source);
            bool nativeHidden=!crossing&&!NodeLinePolicy.IsProtected(prefab.name)&&(NodeLinePolicy.IsLineGeometry(prefab)||NodeLinePolicy.IsPaintFill(prefab.name))
                &&(EntityManager.HasComponent<Road>(owner)&&SegmentUpgradePolicy.HidesNative(EntityManager,owner)||EntityManager.HasComponent<Node>(owner)&&SegmentUpgradePolicy.HidesNode(EntityManager,owner,EntityManager.GetComponentData<Curve>(source).m_Bezier,nodeBoundary,outerEdge,true));
            bool highwayOwned=EntityManager.HasComponent<Node>(owner)&&lineSystem.OwnsHighwayNodePaint(owner,source);
            medianOwned|=nativeHidden||highwayOwned;
            bool preserveNative=AutomaticMarkingPolicy.PreservesGeometry(EntityManager,owner)&&!crossing;
            Entity themedEdge=Entity.Null;
            if(!preserveNative&&automatic&&!crossing&&(Mod.Settings.FollowCityMarkingTheme||Mod.Settings.EuropeanMarkings)&&NodeLinePolicy.IsLineGeometry(prefab)&&!NodeLinePolicy.IsProtected(prefab.name))
            {
                // All automatic line roles follow the city, not only edges.
                // Exact mesh variants preserve geometry and dash spacing.
                if(Mod.Settings.FollowCityMarkingTheme&&(Mod.Settings.EuropeanMarkings||!(nodeBoundary||opposing)))themedEdge=lineSystem.NativeJunctionColor(prefabEntity,nodeBoundary||opposing,prefab.name.IndexOf("Edge",StringComparison.OrdinalIgnoreCase)>=0);
                else if(Mod.Settings.EuropeanMarkings&&Mod.Settings.AutomaticThemeEdges&&prefab.name.IndexOf("Edge",StringComparison.OrdinalIgnoreCase)>=0)themedEdge=lineSystem.ThemeEquivalent(prefabEntity);
            }
            bool adaptEdge=themedEdge!=Entity.Null&&themedEdge!=prefabEntity;
            Entity nativeColor=prefabEntity;
            if(preserveNative)
            {
                // Manual replacement/hiding still owns its selected path. A
                // manual native-restore intent also wins over automatic color.
                if(automatic&&!medianOwned&&!NodeLinePolicy.IsProtected(prefab.name)&&NodeLinePolicy.IsLineGeometry(prefab)&&!lineSystem.KeepsNativeHighwayLine(owner,source))
                {
                    var nativeCurve=EntityManager.GetComponentData<Curve>(source).m_Bezier;
                    byte nativeRole=PreservedTrafficRole(lineSystem,owner,nativeCurve);
                    bool nativeBoundary=nativeRole!=0?nativeRole==1:EntityManager.HasComponent<Node>(owner)?PreservedNodeBoundary(lineSystem,owner,nativeCurve):center||opposing;
                    nativeColor=lineSystem.NativeJunctionColor(prefabEntity,nativeBoundary,prefab.name.IndexOf("Edge",StringComparison.OrdinalIgnoreCase)>=0);
                }
                yellow=false;adaptEdge=false;
            }
            bool nativeColorChanged=preserveNative&&nativeColor!=prefabEntity;
            if(preserveNative&&!medianOwned&&!nativeColorChanged){Remove(source);return;}
            if (!crossing && !(center&&automatic) && !yellow && !medianOwned && !adaptEdge && !nativeColorChanged) { Remove(source); return; }

            if (!_replacements.ContainsKey(source) && EntityManager.HasComponent<Game.Tools.Hidden>(source)) { Defer(source); return; }

            _deferred.Remove(source);_retryDue.Remove(source);

            var curve = EntityManager.GetComponentData<Curve>(source).m_Bezier;

            Entity linedCrossingNode = Entity.Null;
            Entity medianPartner = Entity.Null;
            var curves = new List<Bezier4x3>(); Entity paintPrefab = preserveNative?nativeColor:adaptEdge?themedEdge:yellow?lineSystem.ResolveCenterStyle(prefabEntity):prefabEntity;

            if(hideCenter||segmentHidden||nativeHidden||dividerOwned||removedEdge||highwayOwned){}
            else if (crossing && !invisible)

            {

                if (_white == Entity.Null) {choices.NoteSource(source,"Line choice found; white marking asset is not ready.");Defer(source);return;} // Keep zebra visible until replacement asset exists.

                paintPrefab = _white;

                float width = Mod.Settings.CrosswalkUpgradeWidth;

                float lineWidth = EntityManager.GetComponentData<NetLaneGeometryData>(_white).m_Size.x;

                if (!CrosswalkPlacement.TryLines(curve, width, lineWidth, out var first, out var second)) { choices.NoteSource(source,"Line choice found, but crossing dimensions are invalid.");Remove(source); return; }

                var marking = World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();

                Entity crossingNode = owner;

                Entity crossingRoad = Entity.Null;

                if (World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>().TryResolveApproach(source, out var resolvedRoad, out var resolvedNode))

                {

                    crossingRoad = resolvedRoad; crossingNode = resolvedNode;

                }

                else if (EntityManager.HasComponent<Game.Net.Edge>(owner))

                {

                    crossingRoad = owner;
                    var edge = EntityManager.GetComponentData<Game.Net.Edge>(owner);

                    var midpoint = MathUtils.Position(curve, .5f);

                    if (EntityManager.HasComponent<Node>(edge.m_Start) && EntityManager.HasComponent<Node>(edge.m_End)) crossingNode = math.distance(midpoint, EntityManager.GetComponentData<Node>(edge.m_Start).m_Position) < math.distance(midpoint, EntityManager.GetComponentData<Node>(edge.m_End).m_Position) ? edge.m_Start : edge.m_End;

                }

                bool fittedToCurbs=marking.TryFitCrosswalkRails(crossingNode,crossingRoad,lineWidth,ref first,ref second);
                if (!fittedToCurbs && (!marking.TryClipCrosswalk(crossingNode, first, out first, crossingRoad) || !marking.TryClipCrosswalk(crossingNode, second, out second, crossingRoad)))

                {

                    Remove(source,true);

                    float nativeWidth=EntityManager.HasComponent<NetLaneGeometryData>(prefabEntity)?EntityManager.GetComponentData<NetLaneGeometryData>(prefabEntity).m_Size.x:0;
                    if(!CrosswalkPlacement.TryNativeFootprint(curve,nativeWidth,width,lineWidth,out first,out second))
                    {
                        int attempts=_clipRetries.TryGetValue(source,out var previousAttempts)?previousAttempts+1:1;
                        _clipRetries[source]=attempts;
                        // A handful of rapid render frames must not exhaust all attempts
                        // before native geometry has finished. Retry with bounded backoff.
                        if(attempts<=8)Defer(source,math.min(4f,.125f*(1<<attempts)));
                        _clipFailures.Add(source);choices.NoteSource(source,attempts<=8?"Line choice found; curb clipping and native-footprint fallback failed. Retrying.":"Line choice found; geometry could not be fitted. Native paint retained.");return;
                    }

                }

                linedCrossingNode = crossingNode;
                marking.JoinVisibleCrosswalkEdges(crossingNode,crossingRoad,fittedToCurbs,lineWidth,ref first,ref second);
                _clipFailures.Remove(source);_clipRetries.Remove(source); curves.Add(first); curves.Add(second);
                medianPartner = AddMedianCrosswalkBridges(source, crossingRoad, crossingNode, first, second, choices, marking, curves);

            }

            else if(preserveNative&&!medianOwned){curves.Add(curve);}
            else if(adaptEdge&&!medianOwned){curves.Add(curve);}
            else if(nodeBoundary && yellow && !medianOwned) {if(paintPrefab==prefabEntity){Remove(source);return;}curves.Add(curve);}
            else if (!crossing && !medianOwned)

            {

                var marking = World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();

                marking.GetRoadStopRange(owner, curve, marking.LaneForward(owner, source, curve), out float lower, out float upper);

                if (lower <= 0f && upper >= 1f) { if(paintPrefab==prefabEntity){Remove(source);return;} curves.Add(curve); }

                // Both solid center-line and stop-bar meshes have the same

                // 0.1m smoothing; their expansions cancel at the near footprint edge.

                // End the center line on the approach side of that footprint.

                else if (StopLinePlacement.TrimRange(curve, lower, upper, 0.55f, out var clipped) && MathUtils.Length(clipped) > 0.1f) curves.Add(clipped);

                else { Remove(source); return; }

            }

            var keys = new List<MarkingKey>(); foreach (var c in curves) keys.Add(new MarkingKey(c, paintPrefab));

            bool same = _replacements.TryGetValue(source, out var old) && old.Keys.Count == keys.Count;

            if (same) for (int i = 0; i < keys.Count; i++) if (!old.Keys[i].Equals(keys[i]) || !Alive(old.Paint[i])) { same = false; break; }

            if (same) {old.SourceKey=new MarkingKey(curve,prefabEntity);old.RoutedCrossing=routedCrossing;old.MedianPartner=medianPartner;_sourceIndex.Add(owner,source);TrackCrosswalk(source,linedCrossingNode);if(crossing)choices.NoteSource(source,invisible?"Invisible crossing applied.":"Lined crossing rendered.");return;}

            var next = new Replacement { SourceKey = new MarkingKey(curve, prefabEntity), RoutedCrossing=routedCrossing };

            // Create before hiding original; a missing replacement must not erase paint.

            try

            {

                foreach (var c in curves)

                {

                    var entity = World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().Spawn(owner, c, paintPrefab, false, crossing ? .3f : 0f);

                    EntityManager.AddComponent<MGRIntersectionPaint>(entity); next.Paint.Add(entity);

                }

            }

            catch { foreach (var entity in next.Paint) if (Alive(entity)) GeneratedVisualLifecycle.Retire(World,entity); throw; }

            Remove(source);

            next.Keys.AddRange(keys); _replacements[source] = next;_sourceIndex.Add(owner,source);TrackCrosswalk(source,linedCrossingNode);PreviewPaintInheritanceSystem.SourceChanged(World,source);

            EntityManager.AddComponent<MGRReplacedPaint>(source);

            if(routedCrossing) World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().Suppress(source);
            else EntityManager.AddComponent<Game.Tools.Hidden>(source); RenderChanged(source);
            if(crossing)choices.NoteSource(source,invisible?"Invisible crossing applied.":"Lined crossing rendered.");

        }

        private void MaintainReplacement(Entity source,HashSet<Entity> pending,ref EntityCommandBuffer visibility,CrosswalkVisualSystem crossings)
        {
            if(!_replacements.TryGetValue(source,out var entry))return;
            if (entry.MedianPartner != Entity.Null && !Alive(entry.MedianPartner))
                pending.Add(source);
            if(!Alive(source)||_externalPaint.Contains(EntityManager,source)||!EntityManager.HasComponent<Owner>(source)){pending.Add(source);return;}
            Entity owner=EntityManager.GetComponentData<Owner>(source).m_Owner;
            if(!Alive(owner)){pending.Add(source);return;}
            if(EntityManager.HasComponent<Game.Tools.Hidden>(owner)&&!PreviewPaintInheritanceSystem.KeepsOriginal(World,source)
                || _nativePreviewDrag?.HidesSource(source) == true)
            {
                foreach(var paint in entry.Paint)if(Alive(paint)&&!EntityManager.HasComponent<Game.Tools.Hidden>(paint))
                QueuePaintVisibility(paint, true, ref visibility);
                return;
            }
            bool intact=true;
            foreach(var paint in entry.Paint)
            {
                if(!Alive(paint)){intact=false;continue;}
                if(EntityManager.HasComponent<Game.Tools.Hidden>(paint))
                QueuePaintVisibility(paint, false, ref visibility);
            }
            if(!intact){pending.Add(source);return;}
            if(entry.RoutedCrossing)crossings.Suppress(source);
            else if(!EntityManager.HasComponent<Game.Tools.Hidden>(source))
            QueuePaintVisibility(source, true, ref visibility);
        }

        private void QueuePaintVisibility(Entity entity, bool hidden, ref EntityCommandBuffer visibility)
        {
            if (!visibility.IsCreated) visibility = new EntityCommandBuffer(Allocator.Temp);
            if (hidden) visibility.AddComponent<Game.Tools.Hidden>(entity);
            else visibility.RemoveComponent<Game.Tools.Hidden>(entity);
            if (!EntityManager.HasComponent<BatchesUpdated>(entity)) visibility.AddComponent<BatchesUpdated>(entity);
        }

        protected override void OnUpdate()
        {
            long started=ModDiagnostics.RuntimeCosts.Begin();
            try{UpdateMeasured();}finally{ModDiagnostics.RuntimeCosts.End("IntersectionPaintSystem",started);ModDiagnostics.RuntimeCosts.Workload("IntersectionPaintSources",_replacements.Count,_backlog.Count+_sourceOrder.Count);}
        }
        private System.Action _beforeOwnerIntentMutation;
        private bool _ownerIntentMutated;
        private void BeforeOwnerIntentMutation()
        {
            _ownerIntentMutated = true;
            _ownerNotifications.SetReadOnlyValues(false);
            _geometryNotifications.SetReadOnlyValues(false);
        }
        private void PrepareOwnerValidation(Entity owner, ref bool ownerBatch)
        {
            _ownerIntentMutated = false;
            if (!EntityManager.HasComponent<Node>(owner)) return;
            var choices = World.GetExistingSystemManaged<CrosswalkUpgradeSystem>();
            if (choices != null && !choices.HasNodeIntentsForPruning(owner)) return;
            if (choices == null)
            {
                // System creation can be structural; retain the original fence.
                BeforeOwnerIntentMutation();
                choices = World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>();
                choices.PruneNodeIntents(owner);
            }
            else
            {
                if (_beforeOwnerIntentMutation == null) _beforeOwnerIntentMutation = BeforeOwnerIntentMutation;
                choices.PruneNodeIntents(owner, _beforeOwnerIntentMutation);
            }
            if (!_initial && ownerBatch) _ownerNotifications.SetReadOnlyValues(true);
        }

        private void UpdateMeasured()

        {

            if (Mod.Saving) return;

            if (!_active || Mod.Settings == null) return;
            _preservedAnchors.Clear();
            _preservedTraffic.Clear();

            try

            {

                if (_refresh) { _initial = true; _refresh = false; }

                FindWhite();

                var catalog=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();catalog.ObserveDefinitionsIfReady();

                if(_previousCenterStyle!=Mod.Settings.CenterLineStyle||_previousYellow!=Mod.Settings.YellowCenterLines||_catalogVersion!=CurbLineEmissionSystem.CatalogRevision){_previousCenterStyle=Mod.Settings.CenterLineStyle;_previousYellow=Mod.Settings.YellowCenterLines;_catalogVersion=CurbLineEmissionSystem.CatalogRevision;_initial=true;}

                var watch = _updateWatch; watch.Restart();
                var crossingVisuals = World.GetOrCreateSystemManaged<CrosswalkVisualSystem>();

                var pending = _pending; pending.Clear(); foreach (var item in _backlog) pending.Add(item); _backlog.Clear();

                _maintenanceSources.Clear();
            foreach (var source in _requestedSources){pending.Add(source);_maintenanceSources.Add(source);} _requestedSources.Clear();

                int retryBudget = 16;

                int retryVisits=math.min(_retryOrder.Count,retryBudget);
                while(retryVisits-->0)
                {
                    var source=_retryOrder.Dequeue();
                    if(!_deferred.Contains(source))continue;
                    if(_retryDue.TryGetValue(source,out float due)&&UnityEngine.Time.realtimeSinceStartup<due){_retryOrder.Enqueue(source);continue;}
                    _deferred.Remove(source);_retryDue.Remove(source);pending.Add(source);
                }
                var affected = _affected; affected.Clear();
                // One certificate epoch per existing notification phase. Keep
                // read-only values between mutation boundaries. Intent cleanup
                // remains in its original order, with both read scopes closed.
                // End the phase before discovery, visibility or reconciliation.
                bool ownerBatch = false, geometryBatch = false;
                _queuedOwnerBatch.Clear();
                try
                {
                if (catalog.UsesMarkingSpecificChanges)
                {
                    var pipeline = CurbsideChangePipeline.For(World);
                    pipeline.Drain(ChangeConsumer.IntersectionRoad, markingRoadChanges);
                    pipeline.Drain(ChangeConsumer.IntersectionOwner, markingOwnerChanges);
                    foreach(var road in markingRoadChanges)
                    {
                        if(!Alive(road)||!EntityManager.HasComponent<Game.Net.Edge>(road))continue;
                        if (!_initial)
                        {
                            if (!geometryBatch) { _geometryNotifications.BeginBatch(true); geometryBatch = true; }
                            if (!_geometryNotifications.Changed(EntityManager, _externalPaint, road, true)) continue;
                        }
                        QueueOwnerPaint(road,pending);var edge=EntityManager.GetComponentData<Game.Net.Edge>(road);
                        QueueOwnerPaint(edge.m_Start,pending);QueueOwnerPaint(edge.m_End,pending);
                    }
                    foreach(var owner in markingOwnerChanges)
                    {
                        if(!Alive(owner)||!EntityManager.HasBuffer<Game.Net.SubLane>(owner))continue;
                        PrepareOwnerValidation(owner, ref ownerBatch);
                        if (!_initial && !_ownerIntentMutated && _ownerNotifications.UnchangedBeforeSources(EntityManager, _externalPaint, owner, ref ownerBatch)) continue;
                        if (!_initial && ownerBatch) _ownerNotifications.SetReadOnlyValues(true);
                        _changedOwnerPaintSources.Clear();CollectChangedOwnerPaint(owner,_changedOwnerPaintSources);
                        if (_changedOwnerPaintSources.Count == 0) continue;
                        if (!_initial)
                        {
                            if (!ownerBatch) { _ownerNotifications.BeginBatch(true); ownerBatch = true; }
                            if (!_ownerNotifications.Changed(EntityManager, _externalPaint, owner) && !_ownerIntentMutated) continue;
                        }
                        foreach(var source in _changedOwnerPaintSources){pending.Add(source);affected.Add(source);}
                    }
                    markingRoadChanges.Clear();markingOwnerChanges.Clear();CurbsideChangePipeline.For(World).Clear(ChangeConsumer.IntersectionRoad);CurbsideChangePipeline.For(World).Clear(ChangeConsumer.IntersectionOwner);
                }
                else
                {
                using(var roads=_changedGeometry.ToEntityArray(Allocator.Temp))foreach(var road in roads)
                {
                    if (!_initial)
                    {
                        if (!geometryBatch) { _geometryNotifications.BeginBatch(true); geometryBatch = true; }
                        if (!_geometryNotifications.Changed(EntityManager, _externalPaint, road, true)) continue;
                    }
                    QueueOwnerPaint(road,pending);var edge=EntityManager.GetComponentData<Game.Net.Edge>(road);
                    QueueOwnerPaint(edge.m_Start,pending);QueueOwnerPaint(edge.m_End,pending);
                }



                if (!_initial)

                    using (var owners = _changedOwners.ToEntityArray(Allocator.Temp))

                        foreach (var owner in owners)

                        {

                            PrepareOwnerValidation(owner, ref ownerBatch);

                            if (!_ownerIntentMutated && _ownerNotifications.UnchangedBeforeSources(EntityManager, _externalPaint, owner, ref ownerBatch)) continue;
                            if (ownerBatch) _ownerNotifications.SetReadOnlyValues(true);
                            _changedOwnerPaintSources.Clear();
                            CollectChangedOwnerPaint(owner, _changedOwnerPaintSources);
                            if (_changedOwnerPaintSources.Count == 0) continue;
                            if (!ownerBatch) { _ownerNotifications.BeginBatch(true); ownerBatch = true; }
                            if (!_ownerNotifications.Changed(EntityManager, _externalPaint, owner) && !_ownerIntentMutated) continue;
                            foreach (var source in _changedOwnerPaintSources)
                            { pending.Add(source); affected.Add(source); }

                        }


                    markingRoadChanges.Clear();markingOwnerChanges.Clear();CurbsideChangePipeline.For(World).Clear(ChangeConsumer.IntersectionRoad);CurbsideChangePipeline.For(World).Clear(ChangeConsumer.IntersectionOwner);
                }
                }
                finally
                {
                    if (ownerBatch) _ownerNotifications.EndBatch();
                    if (geometryBatch) _geometryNotifications.EndBatch();
                    _queuedOwnerBatch.Clear();
                }
                // Observe before integrity maintenance. Otherwise its old blanket
                // dependency completion would make the readiness gate too late.
                bool backgroundReady = _sourceDiscovery == null
                    || _sourceDiscovery.TryCollect(_discoverySources, _initial);

                // Only changed/hidden owners need immediate visibility maintenance.
            // Hidden-owner removals are detected from last update's managed IDs.
            _currentHiddenOwners.Clear();
            if (_sourceIndex.Count != 0 && !_hiddenOwners.IsEmptyIgnoreFilter)
                using (var owners = _hiddenOwners.ToEntityArray(Allocator.Temp))
                    foreach (var owner in owners) _currentHiddenOwners.Add(owner);
            _ownerSources.Clear();
            foreach(var owner in _currentHiddenOwners)_sourceIndex.CopyOwner(owner,_ownerSources);
            foreach(var owner in _previousHiddenOwners)if(!_currentHiddenOwners.Contains(owner))_sourceIndex.CopyOwner(owner,_ownerSources);
            foreach(var source in _ownerSources)_maintenanceSources.Add(source);
            _previousHiddenOwners.Clear();_previousHiddenOwners.UnionWith(_currentHiddenOwners);
            _maintenanceSources.UnionWith(affected);
            // Native LaneHiddenSystem may reveal sources every render frame.
            // This repair cannot share the round-robin integrity budget: that
            // would expose originals between visits and cause visible overlap.
            if(!_revealedOriginals.IsEmpty)
            {
                using(var revealed=_revealedOriginals.ToEntityArray(Allocator.Temp))
                using(var visibility=new EntityCommandBuffer(Allocator.Temp))
                {
                    foreach(var source in revealed)
                    {
                        if(!_replacements.TryGetValue(source,out var entry)||entry.RoutedCrossing)continue;
                        if(_externalPaint.Contains(EntityManager,source)){pending.Add(source);continue;}
                        visibility.AddComponent<Game.Tools.Hidden>(source);
                        if(!EntityManager.HasComponent<BatchesUpdated>(source))visibility.AddComponent<BatchesUpdated>(source);
                    }
                    if(!visibility.IsEmpty)visibility.Playback(EntityManager);
                }
            }
            _auditSources.Clear();if(backgroundReady)_sourceIndex.Rotate(_auditSources,64);
            foreach(var source in _auditSources)_maintenanceSources.Add(source);
            if (_maintenanceSources.Count > 0)
            {
                EntityCommandBuffer visibility = default;
                try
                {
                    foreach (var source in _maintenanceSources)
                        MaintainReplacement(source, pending, ref visibility, crossingVisuals);
                    if (visibility.IsCreated) visibility.Playback(EntityManager);
                }
                finally
                {
                    if (visibility.IsCreated) visibility.Dispose();
                }
            }

            if (_sourceDiscovery != null)
            {
                // Only background discovery may defer. Explicit requests, owner
                // events, native reveal repairs and reconciliation stay above/below.
                // Its own system version advances only after a successful read.
                foreach (var source in _discoverySources)
                    if (OwnerPaintCandidate(source) && (!_replacements.TryGetValue(source, out var tracked) || affected.Contains(source) || _initial || !tracked.SourceKey.Equals(new MarkingKey(EntityManager.GetComponentData<Curve>(source).m_Bezier, crossingVisuals.Original(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab))))) pending.Add(source);
            }
            else
            {
            _sources.ResetFilter();

                if (!_initial) _sources.SetChangedVersionFilter(SourceChanges);

                try { using (var sources = _sources.ToEntityArray(Allocator.Temp)) foreach (var source in sources) if (OwnerPaintCandidate(source) && (!_replacements.TryGetValue(source, out var tracked) || affected.Contains(source) || _initial || !tracked.SourceKey.Equals(new MarkingKey(EntityManager.GetComponentData<Curve>(source).m_Bezier, crossingVisuals.Original(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab))))) pending.Add(source); }

                finally { _sources.ResetFilter(); }

            }

                if (pending.Count != 0 || _sourceOrder.Count != 0 || _initial)
                {
                    World.GetExistingSystemManaged<YellowPaintDefinitionSystem>()?.EnsureCurrentCatalog();
                    catalog.RefreshDefinitionsIfNeeded();
                    if (_catalogVersion != CurbLineEmissionSystem.CatalogRevision)
                    {
                        _catalogVersion = CurbLineEmissionSystem.CatalogRevision;
                        _initial = true;
                        _sources.ResetFilter();
                        using (var all = _sources.ToEntityArray(Allocator.Temp))
                            foreach (var source in all) if (OwnerPaintCandidate(source)) pending.Add(source);
                    }
                }
            int processed = 0;

                foreach(var requested in pending)_sourceOrder.Add(requested);
                _processedClicks.Clear();
                // User-selected crossings must not wait behind a city-wide
                // refresh. Keep the same total work/time budget and reserve
                // capacity for background roads after at most eight clicks.
                while(processed<8&&_clickedCrossings.Take(out var clicked))
                {Reconcile(clicked);_processedClicks.Add(clicked);processed++;}
                // Use unused crossing-priority slots after a save. User clicks
                // remain first, at most eight total priority visits are allowed,
                // and ordinary work keeps its existing remainder of the budget.
                ProcessSavedCrosswalkPriority(ref processed);
                while(_sourceOrder.Count>0)
                {
                    if(processed>=128||(processed>=8&&watch.Elapsed.TotalMilliseconds>=2))break;
                    if(!_sourceOrder.Take(out var source))break;
                    if(_processedClicks.Contains(source))continue;
                    Reconcile(source);processed++;
                }

                watch.Stop();

                if (_initial) Mod.Debug($"Intersection paint: {_replacements.Count} native sources replaced; explicit crossing choices reconciled.");

                _initial = false;

            }

            catch (Exception error) { Restore(); Enabled = false; Mod.Log.Error("Intersection paint stopped: " + error); }
            finally { _preservedAnchors.Clear(); _preservedTraffic.Clear(); }

        }

    }

}
