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

        private EntityQuery _sources, _originals, _paint, _definitions, _changedOwners, _changedGeometry;

        private PrefabSystem _prefabs;

        private bool _active, _initial = true;

        private Entity _white; private bool _previousYellow; private string _previousCenterStyle; private int _catalogVersion=-1;

        private static bool _refresh;

        private static readonly HashSet<Entity> _requestedSources = new HashSet<Entity>();

        private readonly HashSet<Entity> _backlog = new HashSet<Entity>();

        private readonly HashSet<Entity> _pending = new HashSet<Entity>(), _affected = new HashSet<Entity>();

        private readonly HashSet<Entity> _clipFailures = new HashSet<Entity>();

        internal static void RequestSource(Entity source) { if (source != Entity.Null) _requestedSources.Add(source); }

        internal static void RequestRefresh() { _refresh = true; }

        private sealed class Replacement

        {

            internal MarkingKey SourceKey;
            internal bool RoutedCrossing;

            internal readonly List<Entity> Paint = new List<Entity>();

            internal readonly List<MarkingKey> Keys = new List<MarkingKey>();

        }

        private readonly HashSet<Entity> _deferred = new HashSet<Entity>();
        private readonly Queue<Entity> _retryOrder=new Queue<Entity>();
        private readonly Dictionary<Entity,int> _clipRetries=new Dictionary<Entity,int>();
        private readonly Dictionary<Entity,float> _retryDue=new Dictionary<Entity,float>();
        private void Defer(Entity source,float delay=.1f){if(_deferred.Add(source)){_retryOrder.Enqueue(source);_retryDue[source]=UnityEngine.Time.realtimeSinceStartup+delay;}}
        private void QueueOwnerPaint(Entity owner,HashSet<Entity> pending)
        {
            if(!EntityManager.HasBuffer<Game.Net.SubLane>(owner))return;
            foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(owner,true)){pending.Add(entry.m_SubLane);_clipRetries.Remove(entry.m_SubLane);}
        }

        private readonly Dictionary<Entity, Replacement> _replacements = new Dictionary<Entity, Replacement>();

        protected override void OnCreate()

        {

            base.OnCreate();

            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();

            var description = new EntityQueryDesc

            {

                All = new[] { ComponentType.ReadOnly<Lane>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>() },

                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>(), ComponentType.ReadOnly<MGRCurbMarking>(), ComponentType.ReadOnly<MGRIntersectionPaint>(), ComponentType.ReadOnly<Game.Net.CarLane>(), ComponentType.ReadOnly<Game.Net.ParkingLane>(), ComponentType.ReadOnly<Game.Net.TrackLane>() }

            };

            _sources = GetEntityQuery(description);

            _changedOwners = GetEntityQuery(ComponentType.ReadOnly<Game.Net.SubLane>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());

            _changedOwners.SetChangedVersionFilter(ComponentType.ReadOnly<Game.Net.SubLane>());
            _changedGeometry=GetEntityQuery(ComponentType.ReadOnly<Road>(),ComponentType.ReadOnly<Game.Net.Edge>(),ComponentType.ReadOnly<EdgeGeometry>(),ComponentType.ReadOnly<Composition>(),ComponentType.ReadOnly<Game.Net.SubLane>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
            _changedGeometry.SetChangedVersionFilter(new[]{ComponentType.ReadOnly<EdgeGeometry>(),ComponentType.ReadOnly<Composition>()});

            _originals = GetEntityQuery(ComponentType.ReadOnly<MGRReplacedPaint>());

            _paint = GetEntityQuery(ComponentType.ReadOnly<MGRIntersectionPaint>(), ComponentType.Exclude<Deleted>());

            _definitions = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<NetLaneArchetypeData>(), ComponentType.ReadOnly<NetLaneGeometryData>(), ComponentType.Exclude<MGRPrivatePaintDefinition>());

        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode) { base.OnGamePreload(purpose, mode); _externalPaint.Clear(); _active = false; Restore(); }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)

        {

            base.OnGameLoadingComplete(purpose, mode); Restore();

            _active = (mode & GameMode.GameOrEditor) != GameMode.None;

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

                _replacements.Remove(source);

            }

            if (EntityManager.Exists(source) && EntityManager.HasComponent<MGRReplacedPaint>(source))

            {

                if (EntityManager.HasComponent<Game.Tools.Hidden>(source) && !EntityManager.HasComponent<MGRHiddenJunctionLine>(source)) EntityManager.RemoveComponent<Game.Tools.Hidden>(source);

                EntityManager.RemoveComponent<MGRReplacedPaint>(source); RenderChanged(source);

            }

        }

        internal void Restore()

        {

            Dependency.Complete();RestorePreviewPaint();

            using (var sources = _originals.ToEntityArray(Allocator.Temp)) foreach (var source in sources) Remove(source);

            using (var paint = _paint.ToEntityArray(Allocator.Temp)) foreach (var entity in paint) GeneratedVisualLifecycle.Retire(World,entity);

            World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().RestoreAll();
            _deferred.Clear(); _retryDue.Clear(); _retryOrder.Clear();_clipRetries.Clear(); _backlog.Clear(); _requestedSources.Clear(); _clipFailures.Clear(); _replacements.Clear(); _initial = true; _white = Entity.Null;

        }

        public void PreSerialize(Context context) { Restore(); World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().RestoreAll(); }

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
        internal void RestorePreviewPaint()
        {
            foreach(var entity in previewHiddenPaint)if(Alive(entity)&&EntityManager.HasComponent<Game.Tools.Hidden>(entity)&&(!EntityManager.HasComponent<Owner>(entity)||!EntityManager.HasComponent<Game.Tools.Hidden>(EntityManager.GetComponentData<Owner>(entity).m_Owner)))
            {EntityManager.RemoveComponent<Game.Tools.Hidden>(entity);RenderChanged(entity);}
            previewHiddenPaint.Clear();
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
            if(marking.TryClipCrosswalk(node,first,out var a,road)&&marking.TryClipCrosswalk(node,second,out var b,road)){first=a;second=b;JoinInnerCrosswalk(node,ref first,ref second);return true;}
            bool fitted=CrosswalkPlacement.TryNativeFootprint(curve,nativeWidth,Mod.Settings.CrosswalkUpgradeWidth,lineWidth,out first,out second);
            if(fitted)JoinInnerCrosswalk(node,ref first,ref second);return fitted;
        }

        private void JoinInnerCrosswalk(Entity node,ref Bezier4x3 first,ref Bezier4x3 second)
        {
            if(!EntityManager.HasComponent<Node>(node))return;
            var position=EntityManager.GetComponentData<Node>(node).m_Position;
            bool firstInner=math.distancesq(MathUtils.Position(first,.5f).xz,position.xz)<=math.distancesq(MathUtils.Position(second,.5f).xz,position.xz);
            var guides=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().JunctionEdgePreview(node,1);
            if(CrosswalkPlacement.TryJoinEdgeEnds(firstInner?first:second,guides,out var joined)){if(firstInner)first=joined;else second=joined;}
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

            bool yellow=Mod.Settings.YellowCenterLines&&(nodeBoundary||opposing);

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
            medianOwned|=dividerOwned||EntityManager.HasComponent<Node>(owner)&&lineSystem.OwnsNodeDivider(owner,source);
            bool nativeHidden=!crossing&&!NodeLinePolicy.IsProtected(prefab.name)&&(NodeLinePolicy.IsLineGeometry(prefab)||NodeLinePolicy.IsPaintFill(prefab.name))
                &&(EntityManager.HasComponent<Road>(owner)&&SegmentUpgradePolicy.HidesNative(EntityManager,owner)||EntityManager.HasComponent<Node>(owner)&&SegmentUpgradePolicy.HidesNode(EntityManager,owner,EntityManager.GetComponentData<Curve>(source).m_Bezier,nodeBoundary,outerEdge,true));
            bool highwayOwned=EntityManager.HasComponent<Node>(owner)&&lineSystem.OwnsHighwayNodePaint(owner,source);
            medianOwned|=nativeHidden||highwayOwned;
            Entity themedEdge=Entity.Null;
            if(!crossing&&Mod.Settings.EuropeanMarkings&&Mod.Settings.ThemeAssetEdgeColors&&NodeLinePolicy.IsLineGeometry(prefab)&&prefab.name.IndexOf("Edge",StringComparison.OrdinalIgnoreCase)>=0)
                themedEdge=lineSystem.ThemeEquivalent(prefabEntity);
            bool adaptEdge=themedEdge!=Entity.Null&&themedEdge!=prefabEntity;
            if (!crossing && !center && !yellow && !medianOwned && !adaptEdge) { Remove(source); return; }

            if (!_replacements.ContainsKey(source) && EntityManager.HasComponent<Game.Tools.Hidden>(source)) { Defer(source); return; }

            _deferred.Remove(source);_retryDue.Remove(source);

            var curve = EntityManager.GetComponentData<Curve>(source).m_Bezier;

            var curves = new List<Bezier4x3>(); Entity paintPrefab = adaptEdge?themedEdge:yellow?lineSystem.ResolveCenterStyle(prefabEntity):prefabEntity;

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

                    var edge = EntityManager.GetComponentData<Game.Net.Edge>(owner);

                    var midpoint = MathUtils.Position(curve, .5f);

                    if (EntityManager.HasComponent<Node>(edge.m_Start) && EntityManager.HasComponent<Node>(edge.m_End)) crossingNode = math.distance(midpoint, EntityManager.GetComponentData<Node>(edge.m_Start).m_Position) < math.distance(midpoint, EntityManager.GetComponentData<Node>(edge.m_End).m_Position) ? edge.m_Start : edge.m_End;

                }

                if (!marking.TryClipCrosswalk(crossingNode, first, out first, crossingRoad) || !marking.TryClipCrosswalk(crossingNode, second, out second, crossingRoad))

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

                JoinInnerCrosswalk(crossingNode,ref first,ref second);
                _clipFailures.Remove(source);_clipRetries.Remove(source); curves.Add(first); curves.Add(second);

            }

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

            if (same) {if(crossing)choices.NoteSource(source,invisible?"Invisible crossing applied.":"Lined crossing rendered.");return;}

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

            next.Keys.AddRange(keys); _replacements[source] = next;

            EntityManager.AddComponent<MGRReplacedPaint>(source);

            if(routedCrossing) World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().Suppress(source);
            else EntityManager.AddComponent<Game.Tools.Hidden>(source); RenderChanged(source);
            if(crossing)choices.NoteSource(source,invisible?"Invisible crossing applied.":"Lined crossing rendered.");

        }

        protected override void OnUpdate()
        {
            long started=ModDiagnostics.RuntimeCosts.Begin();
            try{UpdateMeasured();}finally{ModDiagnostics.RuntimeCosts.End("IntersectionPaintSystem",started);}
        }
        private void UpdateMeasured()

        {

            if (Mod.Saving) return;

            if (!_active || Mod.Settings == null) return;

            try

            {

                if (_refresh) { _initial = true; _refresh = false; }

                Dependency.Complete(); FindWhite();

                var catalog=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();catalog.RefreshDefinitionsIfNeeded();

                if(_previousCenterStyle!=Mod.Settings.CenterLineStyle||_previousYellow!=Mod.Settings.YellowCenterLines||_catalogVersion!=CurbLineEmissionSystem.CatalogRevision){_previousCenterStyle=Mod.Settings.CenterLineStyle;_previousYellow=Mod.Settings.YellowCenterLines;_catalogVersion=CurbLineEmissionSystem.CatalogRevision;_initial=true;}

                var watch = System.Diagnostics.Stopwatch.StartNew();

                var pending = _pending; pending.Clear(); foreach (var item in _backlog) pending.Add(item); _backlog.Clear();

                foreach (var source in _requestedSources) pending.Add(source); _requestedSources.Clear();

                int retryBudget = 16;

                int retryVisits=math.min(_retryOrder.Count,retryBudget);
                while(retryVisits-->0)
                {
                    var source=_retryOrder.Dequeue();
                    if(!_deferred.Contains(source))continue;
                    if(_retryDue.TryGetValue(source,out float due)&&UnityEngine.Time.realtimeSinceStartup<due){_retryOrder.Enqueue(source);continue;}
                    _deferred.Remove(source);_retryDue.Remove(source);pending.Add(source);
                }
                using(var roads=_changedGeometry.ToEntityArray(Allocator.Temp))foreach(var road in roads)
                {
                    QueueOwnerPaint(road,pending);var edge=EntityManager.GetComponentData<Game.Net.Edge>(road);
                    QueueOwnerPaint(edge.m_Start,pending);QueueOwnerPaint(edge.m_End,pending);
                }

                var affected = _affected; affected.Clear();

                if (!_initial)

                    using (var owners = _changedOwners.ToEntityArray(Allocator.Temp))

                        foreach (var owner in owners)

                        {

                            if(EntityManager.HasComponent<Node>(owner)) World.GetOrCreateSystemManaged<CrosswalkUpgradeSystem>().PruneNodeIntents(owner);

                            foreach (var entry in EntityManager.GetBuffer<Game.Net.SubLane>(owner, true))

                            { pending.Add(entry.m_SubLane); affected.Add(entry.m_SubLane); }

                        }

                using (var visibility = new EntityCommandBuffer(Allocator.Temp))

                {

                    foreach (var pair in _replacements)

                    {

                        if (!Alive(pair.Key)) { pending.Add(pair.Key); continue; }
                        if(_externalPaint.Contains(EntityManager,pair.Key)){pending.Add(pair.Key);continue;}

                        Entity owner = EntityManager.GetComponentData<Owner>(pair.Key).m_Owner;

                        if (!Alive(owner)) { pending.Add(pair.Key); continue; }

                        if (EntityManager.HasComponent<Game.Tools.Hidden>(owner))

                        {

                            foreach (var paint in pair.Value.Paint) if (Alive(paint) && !EntityManager.HasComponent<Game.Tools.Hidden>(paint)) { visibility.AddComponent<Game.Tools.Hidden>(paint); if (!EntityManager.HasComponent<BatchesUpdated>(paint)) visibility.AddComponent<BatchesUpdated>(paint); }

                            continue;

                        }

                        foreach (var paint in pair.Value.Paint) if (Alive(paint) && EntityManager.HasComponent<Game.Tools.Hidden>(paint)) { visibility.RemoveComponent<Game.Tools.Hidden>(paint); if (!EntityManager.HasComponent<BatchesUpdated>(paint)) visibility.AddComponent<BatchesUpdated>(paint); }

                        bool intact = true; foreach (var entity in pair.Value.Paint) if (!Alive(entity)) intact = false;

                        if (!intact) { pending.Add(pair.Key); continue; }

                        if(pair.Value.RoutedCrossing) World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().Suppress(pair.Key);
                        else if (!EntityManager.HasComponent<Game.Tools.Hidden>(pair.Key))

                        {

                            visibility.AddComponent<Game.Tools.Hidden>(pair.Key);

                            if (!EntityManager.HasComponent<BatchesUpdated>(pair.Key)) visibility.AddComponent<BatchesUpdated>(pair.Key);

                        }

                    }

                    visibility.Playback(EntityManager);

                }

                _sources.ResetFilter();

                if (!_initial) _sources.SetChangedVersionFilter(new[] { ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>() });

                try { using (var sources = _sources.ToEntityArray(Allocator.Temp)) foreach (var source in sources) if (!_replacements.TryGetValue(source, out var tracked) || affected.Contains(source) || _initial || !tracked.SourceKey.Equals(new MarkingKey(EntityManager.GetComponentData<Curve>(source).m_Bezier, World.GetOrCreateSystemManaged<CrosswalkVisualSystem>().Original(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab)))) pending.Add(source); }

                finally { _sources.ResetFilter(); }

                int processed = 0;

                foreach (var source in pending)

                {

                    if (processed >= 128 || (processed >= 8 && watch.Elapsed.TotalMilliseconds >= 2)) { _backlog.Add(source); continue; }

                    Reconcile(source); processed++;

                }

                watch.Stop();

                if (_initial) Mod.Debug($"Intersection paint: {_replacements.Count} native sources replaced; explicit crossing choices reconciled.");

                _initial = false;

            }

            catch (Exception error) { Restore(); Enabled = false; Mod.Log.Error("Intersection paint stopped: " + error); }

        }

    }

}

