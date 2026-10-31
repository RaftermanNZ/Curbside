using System;

using System.Collections.Generic;

using Colossal.Mathematics;

using Colossal.Serialization.Entities;

using Game;

using Game.Common;

using Game.Net;

using Game.Pathfind;

using Game.UI.Widgets;

using Game.Prefabs;

using Unity.Collections;

using Unity.Entities;

using Unity.Mathematics;

using Edge = Game.Net.Edge;

using SubLane = Game.Net.SubLane;



namespace MedianGrassReplacement.Systems

{

    // Only new runtime marking entities receive this tag. It lets a loaded

    // scene discard our derived markings and reconstruct them from road data.

    public struct MGRCurbMarking : IComponentData, IEmptySerializable { }



    public partial class CurbLineEmissionSystem : GameSystemBase, Game.Serialization.IPreSerialize

    {

        private struct MarkCurve

        {

            internal Bezier4x3 Curve;

            internal Entity Prefab; internal Entity NativeSource;

            internal float Width; internal bool Median,Curb,CenterUpgrade,DividerUpgrade,Protected;

            internal MarkCurve(Bezier4x3 curve, Entity prefab, float width = 0f,bool median=false,bool curb=false) { Curve = curve; Prefab = prefab; NativeSource=Entity.Null; Width = width; Median=median; Curb=curb; CenterUpgrade=false;DividerUpgrade=false;Protected=false; }

        }

        private struct StopMark

        {

            internal Bezier4x3 Curve;

            internal Entity Prefab;

            internal bool AtStart;

            internal float Reach;

        }

        private readonly HashSet<Entity> _stopPrefabs = new HashSet<Entity>();

        private sealed class RoadMarks

        {

            internal int Signature;

            internal readonly List<Entity> Entities = new List<Entity>();

            internal readonly List<MarkCurve> Curves = new List<MarkCurve>();

        }



        private struct JoinCap

        {

            internal Entity Node,Prefab;

            internal float3 Point, Outward;

            internal int Kind,Side,Boundary;

        }

        private readonly Dictionary<Entity, List<JoinCap>> _caps = new Dictionary<Entity, List<JoinCap>>();

        private readonly Dictionary<Entity, RoadMarks> _joins = new Dictionary<Entity, RoadMarks>();

        private readonly HashSet<Entity> _dirtyNodes = new HashSet<Entity>();



        private PrefabSystem _prefabs;

        private EntityQuery _secondaryDefinitions;

        private int _definitionCount = -1;

        internal bool IsStopDefinition(Entity prefab) => _stopPrefabs.Contains(prefab);

        internal void RefreshDefinitionsIfNeeded()

        {

            int count = _lanePrefabs.CalculateEntityCount() + _secondaryDefinitions.CalculateEntityCount();

            if (_catalogRequested || count != _definitionCount) { RefreshCatalog(); _definitionCount = count; }

        }

        private EntityQuery _joinNodes;
        private EntityQuery _roads, _changedRoads, _changedLaneRoads, _deletedRoads, _owned, _deletedOwned, _lanePrefabs;

        private Entity _linePrefab;

        private bool _previousMedian,_previousYellow,_previousMedianAlignment; private string _previousCenterStyle; private float _previousMedianOffset=-1,_previousMedianGap=-1; private string _previousMedianStyle;

        private readonly Dictionary<string,Entity> _nativeLineDefinitions=new Dictionary<string,Entity>(StringComparer.Ordinal);

        private readonly Dictionary<string,Entity> _yellowMeshes=new Dictionary<string,Entity>(StringComparer.Ordinal);

        private Entity _solidYellow;

        internal Entity YellowEquivalent(Entity source)

        {

            if(!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(source,out var prefab)||prefab==null)return source;

            if(prefab.name.StartsWith("EU ",StringComparison.Ordinal)&&prefab.name.IndexOf("Center Line",StringComparison.Ordinal)>=0&&_nativeLineDefinitions.TryGetValue("NA "+prefab.name.Substring(3),out var native))return native;

            var privateVariant=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Variant(source);if(privateVariant!=Entity.Null)return privateVariant;

            var key=PaintMeshKey(prefab,false);if(key==null)return source;

            var yellow=PaintMeshKey(prefab,true);if(key==yellow)return source;

            if(_yellowMeshes.TryGetValue(yellow,out var match))return match;

            return source;

        }



        internal Entity ThemeEquivalent(Entity source)
        {
            if(!Mod.Settings.EuropeanMarkings)return YellowEquivalent(source);
            var palette=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>();var white=palette.WhiteVariant(source);if(white!=Entity.Null)return white;
            if(_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(source,out var prefab)&&prefab!=null)
            {
                byte shape=PaintThemePolicy.WhiteShape(PaintMeshKey(prefab,false)??prefab.name);
                if(shape!=0){var equivalent=shape==3?palette.Divider:palette.Explicit(shape,false);if(equivalent!=Entity.Null)return equivalent;}
            }
            return source;
        }
        internal Entity AutomaticMedianAsset()=>World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Explicit(1,!Mod.Settings.EuropeanMarkings);
        internal Entity NativeJunctionColor(Entity source,bool opposing,bool edge)
        {
            // Only exact mesh-family color variants. Never fall back to a
            // generic solid/dashed definition or the configured center style.
            byte target=NativeJunctionColorPolicy.Target(opposing,edge,Mod.Settings.EuropeanMarkings,Mod.Settings.ThemeAssetEdgeColors);
            if(target==0)return source;
            var palette=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>();
            Entity color=target==1?palette.WhiteVariant(source):target==2?palette.Variant(source):Entity.Null;
            if(ModDiagnostics.ActionJournal.Active)ModDiagnostics.ActionJournal.Record("line.colorDecision","sourcePrefab="+source+" opposing="+opposing+" edge="+edge+" targetColor="+target+" variant="+color+" missingVariant="+(target!=0&&color==Entity.Null));
            return color==Entity.Null?source:color;
        }
        internal Entity ResolveCenterStyle(Entity source)

        {

            if(Mod.Settings.CenterLineStyle=="Vanilla")return Mod.Settings.EuropeanMarkings?ThemeEquivalent(source):source;

            if(string.IsNullOrEmpty(Mod.Settings.CenterLineStyle))return ThemeEquivalent(source);

            var chosen=_nativeLineDefinitions.TryGetValue(Mod.Settings.CenterLineStyle,out var asset)?asset:source;
            return Mod.Settings.EuropeanMarkings?ThemeEquivalent(chosen):chosen;

        }





        private static string PaintMeshKey(NetLaneGeometryPrefab prefab,bool yellow)

        {

            if(prefab.m_Meshes==null||prefab.m_Meshes.Length==0)return null;

            var names=new List<string>();foreach(var mesh in prefab.m_Meshes){if(mesh?.m_Mesh==null)continue;names.Add(yellow?NorthAmericanLinePolicy.YellowMeshKey(mesh.m_Mesh.name):mesh.m_Mesh.name);}

            return names.Count==0?null:string.Join("|",names);

        }

        internal List<NorthAmericanLinePolicy.Traffic> TrafficLanes(Entity composition)

        {

            var result=new List<NorthAmericanLinePolicy.Traffic>();if(!EntityManager.HasBuffer<NetCompositionLane>(composition))return result;

            foreach(var lane in EntityManager.GetBuffer<NetCompositionLane>(composition,true))

            {

                if((lane.m_Flags&(LaneFlags.Road|LaneFlags.Master|LaneFlags.Underground|LaneFlags.Twoway))!=LaneFlags.Road||!EntityManager.HasComponent<NetLaneData>(lane.m_Lane))continue;

                var data=EntityManager.GetComponentData<NetLaneData>(lane.m_Lane);if(!math.all(math.isfinite(lane.m_Position))||data.m_Width<=0)continue;

                result.Add(new NorthAmericanLinePolicy.Traffic(lane.m_Position.x,data.m_Width,lane.m_Position.y,(lane.m_Flags&LaneFlags.Invert)!=0));

            }return result;

        }

        internal bool IsOuterEdgePaint(Entity road,Bezier4x3 curve)
        {
            if(!EntityManager.HasComponent<Composition>(road)||!EntityManager.HasComponent<EdgeGeometry>(road))return false;
            var composition=EntityManager.GetComponentData<Composition>(road).m_Edge;
            if(!EntityManager.HasComponent<NetCompositionData>(composition)||!EntityManager.HasBuffer<NetCompositionLane>(composition))return false;
            float width=EntityManager.GetComponentData<NetCompositionData>(composition).m_Width;
            var lanes=EntityManager.GetBuffer<NetCompositionLane>(composition,true);
            var anchors=ReadCurbAnchors(composition,lanes);
            if(anchors.Count<2)anchors=ReadDecalAnchors(composition,width,lanes);
            if(anchors.Count<2)return false;
            anchors.Sort((a,b)=>a.x.CompareTo(b.x));
            var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);
            for(int i=0;i<3;i++)
            {
                float x=RoadMarkingPolicy.Lateral(geometry,MathUtils.Position(curve,i*.5f),width);
                if(!math.isfinite(x)||math.min(math.abs(x-anchors[0].x),math.abs(x-anchors[anchors.Count-1].x))>math.max(.4f,math.abs(Mod.Settings.CurbLineOffset)+.2f))return false;
            }
            return true;
        }

        internal bool IsOpposingBoundary(Entity road,Bezier4x3 curve)

        {

            if(!EntityManager.HasComponent<Composition>(road)||!EntityManager.HasComponent<EdgeGeometry>(road))return false;

            var composition=EntityManager.GetComponentData<Composition>(road).m_Edge;if(!EntityManager.HasComponent<NetCompositionData>(composition))return false;

            var g=EntityManager.GetComponentData<EdgeGeometry>(road);var point=MathUtils.Position(curve,.5f);float best=float.MaxValue,x=0;

            for(int half=0;half<2;half++)

            {var left=half==0?g.m_Start.m_Left:g.m_End.m_Left;var right=half==0?g.m_Start.m_Right:g.m_End.m_Right;var middle=MathUtils.Lerp(left,right,.5f);float distance=MathUtils.Distance(middle,point,out float at);if(distance>=best)continue;best=distance;var a=MathUtils.Position(left,at);var b=MathUtils.Position(right,at);var across=b.xz-a.xz;float length=math.lengthsq(across);if(length<.01f)continue;x=(math.dot(point.xz-a.xz,across)/length-.5f)*EntityManager.GetComponentData<NetCompositionData>(composition).m_Width;}

            return NorthAmericanLinePolicy.YellowBoundary(x,TrafficLanes(composition));

        }

        internal bool IsYellowNodeBoundary(Entity node,Bezier4x3 curve)=>TryNodeBoundaryRoads(node,curve,out var first,out var last)&&first!=last;
        internal bool IsYellowNativeNodeBoundary(Entity node,Bezier4x3 curve)=>TryNodeBoundaryRoads(node,curve,out _,out _);
        internal bool OwnsNodeCenterRole(Entity node,Bezier4x3 curve)
        {
            if(AutomaticMarkingPolicy.PreservesNode(EntityManager,node)&&!OwnsExplicitNodeCenter(node))return false;
            if(!HasCenterNodeJoin(node)||!EntityManager.HasBuffer<ConnectedEdge>(node)||EntityManager.GetBuffer<ConnectedEdge>(node,true).Length!=2)return false;
            return TryNodeBoundaryRoads(node,curve,out var first,out var last)&&first!=last;
        }
        internal NodePaintMovement NodeMovement(Entity node,Bezier4x3 curve)
        {
            if(!TryNodeBoundaryRoads(node,curve,out var first,out var last,false))return NodePaintMovement.Unknown;
            float2 Outward(Entity road)
            {
                if(!EntityManager.HasComponent<Edge>(road)||!EntityManager.HasComponent<Curve>(road))return default;
                var edge=EntityManager.GetComponentData<Edge>(road);var path=EntityManager.GetComponentData<Curve>(road).m_Bezier;
                return edge.m_Start==node?MathUtils.Tangent(path,0).xz:edge.m_End==node?-MathUtils.Tangent(path,1).xz:default;
            }
            return NodeMovementPolicy.Classify(first==last,Outward(first),Outward(last));
        }
        internal bool OwnsNodeUTurnPaint(Entity node,Bezier4x3 curve)
        {
            if(AutomaticMarkingPolicy.PreservesNode(EntityManager,node))return false;
            if(!Mod.Settings.MedianEdgeLines||!HasCenterNodeJoin(node)||!EntityManager.HasBuffer<ConnectedEdge>(node))return false;
            int roads=0;
            foreach(var entry in EntityManager.GetBuffer<ConnectedEdge>(node,true))
                if(EntityManager.HasComponent<Road>(entry.m_Edge)&&!EntityManager.HasComponent<Deleted>(entry.m_Edge)&&!EntityManager.HasComponent<Game.Tools.Temp>(entry.m_Edge))roads++;
            if(roads!=2||!TryNodeBoundaryRoads(node,curve,out var first,out var last,false)||first!=last||!_caps.TryGetValue(first,out var caps))return false;
            // A custom median transition must exist; an open junction/dead end remains native.
            return caps.Exists(c=>c.Node==node&&c.Kind==2);
        }
        private bool TryNodeBoundaryRoads(Entity node,Bezier4x3 curve,out Entity first,out Entity last,bool requireYellow=true)
        {
            first=last=Entity.Null;
            if(!EntityManager.HasBuffer<ConnectedEdge>(node))return false;
            for(int end=0;end<2;end++)
            {
                float3 point=end==0?curve.a:curve.d;float best=36f;bool yellow=false,found=false;Entity matched=Entity.Null;
                var tangent=math.normalizesafe(MathUtils.Tangent(curve,end).xz);
                foreach(var connection in EntityManager.GetBuffer<ConnectedEdge>(node,true))
                {
                    var road=connection.m_Edge;
                    if(!EntityManager.HasComponent<Road>(road)||EntityManager.HasComponent<Deleted>(road)||!EntityManager.HasComponent<Edge>(road)||!EntityManager.HasComponent<EdgeGeometry>(road)||!EntityManager.HasComponent<Composition>(road))continue;
                    var edge=EntityManager.GetComponentData<Edge>(road);bool start=edge.m_Start==node;if(!start&&edge.m_End!=node)continue;
                    var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);var segment=start?geometry.m_Start:geometry.m_End;
                    var left=start?segment.m_Left.a:segment.m_Left.d;var right=start?segment.m_Right.a:segment.m_Right.d;
                    var across=right-left;float width2=math.lengthsq(across.xz);if(width2<.01f)continue;
                    float fraction=math.dot((point-left).xz,across.xz)/width2;
                    var nearest=math.lerp(left,right,math.clamp(fraction,0,1));float distance=math.distancesq(nearest.xz,point.xz);
                    var roadTangent=math.normalizesafe(MathUtils.Tangent(MathUtils.Lerp(segment.m_Left,segment.m_Right,.5f),start?0:1).xz);
                    if(distance>=best||math.abs(nearest.y-point.y)>.5f||math.abs(math.dot(tangent,roadTangent))<.7f)continue;
                    var composition=EntityManager.GetComponentData<Composition>(road).m_Edge;if(!EntityManager.HasComponent<NetCompositionData>(composition))continue;
                    float width=EntityManager.GetComponentData<NetCompositionData>(composition).m_Width;
                    best=distance;found=true;matched=road;yellow=NorthAmericanLinePolicy.YellowBoundary((fraction-.5f)*width,TrafficLanes(composition));
                }
                if(!found||requireYellow&&!yellow)return false;
                if(end==0)first=matched;else last=matched;
            }
            return true;
        }

        private float _previousOffset = -1f, _previousStopGap = -1f, _previousEndGap = -1f;

        private string _previousStyle;



        private float _previousBayLength = -1f;

        private bool _previousParkingCurbs = true;

        private bool _previousLock, _previousCurbLines;

        private static bool _catalogRequested = true;

        private static readonly HashSet<Entity> _externalDirtyRoads = new HashSet<Entity>();
        private float _nextPaintTiming,_nextJoinTiming;

        private readonly Queue<Entity> _auditRoads = new Queue<Entity>();

        private readonly HashSet<Entity> _parkingDirtyRoads = new HashSet<Entity>();

        internal static void QueueRoad(Entity road) { _externalDirtyRoads.Add(road); }

        private static bool _manualRefresh;

        internal static void RequestManualRefresh() { _manualRefresh = true; }

        private static bool _roadRefreshRequested;

        internal static void RequestRoadRefresh() { _roadRefreshRequested = true; }

        private static readonly SortedSet<string> _lineNames = new SortedSet<string>(StringComparer.Ordinal);

        internal static int CatalogRevision { get; private set; }

        internal static void RequestCatalogRefresh() { _catalogRequested = true; }

        internal static DropdownItem<string>[] GetLineChoices(string selected)

        {

            var names = new SortedSet<string>(_lineNames, StringComparer.Ordinal);

            names.Add("EU Highway Edge Line");

            if (!string.IsNullOrEmpty(selected)) names.Add(selected);

            var items = new List<DropdownItem<string>>();

            foreach (var name in names) items.Add(new DropdownItem<string> { value = name, displayName = name });

            return items.ToArray();

        }



        private bool IsLineAsset(Entity entity, out NetLaneGeometryPrefab prefab)

        {

            if (!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(entity, out prefab) || prefab == null || prefab.name.StartsWith("MGR ",StringComparison.Ordinal) || prefab.m_Meshes == null) return false;

            if (!EntityManager.HasComponent<NetLaneData>(entity)) return false;

            var flags = EntityManager.GetComponentData<NetLaneData>(entity).m_Flags;

            if ((flags & (LaneFlags.Road | LaneFlags.Parking | LaneFlags.Pedestrian | LaneFlags.Track)) != 0) return false;

            if (!EntityManager.GetComponentData<NetLaneArchetypeData>(entity).m_LaneArchetype.Valid) return false;

            foreach (var mesh in prefab.m_Meshes)

                if (mesh != null && mesh.m_Mesh != null && mesh.m_Mesh.name.IndexOf("Line", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;

        }






        private Entity AlleyAsset()
        {
            foreach(var pair in _nativeLineDefinitions)
                if(pair.Key.Replace(" ","").Replace("_","").Equals("EUHighwayEdgeLine",StringComparison.OrdinalIgnoreCase))return pair.Value;
            return World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Explicit(1,false);
        }

        private void RefreshCatalog()

        {

            _sectionMarkingPolicy.Clear();

            _stopPrefabs.Clear();

            _lineNames.Clear();_nativeLineDefinitions.Clear();_yellowMeshes.Clear();_solidYellow=Entity.Null;

            using (var entities = _lanePrefabs.ToEntityArray(Allocator.Temp))

                foreach (var entity in entities)

                {


                    if (IsLineAsset(entity, out var prefab))

                    {

                        _lineNames.Add(prefab.name);_nativeLineDefinitions[prefab.name]=entity;

                        if(prefab.name.StartsWith("EU ",StringComparison.Ordinal)||prefab.name.StartsWith("NA ",StringComparison.Ordinal))

                        {

                            var key=PaintMeshKey(prefab,false);if(key!=null&&key.IndexOf("Yellow",StringComparison.Ordinal)>=0)_yellowMeshes[key]=entity;

                            if(key=="Yellow Solid Line Mesh")_solidYellow=entity;

                        }

                    }

                    if (_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(entity, out var stopPrefab) && stopPrefab != null

                        && (stopPrefab.name == "EU Car Stop Line" || stopPrefab.name == "NA Car Stop Line")

                        && EntityManager.GetComponentData<NetLaneArchetypeData>(entity).m_LaneArchetype.Valid) _stopPrefabs.Add(entity);

                }

            using (var sources = _secondaryDefinitions.ToEntityArray(Allocator.Temp))

                foreach (var source in sources)

                    foreach (var secondary in EntityManager.GetBuffer<SecondaryNetLane>(source, true))

                        if ((secondary.m_Flags & (SecondaryNetLaneFlags.Crossing | SecondaryNetLaneFlags.RequireStop))

                            == (SecondaryNetLaneFlags.Crossing | SecondaryNetLaneFlags.RequireStop)

                            && EntityManager.HasComponent<NetLaneArchetypeData>(secondary.m_Lane)

                            && EntityManager.HasComponent<NetLaneGeometryData>(secondary.m_Lane)) _stopPrefabs.Add(secondary.m_Lane);

            CatalogRevision++;_initial=true;IntersectionPaintSystem.RequestRefresh();

            _catalogRequested = false;

        }

        private EntityArchetype _lineArchetype;

        private bool _active, _initial = true, _waitingLogged, _previousEnabled;

        private int _ownedCount;

        private readonly HashSet<Entity> _pendingRoads = new HashSet<Entity>();
        private readonly HashSet<Entity> _directRoadWork = new HashSet<Entity>(), _backlogRoadWork = new HashSet<Entity>();
        private readonly List<Entity> _deliveredParking = new List<Entity>(8), _retryRoadWork = new List<Entity>();
        private readonly List<Entity> _joinCandidates = new List<Entity>(8), _completedJoins = new List<Entity>(8);
        private readonly System.Diagnostics.Stopwatch _roadWatch = new System.Diagnostics.Stopwatch(), _joinWatch = new System.Diagnostics.Stopwatch();
        private static readonly ComponentType[] RoadGeometryChanges = { ComponentType.ReadOnly<EdgeGeometry>(), ComponentType.ReadOnly<Composition>() };

        private readonly Dictionary<Entity, RoadMarks> _marks = new Dictionary<Entity, RoadMarks>();



        protected override void OnCreate()

        {

            base.OnCreate();

            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();

            var all = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Road>(),

                ComponentType.ReadOnly<EdgeGeometry>(), ComponentType.ReadOnly<Composition>(), ComponentType.ReadOnly<SubLane>() };

            var none = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() };

            _roads = GetEntityQuery(new EntityQueryDesc { All = all, None = none });

            _changedRoads = GetEntityQuery(new EntityQueryDesc { All = all, None = none });

            _changedLaneRoads = GetEntityQuery(new EntityQueryDesc { All = all, None = none });

            _joinNodes=GetEntityQuery(ComponentType.ReadOnly<Node>(),ComponentType.ReadOnly<ConnectedEdge>(),ComponentType.ReadOnly<SubLane>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
            _joinNodes.SetChangedVersionFilter(new[]{ComponentType.ReadOnly<ConnectedEdge>(),ComponentType.ReadOnly<SubLane>()});
            _deletedRoads = GetEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Deleted>());

            _owned = GetEntityQuery(new EntityQueryDesc

            {

                All = new[] { ComponentType.ReadOnly<MGRCurbMarking>() },

                None = new[] { ComponentType.ReadOnly<Deleted>() }

            });

            _deletedOwned = GetEntityQuery(ComponentType.ReadOnly<MGRCurbMarking>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>());

            _secondaryDefinitions = GetEntityQuery(ComponentType.ReadOnly<SecondaryNetLane>());

            _lanePrefabs = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<NetLaneArchetypeData>(), ComponentType.Exclude<MGRPrivatePaintDefinition>());

        }



        public void PreSerialize(Colossal.Serialization.Entities.Context context) { ClearOwned(); _initial = true; }

        protected override void OnGamePreload(Purpose purpose, GameMode mode) { base.OnGamePreload(purpose, mode); _active = false; _diagnosticPaintTarget=Entity.Null; _nodeDividerExternal.Clear(); _externalPaint.Clear(); ClearOwned(); _catalogRequested=true; _definitionCount=-1; }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)

        {

            base.OnGameLoadingComplete(purpose, mode);

            ClearOwned();

            _active = (mode & GameMode.GameOrEditor) != GameMode.None;

            _initial = true;

            _linePrefab = Entity.Null;

            _waitingLogged = false;

            _catalogRequested = true;

        }



        protected override void OnUpdate()
        {
            long started=ModDiagnostics.RuntimeCosts.Begin();
            try{UpdateMeasured();}finally{ModDiagnostics.RuntimeCosts.End("CurbLineEmissionSystem",started);ModDiagnostics.RuntimeCosts.Workload("MarkedRoads",_marks.Count,_pendingRoads.Count+_dirtyNodes.Count);}
        }
        private void UpdateMeasured()

        {

            try { UpdateMarkings(); }

            catch (Exception error)

            {

                try { ClearOwned(); } catch (Exception cleanup) { Mod.Log.Warn("Curb-line cleanup: " + cleanup.Message); }

                Enabled = false;

                Mod.Log.Error("Runtime curb-line emission paused: " + error);

            }

        }



        private void UpdateMarkings()

        {

            if(Mod.Saving) return;

            if (_manualRefresh && _active)

            {

                // Reconcile against the current entities. A refresh must never

                // delete visible paint before replacement geometry is ready.

                _initial = true; _catalogRequested = true;

                _sectionMarkingPolicy.Clear();

                _manualRefresh = false;

                Mod.Debug("Manual road-line refresh requested.");

            }

            if (_active) RefreshDefinitionsIfNeeded();

            var directRoads=_directRoadWork;directRoads.Clear();directRoads.UnionWith(_externalDirtyRoads);

            _externalDirtyRoads.Clear();

            if (_roadRefreshRequested) { _initial = true; _linePrefab=Entity.Null; _roadRefreshRequested = false; }

            if (_active && Mod.Settings != null &&

                (_previousOffset != Mod.Settings.CurbLineOffset || _previousStyle != Mod.Settings.CurbLineStyle

                    || _previousStopGap != Mod.Settings.StopLineCurbGap || _previousEndGap != Mod.Settings.CurbLineStopGap || _previousLock != Mod.Settings.LockCurbAndStopLines

                    || _previousParkingCurbs != Mod.Settings.CurbLinesBesideParking

                    || _previousCurbLines != Mod.Settings.CurbWhiteLines

                    || _previousBayLength != Mod.Settings.FixedParkingBayLength

                    || _previousMedianAlignment!=Mod.Settings.AlignMedianStopBar||_previousCenterStyle!=Mod.Settings.CenterLineStyle

                    || _previousYellow!=Mod.Settings.YellowCenterLines|| _previousMedian!=Mod.Settings.MedianEdgeLines||_previousMedianOffset!=Mod.Settings.MedianLineOffset||_previousMedianGap!=Mod.Settings.MedianLineSetback||_previousMedianStyle!=Mod.Settings.MedianLineStyle))

            {

                Dependency.Complete();

                // Retain old entities until reconciliation can reuse or replace them.

                _linePrefab = Entity.Null;

                _waitingLogged = false;

                _initial = true;

                IntersectionPaintSystem.RequestRefresh();

                _previousMedianAlignment=Mod.Settings.AlignMedianStopBar;_previousCenterStyle=Mod.Settings.CenterLineStyle;

                _previousYellow=Mod.Settings.YellowCenterLines;_previousMedian=Mod.Settings.MedianEdgeLines;_previousMedianOffset=Mod.Settings.MedianLineOffset;_previousMedianGap=Mod.Settings.MedianLineSetback;_previousMedianStyle=Mod.Settings.MedianLineStyle;

                _previousOffset = Mod.Settings.CurbLineOffset;

                _previousStopGap = Mod.Settings.StopLineCurbGap;

                _previousEndGap = Mod.Settings.CurbLineStopGap;

                _previousLock = Mod.Settings.LockCurbAndStopLines;





                _previousCurbLines = Mod.Settings.CurbWhiteLines;

                _previousParkingCurbs = Mod.Settings.CurbLinesBesideParking;



                _previousBayLength = Mod.Settings.FixedParkingBayLength;

                _previousStyle = Mod.Settings.CurbLineStyle;

            }

            bool enabled = _active && Mod.Settings != null;

            if (!enabled)

            {

                if (_previousEnabled) ClearOwned();

                _previousEnabled = false;

                return;

            }

            if (!_previousEnabled) _initial = true;

            _previousEnabled = true;



            // Stationary bay markings do not follow occupancy changes.

            // Use deletion events instead of checking every painted entity on

            // every frame. Unexpected direct destruction gets a bounded audit.

            using (var deleted = _deletedOwned.ToEntityArray(Allocator.Temp))

                foreach (var entity in deleted)

                {

                    Entity road = EntityManager.GetComponentData<Owner>(entity).m_Owner;

                    if (_marks.TryGetValue(road, out var marks) && marks.Entities.Contains(entity)) _pendingRoads.Add(road);

                    if (_joins.ContainsKey(road)) _dirtyNodes.Add(road);

                }

            int countNow = _owned.CalculateEntityCount();

            if (countNow < _ownedCount && _auditRoads.Count == 0)

                foreach (var road in _marks.Keys) _auditRoads.Enqueue(road);

            _ownedCount = countNow;

            int auditBudget = 8;

            while (_auditRoads.Count > 0 && auditBudget-- > 0)

            {

                Entity road = _auditRoads.Dequeue();

                if (!_marks.TryGetValue(road, out var marks)) continue;

                if (!EntityManager.Exists(road) || EntityManager.HasComponent<Deleted>(road)) { _pendingRoads.Add(road); continue; }

                foreach (var marking in marks.Entities)

                    if (!EntityManager.Exists(marking) || EntityManager.HasComponent<Deleted>(marking)

                        || !EntityManager.HasComponent<Curve>(marking)) { _pendingRoads.Add(road); break; }

            }

            using (var deleted = _deletedRoads.ToEntityArray(Allocator.Temp))

                foreach (var road in deleted) RemoveRoad(road);

            int parkingBudget = 8;

            var delivered = _deliveredParking;delivered.Clear();

            foreach (var road in _parkingDirtyRoads) { if (parkingBudget-- <= 0) break; _pendingRoads.Add(road); delivered.Add(road); }

            foreach (var road in delivered) _parkingDirtyRoads.Remove(road);

            // Identical GetEntityQuery descriptions can share one cached query.

            // Apply each filter only while collecting that snapshot, then reset.

            var backlog=_backlogRoadWork;backlog.Clear();backlog.UnionWith(_pendingRoads);_pendingRoads.Clear();
            var pending=directRoads;
            try
            {
                _roads.SetChangedVersionFilter(RoadGeometryChanges);
                using(var roads=_roads.ToEntityArray(Allocator.Temp))foreach(var road in roads)pending.Add(road);
                _roads.SetChangedVersionFilter(ComponentType.ReadOnly<SubLane>());
                using(var roads=_roads.ToEntityArray(Allocator.Temp))foreach(var road in roads)pending.Add(road);
            }
            finally{_roads.ResetFilter();}
            if(_initial)using(var roads=_roads.ToEntityArray(Allocator.Temp))foreach(var road in roads)pending.Add(road);
            foreach(var road in backlog)pending.Add(road);
            using(var changedNodes=_joinNodes.ToEntityArray(Allocator.Temp))foreach(var node in changedNodes)
            {
                _dirtyNodes.Add(node);
                foreach(var connection in EntityManager.GetBuffer<ConnectedEdge>(node,true))
                    if(EntityManager.HasComponent<Road>(connection.m_Edge)&&!EntityManager.HasComponent<Deleted>(connection.m_Edge)&&!EntityManager.HasComponent<Game.Tools.Temp>(connection.m_Edge))pending.Add(connection.m_Edge);
            }
            if (pending.Count == 0 && _dirtyNodes.Count == 0) return;

            if (!ResolveLine()) { foreach (var road in pending) _pendingRoads.Add(road); return; }

            Dependency.Complete();



            {

                var rebuildWatch=_roadWatch;rebuildWatch.Restart();int rebuilt=0;var retryRoads=_retryRoadWork;retryRoads.Clear();

                foreach (var road in pending)

                {

                    if(rebuilt>=32||(rebuilt>=2&&rebuildWatch.Elapsed.TotalMilliseconds>=2)){_pendingRoads.Add(road);continue;}rebuilt++;

                    if (!EntityManager.Exists(road) || EntityManager.HasComponent<Deleted>(road)) { RemoveRoad(road); continue; }

                    if (!EntityManager.HasComponent<EdgeGeometry>(road) || !EntityManager.HasComponent<Composition>(road)

                        || !EntityManager.HasBuffer<SubLane>(road)) { retryRoads.Add(road); continue; }

                    var curves = BuildCurves(road);

                    if (curves == null) { retryRoads.Add(road); continue; } // Geometry not ready: retain current state.

                    int signature = 17+EdgeUpgradePolicy.Mode(EntityManager,road,true)*7919+EdgeUpgradePolicy.Mode(EntityManager,road,false)*15401+CenterlineUpgradePolicy.Mode(EntityManager,road)*101+SegmentUpgradePolicy.Mode(EntityManager,road)*1009+LaneDividerPolicy.Mode(EntityManager,road)*2003+SectionEdgePolicy.Mode(EntityManager,road)*4001;

                    foreach (var curve in curves)

                        unchecked

                        {

                            signature = signature * 31 + StablePointHash(curve.Curve.a); signature = signature * 31 + StablePointHash(curve.Curve.b);

                            signature = signature * 31 + StablePointHash(curve.Curve.c); signature = signature * 31 + StablePointHash(curve.Curve.d);

                            signature = signature * 31 + curve.Prefab.GetHashCode(); signature=signature*31+(curve.Median?1:0);

                        }

                    if (_marks.TryGetValue(road, out var existing) && existing.Signature == signature && existing.Entities.Count == curves.Count)

                    {

                        bool intact = true;

                        foreach (var entity in existing.Entities)

                            if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity)) { intact = false; break; }

                        if (intact) {QueueNativeEdges(road);continue;}

                    }

                    if (curves.Count == 0) { if(existing!=null)foreach(var entity in existing.Entities)GeneratedVisualLifecycle.Retire(World,entity);_marks.Remove(road);QueueNativeEdges(road);continue; }

                    
                var reusable = new Dictionary<MarkingKey, Queue<Entity>>();

                    if (existing != null)

                        for (int i = 0; i < existing.Entities.Count && i < existing.Curves.Count; i++)

                        {

                            Entity entity = existing.Entities[i];

                            if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity)) continue;

                            if (!EntityManager.HasComponent<Curve>(entity) || !Valid(EntityManager.GetComponentData<Curve>(entity).m_Bezier))

                            { GeneratedVisualLifecycle.Retire(World,entity); continue; }

                            var key = new MarkingKey(existing.Curves[i].Curve, existing.Curves[i].Prefab);

                            if (!reusable.TryGetValue(key, out var queue)) reusable[key] = queue = new Queue<Entity>();

                            queue.Enqueue(entity);

                        }

                    var next = new RoadMarks { Signature = signature };

                    foreach (var curve in curves)

                    {

                        var key = new MarkingKey(curve.Curve, curve.Prefab);

                        if (reusable.TryGetValue(key, out var queue) && queue.Count > 0) { next.Entities.Add(queue.Dequeue()); }

                        else { next.Entities.Add(Spawn(road, curve.Curve, curve.Prefab, true, curve.Width)); }

                        next.Curves.Add(curve);

                    }

                    foreach (var queue in reusable.Values)

                        foreach (var entity in queue) if (EntityManager.Exists(entity) && !EntityManager.HasComponent<Deleted>(entity)) GeneratedVisualLifecycle.Retire(World,entity);

                    _marks[road] = next;
                    if(ModDiagnostics.TraceBuffer.Enabled&&CenterlineUpgradePolicy.Mode(EntityManager,road)!=0){int emitted=next.Curves.FindAll(m=>m.CenterUpgrade).Count;ModDiagnostics.TraceBuffer.Record("center.emit",road+" style="+CenterlineUpgradePolicy.Mode(EntityManager,road)+" color="+(Mod.Settings.EuropeanMarkings?"White":"Yellow")+" visualCurves="+emitted+" (paired mesh only for double style)");}

                    QueueNativeEdges(road);

                }

                foreach(var retry in retryRoads)_pendingRoads.Add(retry);
                if(ModDiagnostics.TraceBuffer.Enabled&&UnityEngine.Time.realtimeSinceStartup>=_nextPaintTiming)
                {_nextPaintTiming=UnityEngine.Time.realtimeSinceStartup+1;ModDiagnostics.TraceBuffer.Record("paint.timing","roads="+rebuilt+" elapsedMs="+rebuildWatch.Elapsed.TotalMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" backlog="+_pendingRoads.Count);}

            }

            RebuildJoins();

            if (_initial) _parkingDirtyRoads.Clear();

            _initial = false;

            _ownedCount = _owned.CalculateEntityCount();

        }



        private void QueueNativeEdges(Entity road)

        {

            foreach(var lane in EntityManager.GetBuffer<SubLane>(road,true))

                if(EntityManager.HasComponent<PrefabRef>(lane.m_SubLane)&&_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(EntityManager.GetComponentData<PrefabRef>(lane.m_SubLane).m_Prefab,out var source)&&NodeLinePolicy.IsLineGeometry(source))IntersectionPaintSystem.RequestSource(lane.m_SubLane);

        }





        private static int StablePointHash(float3 point) => ((int3)math.round(point * 1000f)).GetHashCode();



        private bool ResolveLine()

        {

            if (_linePrefab != Entity.Null && EntityManager.Exists(_linePrefab)

                && EntityManager.HasComponent<NetLaneArchetypeData>(_linePrefab)) return true;

            if(!Mod.Settings.CurbWhiteLines&&Mod.Settings.AlleyWhiteEdges)
            {
                var native=AlleyAsset();
                if(native!=Entity.Null&&EntityManager.HasComponent<NetLaneArchetypeData>(native))
                {
                    var archetype=EntityManager.GetComponentData<NetLaneArchetypeData>(native).m_LaneArchetype;
                    if(archetype.Valid){_linePrefab=native;_lineArchetype=archetype;return true;}
                }
            }

            using (var entities = _lanePrefabs.ToEntityArray(Allocator.Temp))

                foreach (var entity in entities)

                {

                    if (!IsLineAsset(entity, out var prefab)) continue;

                    if (prefab.name != Mod.Settings.CurbLineStyle) continue;

                    var archetype = EntityManager.GetComponentData<NetLaneArchetypeData>(entity).m_LaneArchetype;

                    if (!archetype.Valid) continue;

                    _linePrefab = Mod.Settings.EuropeanMarkings&&Mod.Settings.ThemeAssetEdgeColors?ThemeEquivalent(entity):entity;

                    _lineArchetype = archetype;

                    Mod.Debug("Curb lines: using marking asset read-only: " + prefab.name);

                    return true;

                }

            // Optional curb style availability must not stall unrelated tools.
            {
                var fallback=AlleyAsset();if(fallback==Entity.Null)fallback=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Explicit(1,false);
                if(fallback!=Entity.Null&&EntityManager.HasComponent<NetLaneArchetypeData>(fallback))
                {
                    var archetype=EntityManager.GetComponentData<NetLaneArchetypeData>(fallback).m_LaneArchetype;
                    if(archetype.Valid){_linePrefab=fallback;_lineArchetype=archetype;if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("paint.fallback","Using native white edge paint; selected curb style unavailable.");return true;}
                }
            }
            if (!_waitingLogged) Mod.Log.Warn("Curb lines: selected marking asset unavailable or incompatible: " + Mod.Settings.CurbLineStyle);

            _waitingLogged = true;

            return false;

        }



        private readonly Dictionary<Entity, bool> _sectionMarkingPolicy = new Dictionary<Entity, bool>();

        private bool SectionUnmarked(Entity section)

        {

            if (_sectionMarkingPolicy.TryGetValue(section, out bool disabled)) return disabled;

            if (_prefabs.TryGetPrefab<NetSectionPrefab>(section, out var prefab) && prefab != null && prefab.components != null)

                foreach (var component in prefab.components)

                {

                    if (component == null) continue;

                    for (var type = component.GetType(); type != null; type = type.BaseType)

                        if (type.Name == "RoadBuilderLaneGroup") { disabled |= RoadMarkingPolicy.NoMarkings(component); break; }

                }

            _sectionMarkingPolicy[section] = disabled;

            return disabled;

        }



        private List<float2> UnmarkedSpans(Entity road, Entity composition, NetCompositionData data)

        {

            var spans = new List<float2>();

            if (!EntityManager.HasComponent<PrefabRef>(road) || !EntityManager.HasBuffer<NetCompositionPiece>(composition)) return spans;

            Entity definition = EntityManager.GetComponentData<PrefabRef>(road).m_Prefab;

            if (!EntityManager.HasBuffer<NetGeometrySection>(definition)) return spans;

            var sections = EntityManager.GetBuffer<NetGeometrySection>(definition, true);

            bool anyUnmarked = false;

            foreach (var section in sections) if (SectionUnmarked(section.m_Section)) { anyUnmarked = true; break; }

            if (!anyUnmarked) return spans;

            var pieces = EntityManager.GetBuffer<NetCompositionPiece>(composition, true);

            var subsections = GetBufferLookup<NetSubSection>(true);

            var sectionPieces = GetBufferLookup<NetSectionPiece>(true);

            int compactIndex = 0;

            bool inverted = (data.m_Flags.m_General & CompositionFlags.General.Invert) != 0;

            using (var single = new NativeArray<NetGeometrySection>(1, Allocator.Temp))

            using (var probe = new NativeList<NetCompositionPiece>(Allocator.Temp))

                for (int i = 0; i < sections.Length; i++)

                {

                    var section = sections[inverted ? sections.Length - 1 - i : i];

                    if (!EntityManager.HasBuffer<NetSubSection>(section.m_Section) || !EntityManager.HasBuffer<NetSectionPiece>(section.m_Section)) continue;

                    var writable = single; writable[0] = section; probe.Clear();

                    NetCompositionHelpers.GetCompositionPieces(probe, single, data.m_Flags, subsections, sectionPieces);

                    if (probe.Length == 0) continue;

                    bool disabled = SectionUnmarked(section.m_Section);

                    if (disabled)

                        foreach (var piece in pieces)

                            if (piece.m_SectionIndex == compactIndex)

                            {

                                float width = piece.m_Size.x;

                                if (width <= 0f && EntityManager.HasComponent<NetPieceData>(piece.m_Piece)) width = EntityManager.GetComponentData<NetPieceData>(piece.m_Piece).m_Width;

                                if (width > 0f) spans.Add(new float2(piece.m_Offset.x - width * 0.5f, piece.m_Offset.x + width * 0.5f));

                            }

                    compactIndex++;

                }

            return spans;

        }



        private List<MarkCurve> BuildCurves(Entity road)

        {

            var composition = EntityManager.GetComponentData<Composition>(road).m_Edge;

            if (!EntityManager.Exists(composition) || !EntityManager.HasComponent<NetCompositionData>(composition)

                || !EntityManager.HasBuffer<NetCompositionLane>(composition))

            {

                return null;

            }

            var data = EntityManager.GetComponentData<NetCompositionData>(composition);

            if (!math.isfinite(data.m_Width) || data.m_Width <= 0.1f) return null;

            var geometry = EntityManager.GetComponentData<EdgeGeometry>(road);

            var lanes = EntityManager.GetBuffer<NetCompositionLane>(composition, true);

            if (lanes.Length == 0) return null;

            // Missing optional paint must not block visibility/curb/parking changes.
            // Palette registration queues a refresh when its assets become usable.
            var curves = new List<MarkCurve>();

            var stops = ReadStopMarks(road);

            if (_caps.TryGetValue(road, out var oldCaps)) foreach (var cap in oldCaps) _dirtyNodes.Add(cap.Node);

            _caps[road] = new List<JoinCap>();

            var unmarked = UnmarkedSpans(road, composition, data);

            var anchors = ReadCurbAnchors(composition, lanes);

            anchors.Sort((a, b) => a.x.CompareTo(b.x));

            // Only the outside boundaries are selected. Any intermediate curb

            // anchors are left alone, so median/platform markings are not added.

            bool automatic=AutomaticMarkingPolicy.Allows(EntityManager,road)&&!AutomaticMarkingPolicy.PreservesGeometry(EntityManager,road);
            bool alley=RoadSurfacePolicy.IsAlleyForEdges(EntityManager,road);byte edgeMode=SectionEdgePolicy.Mode(EntityManager,road);
            if(edgeMode==1||Mod.Settings.AlleyWhiteEdges && alley || EntityManager.HasComponent<MGRParkingBanSides>(road))
                anchors=ReadDecalAnchors(composition,data.m_Width,lanes);
            anchors.Sort((a,b)=>a.x.CompareTo(b.x));
            for (int i = 0; i < anchors.Count; i++)

            {

                if (i != 0 && i != anchors.Count - 1) continue;

                bool left = anchors.Count == 1 ? anchors[i].x < 0f : i == 0;
                bool parkingBan=EntityManager.HasComponent<MGRParkingBanSides>(road)&&(EntityManager.GetComponentData<MGRParkingBanSides>(road).Mask&RoadSideUpgrades.SideMask(left))!=0;
                if(!automatic&&edgeMode!=1&&!parkingBan||edgeMode==2||RoadSurfacePolicy.IsHighway(EntityManager,road)||!RoadSurfacePolicy.IsPaved(EntityManager,road)||edgeMode!=1&&!parkingBan&&!WhiteEdgePolicy.Enabled(true,alley,Mod.Settings.CurbWhiteLines,Mod.Settings.AlleyWhiteEdges))continue;


                if (!CurbPlacement.TryMapComposed(data.m_Width, anchors[i], left, Mod.Settings.CurbLineOffset, out var position, out float t)) continue;

                if (edgeMode!=1 && RoadMarkingPolicy.Suppressed(position.x, unmarked)) continue;

                if (edgeMode!=1 && !parkingBan && !Mod.Settings.CurbLinesBesideParking && ParkingOnSide(road, geometry, data.m_Width, left)) continue;

                float x = position.x, y = position.y;

                var start = MathUtils.Lerp(geometry.m_Start.m_Left, geometry.m_Start.m_Right, t);

                var end = MathUtils.Lerp(geometry.m_End.m_Left, geometry.m_End.m_Right, t);

                start.a.y += y; start.b.y += y; start.c.y += y; start.d.y += y;

                end.a.y += y; end.b.y += y; end.c.y += y; end.d.y += y;

                var curbStart = MathUtils.Lerp(geometry.m_Start.m_Left, geometry.m_Start.m_Right, anchors[i].x / data.m_Width + 0.5f);

                var curbEnd = MathUtils.Lerp(geometry.m_End.m_Left, geometry.m_End.m_Right, anchors[i].x / data.m_Width + 0.5f);

                curbStart.a.y += y; curbStart.b.y += y; curbStart.c.y += y; curbStart.d.y += y;

                curbEnd.a.y += y; curbEnd.b.y += y; curbEnd.c.y += y; curbEnd.d.y += y;

                var centerStart = MathUtils.Lerp(geometry.m_Start.m_Left, geometry.m_Start.m_Right, 0.5f);

                var centerEnd = MathUtils.Lerp(geometry.m_End.m_Left, geometry.m_End.m_Right, 0.5f);

                var curbAsset=alley?AlleyAsset():(Mod.Settings.YellowCenterLines&&NorthAmericanLinePolicy.YellowBoundary(position.x,TrafficLanes(composition))?ThemeEquivalent(_linePrefab):_linePrefab);
                if(CenterlineUpgradePolicy.Mode(EntityManager,road)!=0&&NorthAmericanLinePolicy.YellowBoundary(position.x,TrafficLanes(composition))&&TryCenterUpgradePlan(road,CenterlineUpgradePolicy.Mode(EntityManager,road),out _,out _))continue;
                if(curbAsset==Entity.Null||CenterlineUpgradePolicy.Mode(EntityManager,road)==CenterlineUpgradePolicy.Removed&&NorthAmericanLinePolicy.YellowBoundary(position.x,TrafficLanes(composition)))continue;

                AddApproach(curves, start, curbStart, centerStart, stops, true, data.m_Width,curbAsset,!alley,-1);

                AddApproach(curves, end, curbEnd, centerEnd, stops, false, data.m_Width,curbAsset,!alley,-1);

                AddCaps(road, start, end, stops, 0,curbAsset);

            }



            if(automatic&&CenterlineUpgradePolicy.Mode(EntityManager,road)!=CenterlineUpgradePolicy.Removed)AddMedianMarks(road,composition,data,geometry,stops,curves,unmarked);

            AddCenterUpgrade(road,stops,curves);AddDividerMarks(road,stops,curves);
            if(automatic)AddHighwayMarks(road,composition,data,geometry,stops,curves,unmarked);
            if(automatic)AddNativeLineCaps(road,stops,composition,data,curves);
            byte hidden=SegmentUpgradePolicy.Mode(EntityManager,road);
            curves.RemoveAll(m=>!m.Protected&&!m.CenterUpgrade&&!m.DividerUpgrade&&SegmentUpgradePolicy.Hides(hidden,m.Median,m.Curb));
            if(_caps.TryGetValue(road,out var filteredCaps))filteredCaps.RemoveAll(c=>SegmentUpgradePolicy.Hides(hidden,c.Kind==2||c.Kind==3,c.Kind==0));
            AddExplicitEdges(road,stops,curves);ApplyDashedEdges(road,curves,false);

            if (EntityManager.HasComponent<MGRParkingUpgrade>(road) && EntityManager.GetComponentData<MGRParkingUpgrade>(road).Marked) AddParkingMarks(road, geometry, stops, curves, unmarked, data.m_Width);

            if(CenterlineVisibility.Hidden(EntityManager,road))curves.RemoveAll(m=>m.Median&&!m.Protected&&!m.CenterUpgrade);
            AddParkingHatching(road,curves);CanonicalCenterPairs(curves);
            // Additions after a hide action are independent; applying visibility
            // explicitly clears the matching placed intent before this rebuild.
            return curves;

        }







        private bool TrimMedian(Bezier4x3 line,List<StopMark> stops,out Bezier4x3 result)

        {

            GetStopRange(line,stops,true,out float lower,out float upper);

            result=default;if(upper<=lower)return false;result=MathUtils.Cut(line,new float2(lower,upper));

            for(int side=0;side<2;side++)

            {

                float cut=side==0?lower:upper;if(side==0?lower<=0:upper>=1)continue;

                float stopWidth=0;

                foreach(var stop in stops)if(StopLinePlacement.TryIntersect(line,stop.Curve,out float at,out var hit)&&math.abs(at-cut)<.001f&&EntityManager.HasComponent<NetLaneGeometryData>(stop.Prefab))stopWidth=math.max(stopWidth,EntityManager.GetComponentData<NetLaneGeometryData>(stop.Prefab).m_Size.x);

                float gap=StopLinePlacement.MedianGap(Mod.Settings.AlignMedianStopBar,Mod.Settings.MedianLineSetback,stopWidth);

                if(!StopLinePlacement.TrimAtStop(result,side==0?0:1,side==0,gap,out result))return false;

            }return Valid(result);

        }



        internal bool HasCurbReplacement(Entity road,Bezier4x3 source)
        {
            if(!_marks.TryGetValue(road,out var marks)||!EntityManager.HasComponent<Composition>(road)||!EntityManager.HasComponent<EdgeGeometry>(road))return false;
            var composition=EntityManager.GetComponentData<Composition>(road).m_Edge;
            if(!EntityManager.HasComponent<NetCompositionData>(composition))return false;
            float width=EntityManager.GetComponentData<NetCompositionData>(composition).m_Width;
            var point=MathUtils.Position(source,.5f);var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);
            float x=RoadMarkingPolicy.Lateral(geometry,point,width);if(!math.isfinite(x))return false;
            bool below=false,above=false;var traffic=TrafficLanes(composition);
            foreach(var lane in traffic){below|=lane.X<x;above|=lane.X>x;}
            if(traffic.Count==0||below&&above||ParkingOnSide(road,geometry,width,x<0)&&!ParkingBanPolicy.Banned(EntityManager,road,point))return false;
            float offset=Mod.Settings.CurbLineOffset;
            var tangent=math.normalizesafe(MathUtils.Tangent(source,.5f).xz);
            foreach(var mark in marks.Curves)
            {
                if(!mark.Curb)continue;
                float distance=MathUtils.Distance(mark.Curve,point,out float at);
                var candidate=MathUtils.Position(mark.Curve,at);
                if(distance<=offset+.35f&&math.abs(candidate.y-point.y)<.2f&&math.abs(math.dot(tangent,math.normalizesafe(MathUtils.Tangent(mark.Curve,at).xz)))>.98f)return true;
            }
            return false;
        }

        private bool OwnsMedianInterior(Entity road,Bezier4x3 source,IList<MarkCurve> marks)
        {
            if(!Mod.Settings.MedianEdgeLines||!EntityManager.HasComponent<Composition>(road)||!EntityManager.HasComponent<EdgeGeometry>(road))return false;
            var composition=EntityManager.GetComponentData<Composition>(road).m_Edge;
            if(!EntityManager.HasBuffer<NetCompositionPiece>(composition)||!EntityManager.HasComponent<NetCompositionData>(composition))return false;
            var spans=new List<float2>();
            foreach(var piece in EntityManager.GetBuffer<NetCompositionPiece>(composition,true))
            {
                if((piece.m_SectionFlags&(NetSectionFlags.Median|NetSectionFlags.Hidden|NetSectionFlags.Underground|NetSectionFlags.Overhead))!=NetSectionFlags.Median||(piece.m_PieceFlags&NetPieceFlags.HasRoadLanes)!=0)continue;
                float width=piece.m_Size.x;if(width<=0&&EntityManager.HasComponent<NetPieceData>(piece.m_Piece))width=EntityManager.GetComponentData<NetPieceData>(piece.m_Piece).m_Width;
                if(math.isfinite(width)&&width>.05f)spans.Add(new float2(piece.m_Offset.x-width*.5f,piece.m_Offset.x+width*.5f));
            }
            var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);float roadWidth=EntityManager.GetComponentData<NetCompositionData>(composition).m_Width;
            foreach(var span in NorthAmericanLinePolicy.Merge(spans))
            {
                bool left=false,right=false;
                foreach(var mark in marks)if(mark.Median)
                {
                    float x=RoadMarkingPolicy.Lateral(geometry,MathUtils.Position(mark.Curve,.5f),roadWidth);
                    left|=math.abs(x-(span.x-Mod.Settings.MedianLineOffset))<.15f;right|=math.abs(x-(span.y+Mod.Settings.MedianLineOffset))<.15f;
                }
                if(!left||!right)continue;
                bool inside=true;
                for(int i=0;i<5;i++)
                {
                    var point=MathUtils.Position(source,i*.25f);float x=RoadMarkingPolicy.Lateral(geometry,point,roadWidth);
                    if(!MedianPaintPolicy.Inside(x,span)){inside=false;break;}
                    // Reject a crossing/diagonal connector; this owns longitudinal paint only.
                    bool aligned=false;foreach(var mark in marks)if(mark.Median){MathUtils.Distance(mark.Curve,point,out float at);if(math.abs(MathUtils.Position(mark.Curve,at).y-point.y)<.3f&&math.abs(math.dot(math.normalizesafe(MathUtils.Tangent(source,i*.25f).xz),math.normalizesafe(MathUtils.Tangent(mark.Curve,at).xz)))>.9f){aligned=true;break;}}
                    if(!aligned){inside=false;break;}
                }
                if(inside)return true;
            }
            return false;
        }

        internal bool HasMedianReplacement(Entity road,Bezier4x3 source)

        {

            if(!Mod.Settings.MedianEdgeLines||!_marks.TryGetValue(road,out var marks))return false;
            if(OwnsMedianInterior(road,source,marks.Curves))return true;

            var point=MathUtils.Position(source,.5f);var tangent=math.normalizesafe(MathUtils.Tangent(source,.5f).xz);

            foreach(var mark in marks.Curves)

            {

                if(!mark.Median)continue;

                float distance=MathUtils.Distance(mark.Curve,point,out float at);

                if(distance<=Mod.Settings.MedianLineOffset+.35f&&math.abs(MathUtils.Position(mark.Curve,at).y-point.y)<.2f&&math.abs(math.dot(tangent,math.normalizesafe(MathUtils.Tangent(mark.Curve,at).xz)))>.98f)return true;

            }return false;

        }



        private void AddMedianMarks(Entity road,Entity composition,NetCompositionData data,EdgeGeometry geometry,List<StopMark> stops,List<MarkCurve> output,List<float2> unmarked)

        {

            if(!Mod.Settings.MedianEdgeLines||!EntityManager.HasBuffer<NetCompositionPiece>(composition))return;

            Entity asset=AutomaticMedianAsset();if(asset==Entity.Null&&!Mod.Settings.EuropeanMarkings)asset=_solidYellow;if(!string.IsNullOrEmpty(Mod.Settings.MedianLineStyle))_nativeLineDefinitions.TryGetValue(Mod.Settings.MedianLineStyle,out asset);
            if(Mod.Settings.EuropeanMarkings&&asset!=Entity.Null)asset=ThemeEquivalent(asset);

            if(asset==Entity.Null)return;

            var spans=new List<float2>();

            foreach(var piece in EntityManager.GetBuffer<NetCompositionPiece>(composition,true))

            {

                if((piece.m_SectionFlags&(NetSectionFlags.Median|NetSectionFlags.Underground|NetSectionFlags.Overhead|NetSectionFlags.Hidden))!=NetSectionFlags.Median||(piece.m_PieceFlags&NetPieceFlags.HasRoadLanes)!=0)continue;

                float width=piece.m_Size.x;if(width<=0&&EntityManager.HasComponent<NetPieceData>(piece.m_Piece))width=EntityManager.GetComponentData<NetPieceData>(piece.m_Piece).m_Width;

                if(width>.05f)spans.Add(new float2(piece.m_Offset.x-width*.5f,piece.m_Offset.x+width*.5f));

            }

            var traffic=TrafficLanes(composition);

            foreach(var span in NorthAmericanLinePolicy.Merge(spans))for(int side=0;side<2;side++)

            {

                float curb=side==0?span.x:span.y;float x=curb+(side==0?-Mod.Settings.MedianLineOffset:Mod.Settings.MedianLineOffset);

                if(RoadMarkingPolicy.Suppressed(x,unmarked))continue;

                bool adjacent=false;float height=0;

                foreach(var lane in traffic)if(x>lane.X-lane.Width*.5f-.05f&&x<lane.X+lane.Width*.5f+.05f){adjacent=true;height=lane.Y;break;}

                if(!adjacent)continue;float fraction=x/data.m_Width+.5f;if(fraction<=0||fraction>=1)continue;

                var medianFirst=MathUtils.Lerp(geometry.m_Start.m_Left,geometry.m_Start.m_Right,fraction);var medianLast=MathUtils.Lerp(geometry.m_End.m_Left,geometry.m_End.m_Right,fraction);

                medianFirst.a.y+=height+.01f;medianFirst.b.y+=height+.01f;medianFirst.c.y+=height+.01f;medianFirst.d.y+=height+.01f;

                medianLast.a.y+=height+.01f;medianLast.b.y+=height+.01f;medianLast.c.y+=height+.01f;medianLast.d.y+=height+.01f;

                AddCaps(road,medianFirst,medianLast,stops,2,asset,side+1);

                for(int half=0;half<2;half++)

                {

                    var line=half==0?MathUtils.Lerp(geometry.m_Start.m_Left,geometry.m_Start.m_Right,fraction):MathUtils.Lerp(geometry.m_End.m_Left,geometry.m_End.m_Right,fraction);

                    line.a.y+=height+.01f;line.b.y+=height+.01f;line.c.y+=height+.01f;line.d.y+=height+.01f;

                    if(TrimMedian(line,stops,out var clipped))output.Add(new MarkCurve(clipped,asset,0,true));

                }

            }

        }



        internal List<float3> ReadCurbAnchors(Entity composition, DynamicBuffer<NetCompositionLane> lanes)

        {

            var anchors = new List<float3>();

            foreach (var lane in lanes)

            {

                if (!_prefabs.TryGetPrefab<NetLanePrefab>(lane.m_Lane, out var prefab) || prefab == null) continue;

                // Vanilla raised sidewalks author this zero-width boarding lane

                // at their road-facing curb. Use the final composed position:

                // it already includes section inversion, offsets and width changes.

                if (prefab.name != "Boarding Lane 0" || !math.all(math.isfinite(lane.m_Position))) continue;

                bool duplicate = false;

                foreach (var anchor in anchors) if (math.abs(anchor.x - lane.m_Position.x) < 0.05f) duplicate = true;

                if (!duplicate) anchors.Add(lane.m_Position);

            }

            // Combined sidewalk/parking pieces do not author Boarding Lane 0.

            // Their second vertex-match offset is the actual parking/sidewalk

            // curb, not the drive-lane/parking boundary. Read it without editing

            // the piece, its lane array, or its composition.

            int parkingCurbs = 0;

            if (EntityManager.HasBuffer<NetCompositionPiece>(composition))

            {

                var pieces = EntityManager.GetBuffer<NetCompositionPiece>(composition, true);

                foreach (var piece in pieces)

                {

                    if ((piece.m_SectionFlags & (NetSectionFlags.Median | NetSectionFlags.Underground | NetSectionFlags.Overhead)) != 0) continue;

                    if (!_prefabs.TryGetPrefab<NetPiecePrefab>(piece.m_Piece, out var prefab) || prefab == null

                        || !prefab.name.StartsWith("Sidewalk With Parking Piece ", StringComparison.Ordinal)

                        || prefab.name.IndexOf("Flat", StringComparison.OrdinalIgnoreCase) >= 0

                        || !EntityManager.HasComponent<NetVertexMatchData>(piece.m_Piece)

                        || !EntityManager.HasBuffer<NetPieceLane>(piece.m_Piece)) continue;

                    float localCurb = EntityManager.GetComponentData<NetVertexMatchData>(piece.m_Piece).m_Offsets.y;

                    var pieceLanes = EntityManager.GetBuffer<NetPieceLane>(piece.m_Piece, true);

                    float roadHeight = float.NaN;

                    foreach (var pieceLane in pieceLanes)

                    {

                        if (!EntityManager.HasComponent<NetLaneData>(pieceLane.m_Lane)) continue;

                        var flags = EntityManager.GetComponentData<NetLaneData>(pieceLane.m_Lane).m_Flags;

                        if ((flags & (LaneFlags.Parking | LaneFlags.Road)) != 0) { roadHeight = pieceLane.m_Position.y; break; }

                    }

                    if (!CurbPlacement.TryParkingCurb(piece.m_Offset, localCurb, roadHeight,

                        (piece.m_SectionFlags & NetSectionFlags.Invert) != 0, out var curb)) continue;

                    bool duplicate = false;

                    foreach (var anchor in anchors) if (math.abs(anchor.x - curb.x) < 0.05f) duplicate = true;

                    if (!duplicate) { anchors.Add(curb); parkingCurbs++; }

                }

            }

            return anchors;

        }



        internal bool TryVisualPowerlineCurves(Entity road, byte mask, List<Bezier4x3> output, List<bool> leftSides)

        {

            if(!EntityManager.HasComponent<Composition>(road) || !EntityManager.HasComponent<EdgeGeometry>(road)) return false;

            Entity composition=EntityManager.GetComponentData<Composition>(road).m_Edge;

            if(!EntityManager.HasComponent<NetCompositionData>(composition) || !EntityManager.HasBuffer<NetCompositionLane>(composition)) return false;

            var data=EntityManager.GetComponentData<NetCompositionData>(composition);

            if((data.m_Flags.m_General & CompositionFlags.General.Tunnel)!=0) return false;

            var anchors=ReadCurbAnchors(composition,EntityManager.GetBuffer<NetCompositionLane>(composition,true));

            anchors.Sort((a,b)=>a.x.CompareTo(b.x));

            if(anchors.Count==0) { anchors.Add(new float3(-data.m_Width*.5f,0,0)); anchors.Add(new float3(data.m_Width*.5f,0,0)); }

            var g=EntityManager.GetComponentData<EdgeGeometry>(road); var edge=EntityManager.GetComponentData<Edge>(road);

            for(int i=0;i<anchors.Count;i++)

            {

                if(i!=0 && i!=anchors.Count-1) continue;

                bool left=anchors.Count==1?anchors[i].x<0:i==0;

                if((mask&RoadSideUpgrades.SideMask(left))==0) continue;

                float x=anchors[i].x+(left?-.65f:.65f),best=5f;

                if(EntityManager.HasBuffer<NetCompositionPiece>(composition)) foreach(var piece in EntityManager.GetBuffer<NetCompositionPiece>(composition,true))

                {

                    if((piece.m_SectionFlags&(NetSectionFlags.Median|NetSectionFlags.Underground|NetSectionFlags.Overhead))!=0 || (piece.m_PieceFlags&(NetPieceFlags.HasRoadLanes|NetPieceFlags.HasBicycleLanes))!=0) continue;

                    if(!_prefabs.TryGetPrefab<PrefabBase>(piece.m_Piece,out var prefab) || prefab==null || prefab.name.IndexOf("grass",StringComparison.OrdinalIgnoreCase)<0) continue;

                    if(left?piece.m_Offset.x>=0:piece.m_Offset.x<=0) continue;

                    float distance=math.abs(piece.m_Offset.x-anchors[i].x);

                    if(distance<best && piece.m_Size.x>=.5f) {x=piece.m_Offset.x;best=distance;}

                }

                float offset=left?x-anchors[i].x:anchors[i].x-x;

                if(!CurbPlacement.TryMapDecal(data.m_Width,anchors[i],left,offset,out var position,out float t)) continue;

                var first=MathUtils.Lerp(g.m_Start.m_Left,g.m_Start.m_Right,t);var last=MathUtils.Lerp(g.m_End.m_Left,g.m_End.m_Right,t);

                first.a.y+=position.y;first.b.y+=position.y;first.c.y+=position.y;first.d.y+=position.y;

                last.a.y+=position.y;last.b.y+=position.y;last.c.y+=position.y;last.d.y+=position.y;

                if(!DecalJunction(edge.m_Start) || StopLinePlacement.TrimAtStop(first,0,true,math.clamp(Mod.Settings.PowerlineCornerSetback,2f,15f),out first)) {output.Add(first);leftSides.Add(left);}

                if(!DecalJunction(edge.m_End) || StopLinePlacement.TrimAtStop(last,1,false,math.clamp(Mod.Settings.PowerlineCornerSetback,2f,15f),out last)) {output.Add(last);leftSides.Add(left);}

            }

            return output.Count>0;

        }

        private List<float3> ReadDecalAnchors(Entity composition,float width,DynamicBuffer<NetCompositionLane> lanes)

        {

            var anchors=ReadCurbAnchors(composition,lanes);float left=float.MaxValue,right=float.MinValue;float leftY=0,rightY=0;

            foreach(var lane in lanes)

            {

                if(!EntityManager.HasComponent<NetLaneData>(lane.m_Lane)) continue;

                var data=EntityManager.GetComponentData<NetLaneData>(lane.m_Lane);

                if((data.m_Flags&(LaneFlags.Road|LaneFlags.Parking))==0 || (data.m_Flags&LaneFlags.Underground)!=0 || !math.all(math.isfinite(lane.m_Position))) continue;

                float lo=lane.m_Position.x-data.m_Width*.5f,hi=lane.m_Position.x+data.m_Width*.5f;

                if(lo<left) {left=lo;leftY=lane.m_Position.y;}if(hi>right) {right=hi;rightY=lane.m_Position.y;}

            }

            return RoadsideAnchorPolicy.Complete(anchors,width,left,right,leftY,rightY);

        }

        internal bool TryDecalCurves(Entity road, float offset, float width, float setback, List<Bezier4x3> output, List<bool> leftSides = null, byte previewSides = 0)

        {

            if (!EntityManager.HasComponent<Composition>(road) || !EntityManager.HasComponent<EdgeGeometry>(road)) return false;

            Entity composition = EntityManager.GetComponentData<Composition>(road).m_Edge;

            if (!EntityManager.HasComponent<NetCompositionData>(composition) || !EntityManager.HasBuffer<NetCompositionLane>(composition)) return false;

            float roadWidth = EntityManager.GetComponentData<NetCompositionData>(composition).m_Width;

            var anchors = ReadDecalAnchors(composition,roadWidth,EntityManager.GetBuffer<NetCompositionLane>(composition,true));

            anchors.Sort((a, b) => a.x.CompareTo(b.x));

            var geometry = EntityManager.GetComponentData<EdgeGeometry>(road);

            var edge = EntityManager.GetComponentData<Edge>(road);

            var stops = ReadStopMarks(road);

            for (int side = 0; side < anchors.Count; side++)

            {

                if (side != 0 && side != anchors.Count - 1) continue;

                bool left = anchors.Count == 1 ? anchors[side].x < 0 : side == 0;

                byte selectedSides = previewSides!=0 ? previewSides : EntityManager.HasComponent<MGRCurbDecalSides>(road) ? EntityManager.GetComponentData<MGRCurbDecalSides>(road).Mask : (byte)0;

                if ((selectedSides & RoadSideUpgrades.SideMask(left)) == 0) continue;

                if (!CurbPlacement.TryMapDecal(roadWidth, anchors[side], left, offset + width * .5f, out var position, out float t)) continue;

                var first = MathUtils.Lerp(geometry.m_Start.m_Left, geometry.m_Start.m_Right, t);

                var last = MathUtils.Lerp(geometry.m_End.m_Left, geometry.m_End.m_Right, t);

                float height = position.y;

                first.a.y += height; first.b.y += height; first.c.y += height; first.d.y += height;

                last.a.y += height; last.b.y += height; last.c.y += height; last.d.y += height;

                GetStopRange(first, stops, true, out float lo, out float hi);

                if (StopLinePlacement.TrimRange(first, lo, hi, 0, out first) &&

                    (!DecalJunction(edge.m_Start) || StopLinePlacement.TrimAtStop(first, 0, true, setback, out first))) { output.Add(first); leftSides?.Add(left); }

                GetStopRange(last, stops, true, out lo, out hi);

                if (StopLinePlacement.TrimRange(last, lo, hi, 0, out last) &&

                    (!DecalJunction(edge.m_End) || StopLinePlacement.TrimAtStop(last, 1, false, setback, out last))) { output.Add(last); leftSides?.Add(left); }

            }

            return true;

        }

        private bool DecalJunction(Entity node)

        {

            if (!EntityManager.HasBuffer<ConnectedEdge>(node)) return false;

            int count = 0; foreach (var item in EntityManager.GetBuffer<ConnectedEdge>(node, true))

                if (EntityManager.HasComponent<Road>(item.m_Edge) && !EntityManager.HasComponent<Deleted>(item.m_Edge)) count++;

            return count > 2 || EntityManager.HasComponent<TrafficLights>(node);

        }



        private bool ParkingOnSide(Entity road, EdgeGeometry geometry, float width, bool left)

        {

            foreach (var item in EntityManager.GetBuffer<SubLane>(road, true))

            {

                var lane = item.m_SubLane;

                if (!EntityManager.HasComponent<Game.Net.ParkingLane>(lane) || EntityManager.HasComponent<Deleted>(lane) || !EntityManager.HasComponent<Curve>(lane)) continue;

                var flags = EntityManager.GetComponentData<Game.Net.ParkingLane>(lane).m_Flags;

                if ((flags & (ParkingLaneFlags.VirtualLane | ParkingLaneFlags.ParkingDisabled | ParkingLaneFlags.SpecialVehicles)) != 0) continue;

                float x = RoadMarkingPolicy.Lateral(geometry, MathUtils.Position(EntityManager.GetComponentData<Curve>(lane).m_Bezier, .5f), width);

                if (left ? x < 0f : x > 0f) return true;

            }

            return false;

        }

        internal bool TryClipCrosswalk(Entity node, Bezier4x3 line, out Bezier4x3 clipped, Entity approachRoad = default(Entity))

        {

            clipped = default(Bezier4x3); float best = float.MaxValue; bool found = false;



            if (!EntityManager.HasBuffer<ConnectedEdge>(node)) return false;

            var axis = math.normalizesafe(MathUtils.Tangent(line, .5f).xz); var middle = MathUtils.Position(line, .5f);

            foreach (var connection in EntityManager.GetBuffer<ConnectedEdge>(node, true))

            {

                var road = connection.m_Edge;

                if (approachRoad != Entity.Null && road != approachRoad) continue;

                if (!EntityManager.HasComponent<Road>(road) || EntityManager.HasComponent<Deleted>(road) || !EntityManager.HasComponent<EdgeGeometry>(road) || !EntityManager.HasComponent<Composition>(road) || !EntityManager.HasComponent<Edge>(road)) continue;

                var composition = EntityManager.GetComponentData<Composition>(road).m_Edge;

                if (!EntityManager.HasComponent<NetCompositionData>(composition) || !EntityManager.HasBuffer<NetCompositionLane>(composition)) continue;

                float width = EntityManager.GetComponentData<NetCompositionData>(composition).m_Width;

                float left = float.MaxValue, right = float.MinValue;

                foreach (var lane in EntityManager.GetBuffer<NetCompositionLane>(composition, true))

                    if (_prefabs.TryGetPrefab<NetLanePrefab>(lane.m_Lane, out var prefab) && prefab != null && prefab.name == "Boarding Lane 0") { left = math.min(left, lane.m_Position.x); right = math.max(right, lane.m_Position.x); }

                if (EntityManager.HasBuffer<NetCompositionPiece>(composition)) foreach (var piece in EntityManager.GetBuffer<NetCompositionPiece>(composition, true))

                    {

                        if ((piece.m_SectionFlags & (NetSectionFlags.Median | NetSectionFlags.Underground | NetSectionFlags.Overhead)) != 0) continue;

                        if (!_prefabs.TryGetPrefab<NetPiecePrefab>(piece.m_Piece, out var pp) || pp == null || !pp.name.StartsWith("Sidewalk With Parking Piece ", StringComparison.Ordinal) || pp.name.IndexOf("Flat", StringComparison.OrdinalIgnoreCase) >= 0 || !EntityManager.HasComponent<NetVertexMatchData>(piece.m_Piece)) continue;

                        if (CurbPlacement.TryParkingCurb(piece.m_Offset, EntityManager.GetComponentData<NetVertexMatchData>(piece.m_Piece).m_Offsets.y, 0f, (piece.m_SectionFlags & NetSectionFlags.Invert) != 0, out var curb)) { left = math.min(left, curb.x); right = math.max(right, curb.x); }

                    }

                // Inner edge of a vanilla sidewalk lane is a conservative fallback

                // when this composition does not expose the explicit curb anchor.

                if (left >= right)

                {

                    float fallbackLeft = float.MinValue, fallbackRight = float.MaxValue;

                    foreach (var lane in EntityManager.GetBuffer<NetCompositionLane>(composition, true))

                    {

                        if ((lane.m_Flags & LaneFlags.Pedestrian) == 0 || !EntityManager.HasComponent<NetLaneData>(lane.m_Lane)) continue;

                        float sidewalkWidth = EntityManager.GetComponentData<NetLaneData>(lane.m_Lane).m_Width;

                        if (!math.isfinite(sidewalkWidth) || sidewalkWidth <= 0f) continue;

                        if (lane.m_Position.x < 0f) fallbackLeft = math.max(fallbackLeft, lane.m_Position.x + sidewalkWidth * .5f);

                        if (lane.m_Position.x > 0f) fallbackRight = math.min(fallbackRight, lane.m_Position.x - sidewalkWidth * .5f);

                    }

                    if (left == float.MaxValue && fallbackLeft != float.MinValue) left = fallbackLeft;

                    if (right == float.MinValue && fallbackRight != float.MaxValue) right = fallbackRight;

                }

                if(left>=right){var fallback=ReadDecalAnchors(composition,width,EntityManager.GetBuffer<NetCompositionLane>(composition,true));foreach(var anchor in fallback){left=math.min(left,anchor.x);right=math.max(right,anchor.x);}}
                if (left >= right || width <= .1f) continue;

                var edge = EntityManager.GetComponentData<Edge>(road); bool start = edge.m_Start == node;

                var geometry = EntityManager.GetComponentData<EdgeGeometry>(road); var segment = start ? geometry.m_Start : geometry.m_End;

                var l = MathUtils.Lerp(segment.m_Left, segment.m_Right, left / width + .5f);

                var r = MathUtils.Lerp(segment.m_Left, segment.m_Right, right / width + .5f);

                var lp = start ? l.a : l.d; var rp = start ? r.a : r.d;

                if (math.abs(math.dot(axis, math.normalizesafe((rp - lp).xz))) < .7f) continue;

                float distance = math.distance(middle.xz, ((lp + rp) * .5f).xz);

                if (distance > 12f || distance >= best) continue;

                var lt = math.normalizesafe(MathUtils.Tangent(l, start ? 0f : 1f)); var rt = math.normalizesafe(MathUtils.Tangent(r, start ? 0f : 1f));

                var lc = new Bezier4x3(lp - lt * 20f, lp - lt * 7f, lp + lt * 7f, lp + lt * 20f);

                var rc = new Bezier4x3(rp - rt * 20f, rp - rt * 7f, rp + rt * 7f, rp + rt * 20f);

                if (!CrosswalkPlacement.TryApproachTrim(line, lc, rc, .5f, out var candidate)) continue;

                clipped = candidate; best = distance; found = true;

            }

            return found;

        }



        private void AddParkingMarks(Entity road, EdgeGeometry geometry, List<StopMark> stops, List<MarkCurve> output, List<float2> unmarked, float compositionWidth)

        {

            var roadCenter = (geometry.m_Start.m_Left.d + geometry.m_Start.m_Right.d

                + geometry.m_End.m_Left.a + geometry.m_End.m_Right.a) * 0.25f;

            var lanes = EntityManager.GetBuffer<SubLane>(road, true);

            foreach (var entry in lanes)

            {

                Entity lane = entry.m_SubLane;

                if (!EntityManager.HasComponent<Game.Net.ParkingLane>(lane) || EntityManager.HasComponent<Deleted>(lane)

                    || !EntityManager.HasComponent<Curve>(lane) || !EntityManager.HasComponent<PrefabRef>(lane)) continue;

                var parking = EntityManager.GetComponentData<Game.Net.ParkingLane>(lane);

                if ((parking.m_Flags & (ParkingLaneFlags.VirtualLane | ParkingLaneFlags.ParkingDisabled | ParkingLaneFlags.SpecialVehicles)) != 0) continue;

                Entity prefab = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;

                if (!EntityManager.HasComponent<NetLaneData>(prefab) || !EntityManager.HasComponent<ParkingLaneData>(prefab)) continue;

                float width = EntityManager.GetComponentData<NetLaneData>(prefab).m_Width;

                var curve = EntityManager.GetComponentData<Curve>(lane);

                if (!math.isfinite(width) || width <= 0.1f || !Valid(curve.m_Bezier)) continue;

                float lateral = RoadMarkingPolicy.Lateral(geometry, MathUtils.Position(curve.m_Bezier, 0.5f), compositionWidth);

                byte sideMode = RoadSideUpgrades.ParkingMode(EntityManager, road, MathUtils.Position(curve.m_Bezier, .5f));

                if (sideMode < 2) continue;

                if (RoadMarkingPolicy.Suppressed(lateral, unmarked)) continue;

                float2 tangent = math.normalizesafe(MathUtils.Tangent(curve.m_Bezier, 0.5f).xz);

                float2 inward = roadCenter.xz - MathUtils.Position(curve.m_Bezier, 0.5f).xz;

                bool inwardLeft = math.dot(MathUtils.Left(tangent), inward) > 0f;

                var boundary = NetUtils.OffsetCurveLeftSmooth(curve.m_Bezier, inwardLeft ? width * 0.5f : -width * 0.5f);

                boundary.a.y += 0.01f; boundary.b.y += 0.01f; boundary.c.y += 0.01f; boundary.d.y += 0.01f;

                GetStopRange(boundary, stops, LaneForward(road, lane, curve.m_Bezier), out float lower, out float upper);

                if (upper <= lower) continue;

                if (!StopLinePlacement.TrimRange(boundary, lower, upper, Mod.Settings.LockCurbAndStopLines ? 0f : Mod.Settings.CurbLineStopGap, out var clippedBoundary)) continue;

                MathUtils.Distance(boundary, clippedBoundary.a, out lower); MathUtils.Distance(boundary, clippedBoundary.d, out upper);

                var style = (ParkingBayStyle)(sideMode - 2);

                if ((byte)style > 4) style = ParkingBayStyle.BoundaryAndTs;

                if(ParkingBanPolicy.Banned(EntityManager,road,MathUtils.Position(curve.m_Bezier,.5f)))continue;
                var data = EntityManager.GetComponentData<ParkingLaneData>(prefab);

                // Draw only the slot layout currently used by native parking.

                // Occupied continuous lanes may still be waiting for conversion;

                // previewing a requested spacing here falsely promises alignment.

                if (!math.isfinite(data.m_SlotInterval) || math.abs(data.m_SlotAngle) > 0.25f) continue;

                if (data.m_SlotInterval <= 0f) continue;

                int count = NetUtils.GetParkingSlotCount(curve, parking, data);

                if (count < 1 || count > 512) continue;

                float interval = NetUtils.GetParkingSlotInterval(curve, parking, data, count);

                float offset;

                switch (parking.m_Flags & (ParkingLaneFlags.StartingLane | ParkingLaneFlags.EndingLane))

                {

                    case ParkingLaneFlags.StartingLane: offset = curve.m_Length - count * interval; break;

                    case ParkingLaneFlags.EndingLane: offset = 0f; break;

                    default: offset = (curve.m_Length - count * interval) * 0.5f; break;

                }

                offset = math.max(0f, offset);

                var parameters=new float[count+1];var available=new bool[count];
                for(int i=0;i<=count;i++)parameters[i]=ParkingMarkPlacement.SlotParameter(curve.m_Bezier,offset+i*interval);
                bool closeStart=ParkingClosesAt(road,boundary.a),closeEnd=ParkingClosesAt(road,boundary.d);
                bool usablePaint=ParkingSetback.TryLimits(curve.m_Bezier,lower,upper,closeStart,closeEnd,Mod.Settings.ParkingIntersectionSetback,out float paintFirst,out float paintLast);
                for(int i=0;i<count;i++)available[i]=ParkingPaintAvailable(road,lane,curve.m_Bezier,parameters[i],parameters[i+1],usablePaint,paintFirst,paintLast);
                float margin=.31f/math.max(.1f,MathUtils.Length(boundary));
                var markers=ParkingMarkPlacement.BayMarkers(parameters,available,lower>0?lower+margin:0,upper<1?upper-margin:1);
                foreach(var paint in ParkingMarkPlacement.BuildBayStyle(boundary,lower,upper,markers,!inwardLeft,style,width,closeStart,closeEnd,Mod.Settings.CloseParkingAtBuildingEntrances))
                    if(Valid(paint))output.Add(new MarkCurve(paint,_linePrefab,.1f));

                if ((style == ParkingBayStyle.BoundaryAndTs || style == ParkingBayStyle.CurbDividers) && markers.Count>1 && markers[0].T<=.001f && markers[markers.Count-1].T>=.999f && EntityManager.HasComponent<EdgeLane>(lane))

                {

                    var delta = EntityManager.GetComponentData<EdgeLane>(lane).m_EdgeDelta;

                    bool forward = LaneForward(road, lane, curve.m_Bezier);

                    if (math.min(delta.x, delta.y) <= .001f && math.max(delta.x, delta.y) >= .999f)

                        AddCaps(road, forward ? boundary : MathUtils.Invert(boundary), forward ? boundary : MathUtils.Invert(boundary), stops, 1);

                }

            }

        }



        private readonly ExternalPaintOwnership _externalPaint=new ExternalPaintOwnership();

        private void AddNativeLineCaps(Entity road,List<StopMark> stops,Entity composition,NetCompositionData data,List<MarkCurve> current)

        {
            if(SegmentUpgradePolicy.HidesNative(EntityManager,road))return;

            var edge=EntityManager.GetComponentData<Edge>(road);var caps=_caps[road];var traffic=TrafficLanes(composition);

            foreach(var item in EntityManager.GetBuffer<SubLane>(road,true))

            {

                var lane=item.m_SubLane;

                if(EntityManager.HasComponent<Deleted>(lane)||EntityManager.HasComponent<MGRCurbMarking>(lane)||!EntityManager.HasComponent<Curve>(lane)||!EntityManager.HasComponent<PrefabRef>(lane))continue;

                var definition=EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;

                if(!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(definition,out var prefab)||prefab==null)continue;

                if(_externalPaint.Contains(EntityManager,lane))continue;
                bool center=ExternalPaintPolicy.Key(prefab.name).IndexOf("CenterLine",StringComparison.OrdinalIgnoreCase)>=0;

                bool curb=ExternalPaintPolicy.Key(prefab.name).IndexOf("EdgeLine",StringComparison.OrdinalIgnoreCase)>=0&&prefab.name.IndexOf("Parking",StringComparison.OrdinalIgnoreCase)<0;

                bool opposing=prefab.name.IndexOf("Parking",StringComparison.OrdinalIgnoreCase)<0&&ExternalPaintPolicy.IsBoundary(prefab.name)&&NorthAmericanLinePolicy.YellowBoundary(RoadMarkingPolicy.Lateral(EntityManager.GetComponentData<EdgeGeometry>(road),MathUtils.Position(EntityManager.GetComponentData<Curve>(lane).m_Bezier,.5f),data.m_Width),traffic);
                center|=opposing;
                if((CenterlineVisibility.Hidden(EntityManager,road)||current.Exists(m=>m.CenterUpgrade))&&center)continue;
                if(!center&&!curb||SegmentUpgradePolicy.Hides(EntityManager,road,center,curb))continue;

                var curve=EntityManager.GetComponentData<Curve>(lane).m_Bezier;bool indexed=EntityManager.HasComponent<EdgeLane>(lane);var delta=indexed?EntityManager.GetComponentData<EdgeLane>(lane).m_EdgeDelta:new float2(.5f);
                if(LaneDividerPolicy.Mode(EntityManager,road)!=0&&IsInteriorLanePaint(road,curve)||SectionEdgePolicy.Mode(EntityManager,road)==2&&IsOuterEdgePaint(road,curve))continue;

                var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);

                float lateral=RoadMarkingPolicy.Lateral(geometry,MathUtils.Position(curve,.5f),data.m_Width);

                bool generated=OwnsMedianInterior(road,curve,current);var probe=MathUtils.Position(curve,.5f);var tangent=math.normalizesafe(MathUtils.Tangent(curve,.5f).xz);
                foreach(var mark in current)
                {
                    if(!mark.Median&&!mark.Curb)continue;
                    float distance=MathUtils.Distance(mark.Curve,probe,out float at);
                    if(distance<=(mark.Median?Mod.Settings.MedianLineOffset:Mod.Settings.CurbLineOffset)+.35f&&math.abs(MathUtils.Position(mark.Curve,at).y-probe.y)<.2f&&math.abs(math.dot(tangent,math.normalizesafe(MathUtils.Tangent(mark.Curve,at).xz)))>.98f){generated=true;break;}
                }
                if((curb||center)&&generated)continue;
                if(curb){bool below=false,above=false;foreach(var laneData in traffic){below|=laneData.X<lateral;above|=laneData.X>lateral;}if(below&&above||traffic.Count==0)continue;}

                int kind=center?3:0;int roadSide=lateral<0?-1:1;

                var asset=center?(Mod.Settings.YellowCenterLines?ResolveCenterStyle(definition):definition):(_nativeLineDefinitions.TryGetValue("EU Highway Edge Line",out var white)?white:definition);

                if(curb&&Mod.Settings.YellowCenterLines&&NorthAmericanLinePolicy.YellowBoundary(lateral,traffic))asset=ResolveCenterStyle(asset);

                for(int side=0;side<2;side++)

                {

                    float at=side==0?delta.x:delta.y;var point=side==0?curve.a:curve.d;bool start;

                    if(indexed){if(at>.001f&&at<.999f)continue;start=at<=.001f;}

                    else if(!NodeLinePlacement.TryRoadEnd(point,geometry.m_Start.m_Left.a,geometry.m_Start.m_Right.a,geometry.m_End.m_Left.d,geometry.m_End.m_Right.d,out start))continue;

                    bool stopped=false;foreach(var stop in stops)if(stop.AtStart==start){stopped=true;break;}if(stopped)continue;

                    var node=start?edge.m_Start:edge.m_End;bool duplicate=false;

                    foreach(var cap in caps)if(cap.Node==node&&(cap.Kind==kind||curb&&RoadSurfacePolicy.IsHighway(EntityManager,road)&&cap.Kind==2)&&(kind==0?cap.Side==(start?-roadSide:roadSide):math.distancesq(cap.Point,point)<.01f)){duplicate=true;break;}

                    if(duplicate)continue;

                    caps.Add(new JoinCap{Node=node,Point=point,Outward=(side==0?-1:1)*math.normalizesafe(MathUtils.Tangent(curve,side==0?0:1)),Kind=kind,Side=start?-roadSide:roadSide,Prefab=asset});_dirtyNodes.Add(node);

                }

            }

        }

        private void AddCaps(Entity road, Bezier4x3 first, Bezier4x3 last, List<StopMark> stops, int kind,Entity asset=default,int boundary=0)

        {

            if (!_caps.TryGetValue(road, out var caps)) return;

            var edge = EntityManager.GetComponentData<Edge>(road);

            float lateral=RoadMarkingPolicy.Lateral(EntityManager.GetComponentData<EdgeGeometry>(road),MathUtils.Position(first,.5f),1);

            int roadSide=lateral<0?-1:1;

            bool startStop = false, endStop = false;

            foreach (var stop in stops) { if (stop.AtStart) startStop = true; else endStop = true; }

            if (!startStop) caps.Add(new JoinCap { Node = edge.m_Start, Point = first.a, Outward = -math.normalizesafe(MathUtils.Tangent(first, 0f)), Kind = kind, Boundary=boundary, Side=-roadSide, Prefab=asset==Entity.Null?_linePrefab:asset });

            if (!endStop) caps.Add(new JoinCap { Node = edge.m_End, Point = last.d, Outward = math.normalizesafe(MathUtils.Tangent(last, 1f)), Kind = kind, Boundary=boundary, Side=roadSide, Prefab=asset==Entity.Null?_linePrefab:asset });

            _dirtyNodes.Add(edge.m_Start); _dirtyNodes.Add(edge.m_End);

        }



        internal bool CoversNativeNodePaint(Entity node,Bezier4x3 source,bool center)
        {
            if(!_joins.TryGetValue(node,out var joins))return false;
            float limit=center?.35f:Mod.Settings.CurbLineOffset+.35f;
            foreach(var mark in joins.Curves)
            {
                if(center?!mark.Median:mark.Median||math.abs(mark.Width-.15f)>.01f)continue;
                bool covered=true;
                for(int sample=0;sample<3;sample++)
                {
                    float t=sample*.5f;var point=MathUtils.Position(source,t);
                    float distance=MathUtils.Distance(mark.Curve,point,out float at);
                    var candidate=MathUtils.Position(mark.Curve,at);
                    if(distance>limit||math.abs(candidate.y-point.y)>.2f||math.abs(math.dot(math.normalizesafe(MathUtils.Tangent(source,t).xz),math.normalizesafe(MathUtils.Tangent(mark.Curve,at).xz)))<.9f){covered=false;break;}
                }
                if(covered)return true;
            }
            return false;
        }

        internal bool HasCenterNodeJoin(Entity node)=>_joins.TryGetValue(node,out var marks)&&marks.Curves.Exists(c=>c.Median);

        internal bool HasNodeJoin(Entity node) => _joins.TryGetValue(node, out var marks) && marks.Entities.Count > 0;

        internal void QueueNodeRefresh(Entity node) { _dirtyNodes.Add(node); }



        private void RebuildJoins()

        {

            var completed=_completedJoins;completed.Clear();var joinWatch=_joinWatch;joinWatch.Restart();
            WorkBatchPolicy.Take(_dirtyNodes,_joinCandidates,8);
            foreach (var node in _joinCandidates)

            {

                if(completed.Count>=8||completed.Count>0&&joinWatch.Elapsed.TotalMilliseconds>=2)break;
                completed.Add(node);
                var curves = new List<MarkCurve>();

                var roads = new List<Entity>();

                if (EntityManager.Exists(node) && !EntityManager.HasComponent<Deleted>(node) && EntityManager.HasBuffer<ConnectedEdge>(node))

                    foreach (var connected in EntityManager.GetBuffer<ConnectedEdge>(node, true))

                        if (EntityManager.HasComponent<Road>(connected.m_Edge) && !EntityManager.HasComponent<Deleted>(connected.m_Edge)

                            && !EntityManager.HasComponent<Game.Tools.Temp>(connected.m_Edge)) roads.Add(connected.m_Edge);

                bool preserveNative=AutomaticMarkingPolicy.PreservesNode(EntityManager,node);
                if (!preserveNative && roads.Count == 2 && _caps.TryGetValue(roads[0], out var aCaps) && _caps.TryGetValue(roads[1], out var bCaps))

                {

                    bool centerHandled=TryCenterTransitions(node,aCaps,bCaps,curves);
                    var used = new HashSet<int>();

                    foreach (var a in aCaps)

                    {

                        if (a.Node != node || a.Kind==0&&EntityManager.HasComponent<MGRJunctionEdgeLines>(node) || centerHandled&&(a.Kind==2||a.Kind==3) || a.Kind==3&&(aCaps.Exists(c=>c.Node==node&&c.Kind==2)||bCaps.Exists(c=>c.Node==node&&c.Kind==2))) continue;

                        int best = -1; float distance = 80f;

                        for (int i = 0; i < bCaps.Count; i++)

                        {

                            var b = bCaps[i];

                            float gap = math.distance(a.Point.xz, b.Point.xz);

                            if (used.Contains(i) || b.Node != node || a.Kind != b.Kind || a.Kind<=1&&a.Side!=-b.Side || a.Prefab!=b.Prefab || gap >= distance

                                || !NodeLinePlacement.CanJoin(a.Point,a.Outward,b.Point,b.Outward,a.Kind,a.Side,b.Side)) continue;

                            best = i; distance = gap;

                        }

                        if (best < 0) continue;

                        used.Add(best);

                        var other = bCaps[best];

                        if (distance <= 0.1f) continue;

                        var curve = NodeLinePlacement.Connect(a.Point, a.Outward, other.Point, other.Outward);

                        if (Valid(curve)) curves.Add(new MarkCurve(curve, a.Prefab, a.Kind==2||a.Kind==3?0f:(a.Kind == 1 ? .1f : .15f),(a.Kind==2||a.Kind==3),a.Kind==0));

                    }



                }

                bool nativeHighway=NativeHighwayPathsReady(node);
                if(!preserveNative&&!nativeHighway&&AutomaticMarkingPolicy.Allows(EntityManager,node))AddHighwayJoins(node,roads,curves);
                byte nodeHidden=SegmentUpgradePolicy.Mode(EntityManager,node);
                curves.RemoveAll(m=>!m.Protected&&SegmentUpgradePolicy.Hides(nodeHidden,m.Median,m.Curb));
                if(CenterlineVisibility.Hidden(EntityManager,node))curves.RemoveAll(m=>m.Median&&!m.Protected);
                if(!nativeHighway){AddNodeDividers(node,curves);AddNodeCenter(node,curves);CanonicalCenterPairs(curves);}
                if(!nativeHighway&&EntityManager.HasComponent<MGRJunctionEdgeLines>(node))AddJunctionEdges(node,roads,curves);
                if(EntityManager.HasComponent<MGRRemoveJunctionEdges>(node))curves.RemoveAll(m=>!m.Protected&&(m.Curb||roads.Exists(r=>RoadSurfacePolicy.IsHighway(EntityManager,r))&&m.Median&&!m.CenterUpgrade));
                // Render explicit paired node edges as the same individual stripes as their previews.
                byte edgePattern=EdgeUpgradePolicy.Mode(EntityManager,node,true);
                if(!nativeHighway&&EdgeUpgradePolicy.Paired(edgePattern)){
                    var split=new List<MarkCurve>();var single=World.GetOrCreateSystemManaged<YellowPaintDefinitionSystem>().Explicit(1,EdgeUpgradePolicy.IsYellow(edgePattern));
                    foreach(var mark in curves)if(mark.Curb&&!mark.Protected){if(edgePattern>=12){AddPassingMarks(node,true,1,new List<Bezier4x3>{NetUtils.OffsetCurveLeftSmooth(mark.Curve,-EdgeSpacing(edgePattern)),NetUtils.OffsetCurveLeftSmooth(mark.Curve,EdgeSpacing(edgePattern))},single,split);continue;}split.Add(new MarkCurve(NetUtils.OffsetCurveLeftSmooth(mark.Curve,-EdgeSpacing(edgePattern)),single,0,false,true));split.Add(new MarkCurve(NetUtils.OffsetCurveLeftSmooth(mark.Curve,EdgeSpacing(edgePattern)),single,0,false,true));}else split.Add(mark);
                    curves=split;
                }
                if(nativeHighway)AddHighwayNodePaint(node,curves);else ApplyDashedEdges(node,curves,true);
                ApplyHighwayLineIntents(node,curves);
                var reusable = new Dictionary<MarkingKey, Queue<Entity>>();

                if (_joins.TryGetValue(node, out var old))

                    for (int i = 0; i < old.Entities.Count; i++)

                    {

                        var entity = old.Entities[i];

                        if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity)) continue;

                        var key = new MarkingKey(old.Curves[i].Curve, old.Curves[i].Prefab);

                        if (!reusable.TryGetValue(key, out var queue)) reusable[key] = queue = new Queue<Entity>();

                        queue.Enqueue(entity);

                    }

                var next = new RoadMarks();

                foreach (var curve in curves)

                {

                    var key = new MarkingKey(curve.Curve, curve.Prefab);

                    next.Entities.Add(reusable.TryGetValue(key, out var queue) && queue.Count > 0 ? queue.Dequeue() : Spawn(node, curve.Curve, curve.Prefab, true, curve.Width));

                    next.Curves.Add(curve);

                }

                foreach (var queue in reusable.Values) foreach (var entity in queue) GeneratedVisualLifecycle.Retire(World,entity);

                if (next.Entities.Count > 0) _joins[node] = next; else _joins.Remove(node);

            }

            foreach (var node in completed)
            {
                JunctionMarkingSystem.RequestNodeRefresh(node);
                // Replacement joins are now committed. Node-source paint must see
                // that final ownership, even when native SubLane buffers did not change.
                if(!EntityManager.Exists(node)||!EntityManager.HasBuffer<SubLane>(node))continue;
                foreach(var entry in EntityManager.GetBuffer<SubLane>(node,true))
                {
                    var lane=entry.m_SubLane;
                    if(EntityManager.HasComponent<MGRCurbMarking>(lane)||EntityManager.HasComponent<MGRIntersectionPaint>(lane)||!EntityManager.HasComponent<PrefabRef>(lane))continue;
                    if(_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab,out var prefab)&&NodeLinePolicy.IsLineGeometry(prefab))IntersectionPaintSystem.RequestSource(lane);
                }
            }

            foreach(var node in completed)_dirtyNodes.Remove(node);
            if(ModDiagnostics.TraceBuffer.Enabled&&UnityEngine.Time.realtimeSinceStartup>=_nextJoinTiming){_nextJoinTiming=UnityEngine.Time.realtimeSinceStartup+1;ModDiagnostics.TraceBuffer.Record("join.timing","nodes="+completed.Count+" elapsedMs="+joinWatch.Elapsed.TotalMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" backlog="+_dirtyNodes.Count);}

        }



        private bool ParkingClosesAt(Entity road,float3 point)
        {
            if(!EntityManager.HasComponent<Edge>(road))return false;
            var edge=EntityManager.GetComponentData<Edge>(road);
            if(!EntityManager.HasComponent<Node>(edge.m_Start)||!EntityManager.HasComponent<Node>(edge.m_End))return false;
            var node=math.distancesq(point,EntityManager.GetComponentData<Node>(edge.m_Start).m_Position)<math.distancesq(point,EntityManager.GetComponentData<Node>(edge.m_End).m_Position)?edge.m_Start:edge.m_End;
            // A parking sublane may end at a building entrance far from either
            // intersection. Only a real junction end inside its approach can close.
            float radius=12;
            if(EntityManager.HasComponent<Composition>(road)){var comp=EntityManager.GetComponentData<Composition>(road).m_Edge;if(EntityManager.HasComponent<NetCompositionData>(comp))radius=math.max(radius,EntityManager.GetComponentData<NetCompositionData>(comp).m_Width+Mod.Settings.FixedParkingBayLength);}
            return DecalJunction(node)&&math.distancesq(point,EntityManager.GetComponentData<Node>(node).m_Position)<=radius*radius;
        }
        private bool ParkingBlocked(Entity lane, float lower, float upper)

        {

            if (!EntityManager.HasBuffer<LaneOverlap>(lane)) return false;

            foreach (var overlap in EntityManager.GetBuffer<LaneOverlap>(lane, true))

            {

                Entity other = overlap.m_Other;

                if (!EntityManager.Exists(other) || EntityManager.HasComponent<Deleted>(other)

                    || EntityManager.HasComponent<MGRCurbMarking>(other)) continue;

                if (!EntityManager.HasComponent<Game.Net.CarLane>(other) && !EntityManager.HasComponent<Game.Net.TrackLane>(other)) continue;

                if (overlap.m_ThisStart / 255f < upper && overlap.m_ThisEnd / 255f > lower) return true;

            }

            return false;

        }



        internal bool LaneForward(Entity road, Entity lane, Bezier4x3 curve)

        {

            if (EntityManager.HasComponent<EdgeLane>(lane))

            {

                var delta = EntityManager.GetComponentData<EdgeLane>(lane).m_EdgeDelta;

                if (math.abs(delta.y - delta.x) > 0.001f) return delta.y > delta.x;

            }

            if (!EntityManager.HasComponent<Curve>(road)) return true;

            var center = EntityManager.GetComponentData<Curve>(road).m_Bezier;

            MathUtils.Distance(center, curve.a, out float a);

            MathUtils.Distance(center, curve.d, out float d);

            return d >= a;

        }



        internal void GetRoadStopRange(Entity road, Bezier4x3 curve, bool forward, out float lower, out float upper)

            => GetStopRange(curve, ReadStopMarks(road), forward, out lower, out upper);



        private List<StopMark> ReadStopMarks(Entity road)

        {

            var result = new List<StopMark>();

            if (!EntityManager.HasComponent<EdgeGeometry>(road) || !EntityManager.HasComponent<Edge>(road)) return result;

            var geometry = EntityManager.GetComponentData<EdgeGeometry>(road);

            var first = MathUtils.Lerp(geometry.m_Start.m_Left, geometry.m_Start.m_Right, 0.5f);

            var last = MathUtils.Lerp(geometry.m_End.m_Left, geometry.m_End.m_Right, 0.5f);

            var edge = EntityManager.GetComponentData<Edge>(road);

            float reach = math.max(12f, math.distance(geometry.m_Start.m_Left.a.xz, geometry.m_Start.m_Right.a.xz) * 1.5f);

            var seen = new HashSet<Entity>();

            foreach (var owner in new[] { road, edge.m_Start, edge.m_End })

            {

                if (!EntityManager.HasBuffer<SubLane>(owner)) continue;

                foreach (var entry in EntityManager.GetBuffer<SubLane>(owner, true))

                {

                    Entity lane = entry.m_SubLane;

                    if (!seen.Add(lane) || !EntityManager.Exists(lane) || EntityManager.HasComponent<Deleted>(lane)

                        || EntityManager.HasComponent<MGRCurbMarking>(lane)

                        || !EntityManager.HasComponent<PrefabRef>(lane) || !EntityManager.HasComponent<Curve>(lane)) continue;

                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;

                    if (!_stopPrefabs.Contains(prefab)) continue;

                    var curve = EntityManager.GetComponentData<Curve>(lane).m_Bezier;

                    if (!Valid(curve)) continue;

                    var midpoint = (curve.a + curve.d) * 0.5f;

                    bool atStart = owner == road ? math.distancesq(midpoint.xz, first.a.xz) <= math.distancesq(midpoint.xz, last.d.xz) : owner == edge.m_Start;

                    var endpoint = atStart ? first.a : last.d;

                    var inward = math.normalizesafe((atStart ? MathUtils.Tangent(first, 0f) : -MathUtils.Tangent(last, 1f)).xz);

                    var axis = math.normalizesafe((curve.d - curve.a).xz);

                    if (math.abs(math.dot(axis, inward)) > 0.5f || math.distance(midpoint.xz, endpoint.xz) > reach

                        || math.abs(midpoint.y - endpoint.y) > 1f || math.dot(midpoint.xz - endpoint.xz, inward) < -1f) continue;

                    result.Add(new StopMark { Curve = curve, Prefab = prefab, AtStart = atStart, Reach = reach });

                }

            }

            return result;

        }



        private static void GetStopRange(Bezier4x3 curve, List<StopMark> stops, bool forward, out float lower, out float upper)

        {

            lower = 0f; upper = 1f;

            foreach (var stop in stops)

                if (StopLinePlacement.TryIntersect(curve, stop.Curve, out float cut, out var hit)

                    && math.distance(hit.xz, ((stop.Curve.a + stop.Curve.d) * 0.5f).xz) <= stop.Reach)

                    StopLinePlacement.AccumulateStopCut(cut, stop.AtStart == forward, ref lower, ref upper);

        }



        private void AddApproach(List<MarkCurve> output, Bezier4x3 line, Bezier4x3 curb,

            Bezier4x3 center, List<StopMark> stops, bool start, float width,Entity asset=default,bool extendStops=true,float gapOverride=-1)

        {

            GetStopRange(line, stops, true, out float lower, out float upper);

            float gap = gapOverride>=0?gapOverride:(Mod.Settings.LockCurbAndStopLines ? 0f : Mod.Settings.CurbLineStopGap);

            if (StopLinePlacement.TrimRange(line, lower, upper, gap, out var clipped) && Valid(clipped))

                output.Add(new MarkCurve(clipped, asset==Entity.Null?_linePrefab:asset,0,false,true));

            if(!extendStops)return;
            // The same two-end range drives stop-bar extensions; choose the

            // nearest outer endpoint when multiple lane stop bars are collinear.

            for (int side = 0; side < 2; side++)

            {

                float limit = side == 0 ? lower : upper;

                if (side == 0 ? lower <= 0f : upper >= 1f) continue;

                bool found = false;

                float shortest = float.MaxValue;

                MarkCurve best = default(MarkCurve);

                foreach (var stop in stops)

                {

                    if (stop.AtStart != (side == 0) || !StopLinePlacement.TryIntersect(line, stop.Curve, out float cut, out var hit)

                        || math.distance(hit.xz, MathUtils.Position(line, limit).xz) > 0.05f

                        || !StopLinePlacement.TryIntersect(curb, stop.Curve, out var curbT, out var curbHit)

                        || !StopLinePlacement.TryIntersect(center, stop.Curve, out var centerT, out var centerHit)

                        || !StopLinePlacement.TryExtension(stop.Curve, curbHit, centerHit, width,

                            Mod.Settings.LockCurbAndStopLines ? math.distance(curbHit.xz, hit.xz) : Mod.Settings.StopLineCurbGap,

                            out var extension) || !Valid(extension)) continue;

                    float length = math.distance(extension.a.xz, extension.d.xz);

                    if (length >= shortest) continue;

                    shortest = length; found = true; best = new MarkCurve(extension, stop.Prefab);best.Protected=true;

                }

                if (found) output.Add(best);

            }

        }



        private static bool Valid(Bezier4x3 curve) => math.all(math.isfinite(curve.a)) && math.all(math.isfinite(curve.b))

            && math.all(math.isfinite(curve.c)) && math.all(math.isfinite(curve.d)) && MathUtils.Length(curve) > 0.1f;



        internal Entity Spawn(Entity road, Bezier4x3 curve, Entity prefab, bool curbOwned = true, float paintWidth = 0f)

        {

            Entity entity;

            using (var created = new NativeArray<Entity>(1, Allocator.Temp))

            {

                EntityManager.CreateEntity(EntityManager.GetComponentData<NetLaneArchetypeData>(prefab).m_LaneArchetype, created);

                entity = created[0];

            }

            EntityManager.SetComponentData(entity, new PrefabRef(prefab));

            EntityManager.SetComponentData(entity, new Lane

            {

                m_StartNode = new PathNode(new PathNode(entity, (ushort)0), true),

                m_MiddleNode = new PathNode(new PathNode(entity, (ushort)1), true),

                m_EndNode = new PathNode(new PathNode(entity, (ushort)2), true)

            });

            EntityManager.SetComponentData(entity, new Curve { m_Bezier = curve, m_Length = MathUtils.Length(curve) });

            EntityManager.AddComponentData(entity, new Owner(road));

            if (curbOwned) EntityManager.AddComponent<MGRCurbMarking>(entity);

            EntityManager.AddComponent<Game.Net.Elevation>(entity);

            // This entity already has its final curve. Do not let either

            // vanilla or TRL secondary-lane generation reinterpret it.

            if (EntityManager.HasComponent<Game.Net.SecondaryLane>(entity)) EntityManager.RemoveComponent<Game.Net.SecondaryLane>(entity);

            if (!EntityManager.HasComponent<Created>(entity)) EntityManager.AddComponent<Created>(entity);

            if (!EntityManager.HasComponent<Updated>(entity)) EntityManager.AddComponent<Updated>(entity);

            GeneratedVisualLifecycle.Track(World,entity);

            if(ModDiagnostics.ActionJournal.Active)ModDiagnostics.ActionJournal.Record("line.createdByCurbside","entity="+entity+" owner="+road+" prefab="+prefab+" curbOwned="+curbOwned+" a="+curve.a+" b="+curve.b+" c="+curve.c+" d="+curve.d);
            return entity;

        }



        private void RemoveRoad(Entity road)

        {

            if (_caps.TryGetValue(road, out var caps)) { foreach (var cap in caps) _dirtyNodes.Add(cap.Node); _caps.Remove(road); }

            if (!_marks.TryGetValue(road, out var entry)) return;

            foreach (var entity in entry.Entities)

                if (EntityManager.Exists(entity) && !EntityManager.HasComponent<Deleted>(entity)) GeneratedVisualLifecycle.Retire(World,entity);

            _marks.Remove(road);

        }



        internal void ClearOwned()

        {

            Dependency.Complete();

            using (var entities = _owned.ToEntityArray(Allocator.Temp))

                foreach (var entity in entities) GeneratedVisualLifecycle.Retire(World,entity);

            _parkingDirtyRoads.Clear();

            _auditRoads.Clear();

            _externalDirtyRoads.Clear();

            _sectionMarkingPolicy.Clear();

            _marks.Clear();

            _caps.Clear(); _joins.Clear(); _dirtyNodes.Clear();

            _pendingRoads.Clear();

            _ownedCount = 0;

        }

    }

}
