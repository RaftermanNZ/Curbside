using System;

using System.Collections.Generic;

using Colossal.Mathematics;

using Colossal.Serialization.Entities;

using Game;

using Game.Common;

using Game.Net;

using Game.Prefabs;

using Game.Serialization;

using Unity.Collections;

using Unity.Entities;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

    public struct MGRVisualPowerlineUpgrade : IComponentData, ISerializable

    {

        public byte Sides;

        public void Serialize<T>(T writer) where T:IWriter {writer.Write(Sides);}

        public void Deserialize<T>(T reader) where T:IReader {reader.Read(out Sides);}

    }

    public struct MGRVisualPowerlineRuntime : IComponentData { }

    internal static class VisualPowerlinePolicy

    {

        internal static Bezier4x3 Span(float3 a,float3 b,float hanging)

        {

            float drop=math.min(2f,math.distance(a.xz,b.xz)*math.clamp(hanging,.01f,.08f)*1.3333334f);

            var first=math.lerp(a,b,1f/3f);var last=math.lerp(a,b,2f/3f);first.y-=drop;last.y-=drop;

            return new Bezier4x3(a,first,last,b);

        }

        internal static int PoleCount(float length,float spacing,float minimum)

        {

            if(!math.isfinite(length)||length<=0) return 0;

            if(length<minimum) return 1;

            int intervals=math.max(1,(int)math.round(length/spacing));

            return math.min(math.min(intervals,(int)math.floor(length/minimum))+1,32);

        }

        internal static List<int2> Terminals(IList<float3> a,IList<float3> b)

        {

            // Native low-voltage poles use three terminals. Examine every

            // ordering together to avoid twists from greedy individual matches.

            var result=new List<int2>();int count=math.min(a.Count,b.Count);

            if(count==0) return result;

            if(count>6 || b.Count>6) return result; // unsupported pole definition, never unbounded work

            var used=new bool[b.Count];var path=new int[count];var bestPath=new int[count];float best=float.MaxValue;

            Action<int,float> visit=null;

            visit=(i,cost)=>

            {

                if(cost>=best) return;

                if(i==count) {best=cost;Array.Copy(path,bestPath,count);return;}

                for(int j=0;j<b.Count;j++) if(!used[j]) {used[j]=true;path[i]=j;visit(i+1,cost+math.distance(a[i].xz,b[j].xz));used[j]=false;}

            };

            visit(0,0);for(int i=0;i<count;i++) result.Add(new int2(i,bestPath[i]));return result;

        }

        internal static List<int2> StraightConnections(IList<float3> points,IList<float3> tangents,IList<bool> alley,float3 centre)
        {
            var result=new List<int2>();int count=points.Count;
            if(count<2||count>64||tangents.Count!=count||alley.Count!=count)return result;
            var edges=new List<System.Tuple<int,int,float>>();
            for(int a=0;a<count;a++)for(int b=a+1;b<count;b++)
            {
                if(!math.all(math.isfinite(points[a]))||!math.all(math.isfinite(points[b])))continue;
                var delta=points[b].xz-points[a].xz;float length=math.length(delta);
                if(length<.5f||length>80||math.abs(points[a].y-points[b].y)>8)continue;
                var direction=delta/length;
                float alignment=math.abs(math.dot(direction,math.normalizesafe(tangents[a].xz)))+math.abs(math.dot(direction,math.normalizesafe(tangents[b].xz)));
                var mid=(points[a].xz+points[b].xz)*.5f;
                float radius=math.min(math.distance(points[a].xz,centre.xz),math.distance(points[b].xz,centre.xz));
                float intrusion=math.max(0,radius-math.distance(mid,centre.xz));
                // Game fitting heuristics, not statutory construction dimensions.
                bool straight=math.abs(math.dot(direction,math.normalizesafe(tangents[a].xz)))>=.94f&&math.abs(math.dot(direction,math.normalizesafe(tangents[b].xz)))>=.94f;
                float cost=length+25*(2-alignment)+2*intrusion-(straight?1000:0);
                if(alley[a]!=alley[b])cost+=40; // secondary spur, preserve the main run
                edges.Add(System.Tuple.Create(a,b,cost));
            }
            edges.Sort((a,b)=>{int order=a.Item3.CompareTo(b.Item3);if(order!=0)return order;order=a.Item1.CompareTo(b.Item1);return order!=0?order:a.Item2.CompareTo(b.Item2);});
            var parent=new int[count];var degree=new int[count];for(int i=0;i<count;i++)parent[i]=i;
            System.Func<int,int> root=null;root=i=>parent[i]==i?i:(parent[i]=root(parent[i]));
            foreach(var edge in edges)
            {
                int a=edge.Item1,b=edge.Item2;if(root(a)==root(b))continue;
                if(degree[a]>0&&degree[b]>0&&!alley[a]&&!alley[b])continue; // independent through-runs need no arbitrary cross-road tie
                bool crossing=false;
                foreach(var pair in result)if(pair.x!=a&&pair.y!=a&&pair.x!=b&&pair.y!=b&&Crosses(points[a].xz,points[b].xz,points[pair.x].xz,points[pair.y].xz)){crossing=true;break;}
                if(crossing)continue;
                result.Add(new int2(a,b));degree[a]++;degree[b]++;parent[root(a)]=root(b);
            }
            return result;
        }
        internal static bool Crosses(float2 a,float2 b,float2 c,float2 d)
        {
            float2 ab=b-a,cd=d-c;
            float ac=ab.x*(c.y-a.y)-ab.y*(c.x-a.x),ad=ab.x*(d.y-a.y)-ab.y*(d.x-a.x);
            float ca=cd.x*(a.y-c.y)-cd.y*(a.x-c.x),cb=cd.x*(b.y-c.y)-cd.y*(b.x-c.x);
            return ac*ad<-.0001f&&ca*cb<-.0001f;
        }
        internal static List<int2> PriorityConnections(IList<float3> points,IList<bool> alley,float3 centre)
        {
            if(points.Count!=alley.Count)return new List<int2>();
            var indices=new List<int>();var main=new List<float3>();
            for(int i=0;i<points.Count;i++)if(!alley[i]){indices.Add(i);main.Add(points[i]);}
            if(indices.Count==0)return JunctionConnections(points,centre);
            var result=new List<int2>();foreach(var pair in JunctionConnections(main,centre))result.Add(new int2(indices[pair.x],indices[pair.y]));
            for(int i=0;i<points.Count;i++)if(alley[i])
            {
                int target=-1;float distance=float.MaxValue;
                foreach(int j in indices){float value=math.distancesq(points[i],points[j]);if(value<distance){distance=value;target=j;}}
                if(target>=0)result.Add(new int2(i,target));
            }
            return result;
        }

        internal static List<int2> JunctionConnections(IList<float3> points,float3 centre)

        {

            var result=new List<int2>();if(points.Count<2) return result;

            var order=new List<int>();for(int i=0;i<points.Count;i++) order.Add(i);

            order.Sort((a,b)=>math.atan2(points[a].z-centre.z,points[a].x-centre.x).CompareTo(math.atan2(points[b].z-centre.z,points[b].x-centre.x)));

            int omit=0;float longest=-1;

            for(int i=0;i<order.Count;i++) {float distance=math.distancesq(points[order[i]],points[order[(i+1)%order.Count]]);if(distance>longest) {longest=distance;omit=i;}}

            for(int i=0;i<order.Count;i++) if(i!=omit) result.Add(new int2(order[i],order[(i+1)%order.Count]));

            return result;

        }

    }



    // Derived native render entities only: no electrical graph, capacity or service.

    public partial class VisualPowerlineSystem : GameSystemBase, IPreSerialize

    {

        internal const string AssetName="MGR Visual Roadside Powerlines";

        internal static string Status {get;private set;}="Not applied";

        private EntityQuery _roads,_nodes,_deleted,_owned,_assets,_poleAssets;

        private PrefabSystem _prefabs;private Entity _wire,_pole;private float3[] _attachments=Array.Empty<float3>();private float _hanging=.025f;

        private bool _active,_initial=true,_assetsUnavailable;

        private sealed class Pole {internal Entity Entity;internal float3 Position,Tangent,Axis;internal float3[] Attachments;internal int Uses,OrphanTick;internal bool Invalidated;internal readonly HashSet<Entity> Roads=new HashSet<Entity>();}

        private sealed class RoadVisual {internal int Hash;internal Entity StartNode,EndNode;internal readonly List<Pole> Poles=new List<Pole>();internal readonly List<bool> Sides=new List<bool>();internal readonly List<Entity> Wires=new List<Entity>();internal readonly List<End> Ends=new List<End>();}

        private struct End {internal Entity Node;internal Pole Pole;}

        private readonly Dictionary<Entity,RoadVisual> _visuals=new Dictionary<Entity,RoadVisual>();

        private readonly VisualSpanIndex<Entity> _spans=new VisualSpanIndex<Entity>();

        private readonly Dictionary<Entity,Pole> _poleEntities=new Dictionary<Entity,Pole>();

        private readonly Queue<Entity> _roadQueue=new Queue<Entity>();

        private readonly Dictionary<int3,List<Pole>> _poles=new Dictionary<int3,List<Pole>>();

        private float _spacing=35f,_minimumGap=10f,_cornerSetback=6f,_preferredHeight=10f;

        private readonly HashSet<Pole> _orphans=new HashSet<Pole>();

        private int _tick;

        private readonly Dictionary<Entity,List<Entity>> _joins=new Dictionary<Entity,List<Entity>>();

        private readonly HashSet<Entity> _pending=new HashSet<Entity>(),_dirtyNodes=new HashSet<Entity>();

        private void EnqueueRoad(Entity road) {if(_pending.Add(road)) _roadQueue.Enqueue(road);}

        private readonly HashSet<Entity> _withdrawals=new HashSet<Entity>();
        internal void QueueRoad(Entity road,bool removeVisuals=false) {if(removeVisuals)_withdrawals.Add(road);EnqueueRoad(road);_assetsUnavailable=false;Status="Roadside powerlines queued";}

        protected override void OnCreate()

        {

            base.OnCreate();InitializePoleHeights();_prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();

            _roads=GetEntityQuery(ComponentType.ReadOnly<Road>(),ComponentType.ReadOnly<Edge>(),ComponentType.ReadOnly<EdgeGeometry>(),ComponentType.ReadOnly<Composition>(),ComponentType.ReadOnly<MGRVisualPowerlineUpgrade>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());

            _nodes=GetEntityQuery(ComponentType.ReadOnly<Node>(),ComponentType.ReadOnly<ConnectedEdge>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());

            _nodes.SetChangedVersionFilter(ComponentType.ReadOnly<ConnectedEdge>());

            _deleted=GetEntityQuery(ComponentType.ReadOnly<Road>(),ComponentType.ReadOnly<Deleted>());

            _owned=GetEntityQuery(ComponentType.ReadOnly<MGRVisualPowerlineRuntime>(),ComponentType.Exclude<Deleted>());

            _assets=GetEntityQuery(ComponentType.ReadOnly<UtilityLaneData>(),ComponentType.ReadOnly<NetLaneArchetypeData>(),ComponentType.ReadOnly<NetLaneGeometryData>());

            _poleAssets=GetEntityQuery(ComponentType.ReadOnly<ObjectData>(),ComponentType.ReadOnly<UtilityObjectData>(),ComponentType.ReadOnly<ObjectGeometryData>(),ComponentType.ReadOnly<Game.Prefabs.SubLane>());

        }

        protected override void OnGamePreload(Purpose purpose,GameMode mode) {base.OnGamePreload(purpose,mode);_active=false;Clear();RestorePoleDefinitions();}

        protected override void OnGameLoadingComplete(Purpose purpose,GameMode mode) {base.OnGameLoadingComplete(purpose,mode);Clear();_wire=Entity.Null;_pole=Entity.Null;_assetsUnavailable=false;_active=(mode&GameMode.GameOrEditor)!=GameMode.None;}

        protected override void OnDestroy() {DisposeResources();base.OnDestroy();}

        private bool Live(Entity e) => EntityManager.Exists(e)&&!EntityManager.HasComponent<Deleted>(e);

        private void Delete(Entity e) {if(Live(e)) GeneratedVisualLifecycle.Retire(World,e);}

        internal void Clear()

        {

            Dependency.Complete();using(var owned=_owned.ToEntityArray(Allocator.Temp)) foreach(var e in owned) Delete(e);

            _visuals.Clear();_poles.Clear();_poleEntities.Clear();_orphans.Clear();_spans.Clear();_joins.Clear();_pending.Clear();_roadQueue.Clear();_dirtyNodes.Clear();_withdrawals.Clear();_initial=true;Status="Not applied";

        }

        public void PreSerialize(Context context) {Clear();RestorePoleDefinitions();}

        private bool FindAssets()

        {

            if(Live(_wire)&&Live(_pole)) return true;

            if(_assetsUnavailable) return false;

            // Native low-voltage wire lanes have no node-object reference. Poles

            // expose their terminals through authored prefab sub-lanes instead.

            using(var items=_assets.ToEntityArray(Allocator.Temp)) foreach(var lane in items)

            {

                var data=EntityManager.GetComponentData<UtilityLaneData>(lane);

                if((data.m_UtilityTypes&UtilityTypes.LowVoltageLine)==0 || data.m_Hanging<=0 || !EntityManager.HasBuffer<SubMesh>(lane) || EntityManager.GetBuffer<SubMesh>(lane,true).Length==0) continue;

                if(_wire==Entity.Null || (_prefabs.TryGetPrefab<PrefabBase>(lane,out var name) && name.name=="Low-voltage Cable Lane")) {_wire=lane;_hanging=data.m_Hanging;}

            }

            float best=float.MaxValue;

            using(var items=_poleAssets.ToEntityArray(Allocator.Temp)) foreach(var pole in items)

            {

                var data=EntityManager.GetComponentData<UtilityObjectData>(pole);

                if((data.m_UtilityTypes&UtilityTypes.LowVoltageLine)==0 || !_prefabs.TryGetPrefab<PrefabBase>(pole,out var prefab) || !prefab.name.StartsWith("PowerLinePole",StringComparison.Ordinal) || prefab.name.IndexOf("Placeholder",StringComparison.OrdinalIgnoreCase)>=0) continue;

                var points=new List<float3>();float height=0;

                foreach(var terminal in EntityManager.GetBuffer<Game.Prefabs.SubLane>(pole,true))

                {

                    if(!EntityManager.HasComponent<UtilityLaneData>(terminal.m_Prefab) || (EntityManager.GetComponentData<UtilityLaneData>(terminal.m_Prefab).m_UtilityTypes&UtilityTypes.LowVoltageLine)==0) continue;

                    var point=terminal.m_Curve.a;

                    if(!math.all(math.isfinite(point)) || point.y<3 || point.y>20 || math.distancesq(point,terminal.m_Curve.d)>.01f) continue;

                    points.Add(point);height=math.max(height,point.y);

                }

                if(points.Count==0) continue;

                float score=math.abs(height-8f)+(prefab.name=="PowerLinePole8m01"?0:.001f);

                if(score<best) {best=score;_pole=pole;_attachments=points.ToArray();}

            }

            if(_wire!=Entity.Null && _pole!=Entity.Null) {PreparePoleHeight();return true;}

            _assetsUnavailable=true;Status=_wire==Entity.Null?"Native overhead wire asset unavailable":"Native roadside pole terminals unavailable";return false;

        }

        private bool CanSharePole(Pole pole,Entity road)
        {
            if(pole.Uses==0||pole.Roads.Contains(road))return true;
            if(!EntityManager.HasComponent<Edge>(road))return false;
            var edge=EntityManager.GetComponentData<Edge>(road);
            foreach(var owner in pole.Roads)if(Live(owner)&&EntityManager.HasComponent<Edge>(owner)&&RoadTopologyPolicy.ShareEndpoint(edge,EntityManager.GetComponentData<Edge>(owner)))return true;
            return false;
        }
        private Pole GetPole(Entity road,float3 position,float3 tangent)

        {

            int3 key=(int3)math.floor(position/10f);float3 axis=math.normalizesafe(new float3(tangent.x,0,tangent.z));

            int radius=(int)math.ceil(_minimumGap/10f);Pole nearest=null;float best=float.MaxValue;

            for(int x=-radius;x<=radius;x++) for(int y=-1;y<=1;y++) for(int z=-radius;z<=radius;z++)

                if(_poles.TryGetValue(key+new int3(x,y,z),out var bucket)) foreach(var existing in bucket)

                {

                    if(!Live(existing.Entity)||existing.Invalidated||!CanSharePole(existing,road)) continue;var delta=existing.Position-position;float distance=math.lengthsq(delta);

                    float lateral=math.abs(delta.x*axis.z-delta.z*axis.x);

                    bool sameRun=math.abs(math.dot(axis,existing.Tangent))>.96f && lateral<1.25f && math.abs(delta.y)<.75f;

                    if((distance<.81f || (sameRun && distance<_minimumGap*_minimumGap)) && distance<best) {nearest=existing;best=distance;}

                }

            if(nearest!=null) {if(nearest.Uses==0) MovePole(nearest,position,axis,road);nearest.Uses++;nearest.Roads.Add(road);_orphans.Remove(nearest);return nearest;}

            quaternion rotation=quaternion.LookRotationSafe(new float3(tangent.x,0,tangent.z),new float3(0,1,0));

            Entity entity;

            using(var created=new NativeArray<Entity>(1,Allocator.Temp))

            {

                EntityManager.CreateEntity(EntityManager.GetComponentData<ObjectData>(_pole).m_Archetype,created);

                entity=created[0];

            }

            EntityManager.SetComponentData(entity,new PrefabRef(_pole));

            EntityManager.SetComponentData(entity,new Game.Objects.Transform(position,rotation));
            if(EntityManager.HasComponent<Game.Objects.Stack>(entity))EntityManager.SetComponentData(entity,new Game.Objects.Stack{m_Range=_poleRange});else EntityManager.AddComponentData(entity,new Game.Objects.Stack{m_Range=_poleRange});

            if(EntityManager.HasComponent<Game.Objects.UtilityObject>(entity)) EntityManager.RemoveComponent<Game.Objects.UtilityObject>(entity);

            if(EntityManager.HasComponent<Game.Objects.NetObject>(entity)) EntityManager.RemoveComponent<Game.Objects.NetObject>(entity);

            if(EntityManager.HasComponent<Owner>(entity)) EntityManager.RemoveComponent<Owner>(entity);

            if(!EntityManager.HasComponent<Created>(entity)) EntityManager.AddComponent<Created>(entity);

            if(!EntityManager.HasComponent<Updated>(entity)) EntityManager.AddComponent<Updated>(entity);

            EntityManager.AddComponent<MGRVisualPowerlineRuntime>(entity);GeneratedVisualLifecycle.Track(World,entity);

            var attachments=new float3[_attachments.Length];

            for(int i=0;i<attachments.Length;i++) attachments[i]=position+math.rotate(rotation,_attachments[i]);

            var pole=new Pole {Entity=entity,Position=position,Tangent=axis,Axis=axis,Attachments=attachments,Uses=1};

            pole.Roads.Add(road);_poleEntities[entity]=pole;

            if(!_poles.TryGetValue(key,out var ownBucket)) {_poles[key]=ownBucket=new List<Pole>();}ownBucket.Add(pole);return pole;

        }

        private Entity Wire(Entity owner,Pole from,Pole to,float3 a,float3 b)

        {

            if(math.distance(a,b)<.25f) return Entity.Null;

            var curve=VisualPowerlinePolicy.Span(a,b,_hanging);

            var entity=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().Spawn(owner,curve,_wire,false);

            EntityManager.AddComponent<MGRVisualPowerlineRuntime>(entity);

            // Strip simulation flow mapping: these wires never carry electricity.

            if(EntityManager.HasComponent<EdgeMapping>(entity)) EntityManager.RemoveComponent<EdgeMapping>(entity);

            if(EntityManager.HasBuffer<SubFlow>(entity)) EntityManager.RemoveComponent<SubFlow>(entity);

            if(EntityManager.HasComponent<HangingLane>(entity)) EntityManager.SetComponentData(entity,new HangingLane {m_Distances=new float2(.1f)});

            _spans.Add(entity,from.Entity,to.Entity,owner);return entity;

        }

        private void JoinPoles(Entity owner,Pole a,Pole b,List<Entity> wires)

        {

            foreach(var pair in VisualPowerlinePolicy.Terminals(a.Attachments,b.Attachments))

            {

                var wire=Wire(owner,a,b,a.Attachments[pair.x],b.Attachments[pair.y]);if(wire!=Entity.Null) wires.Add(wire);

            }

        }

        private void DeleteWire(Entity wire) {_spans.Remove(wire);Delete(wire);}

        private void QueueSpanOwner(Entity owner)

        {

            if(!Live(owner)) {ClearNodeSpans(owner);if(_visuals.ContainsKey(owner)) EnqueueRoad(owner);return;}

            if(EntityManager.HasComponent<Node>(owner)) _dirtyNodes.Add(owner);

            else if(EntityManager.HasComponent<MGRVisualPowerlineUpgrade>(owner)) EnqueueRoad(owner);

        }

        private void InvalidatePole(Pole pole)

        {

            if(pole.Invalidated) return;pole.Invalidated=true;

            foreach(var road in pole.Roads) QueueSpanOwner(road);

            foreach(var wire in _spans.At(pole.Entity)) {if(_spans.Owner(wire,out var owner)) QueueSpanOwner(owner);DeleteWire(wire);}

        }

        // Called at pre-culling for native deletion notifications, before a

        // dangling wire can be drawn. Rendering is rebuilt by the bounded queue.

        internal void NotifyRemoved(Entity entity)

        {

            if(_poleEntities.TryGetValue(entity,out var pole)) InvalidatePole(pole);

            else if(_spans.Owner(entity,out var owner)) {DeleteWire(entity);QueueSpanOwner(owner);}

        }

        private void ClearNodeSpans(Entity node)

        {

            if(!_joins.TryGetValue(node,out var wires)) return;

            foreach(var wire in wires) DeleteWire(wire);_joins.Remove(node);

        }

        private void RemoveRoad(Entity road,bool explicitRemoval=false)

        {

            if(!_visuals.TryGetValue(road,out var visual)) return;

            foreach(var wire in visual.Wires) DeleteWire(wire);

            foreach(var end in visual.Ends) {ClearNodeSpans(end.Node);_dirtyNodes.Add(end.Node);}

            ReleasePoles(road,visual,null);

            _visuals.Remove(road);
            if(explicitRemoval)foreach(var pole in new HashSet<Pole>(visual.Poles))
            {
                if(pole.Uses!=0)continue; // A neighboring upgraded run still owns a shared pole.
                InvalidatePole(pole);Delete(pole.Entity);RemoveFromPool(pole);_poleEntities.Remove(pole.Entity);_orphans.Remove(pole);
            }

        }

        private void RemoveFromPool(Pole pole)

        {

            var key=(int3)math.floor(pole.Position/10f);

            if(_poles.TryGetValue(key,out var bucket)) {bucket.Remove(pole);if(bucket.Count==0) _poles.Remove(key);}

        }

        private void MovePole(Pole pole,float3 position,float3 axis,Entity updatingRoad)

        {

            foreach(var wire in _spans.At(pole.Entity))

            {

                if(_spans.Owner(wire,out var owner) && owner!=updatingRoad) QueueSpanOwner(owner);

                DeleteWire(wire);

            }

            RemoveFromPool(pole);pole.Position=position;pole.Axis=axis;

            var rotation=quaternion.LookRotationSafe(axis,new float3(0,1,0));

            EntityManager.SetComponentData(pole.Entity,new Game.Objects.Transform(position,rotation));

            if(!EntityManager.HasComponent<Updated>(pole.Entity)) EntityManager.AddComponent<Updated>(pole.Entity);

            if(!EntityManager.HasComponent<BatchesUpdated>(pole.Entity)) EntityManager.AddComponent<BatchesUpdated>(pole.Entity);

            for(int i=0;i<pole.Attachments.Length;i++) pole.Attachments[i]=position+math.rotate(rotation,_attachments[i]);

            var key=(int3)math.floor(position/10f);if(!_poles.TryGetValue(key,out var bucket)) _poles[key]=bucket=new List<Pole>();bucket.Add(pole);

        }

        private Pole AcquirePole(Entity road,float3 position,float3 tangent,bool left,RoadVisual previous,HashSet<Pole> used)

        {

            Pole nearest=null;float best=_spacing*_spacing;

            if(previous!=null) for(int i=0;i<previous.Poles.Count;i++)

            {

                var pole=previous.Poles[i];if(previous.Sides[i]!=left || used.Contains(pole) || !Live(pole.Entity) || pole.Invalidated || pole.Roads.Count!=1) continue;

                float distance=math.distancesq(position,pole.Position);if(distance<best) {best=distance;nearest=pole;}

            }

            if(nearest==null) {var shared=GetPole(road,position,tangent);used.Add(shared);return shared;}

            used.Add(nearest);nearest.Uses++;nearest.Roads.Add(road);var axis=math.normalizesafe(new float3(tangent.x,0,tangent.z));

            if(math.distancesq(nearest.Position,position)>.0025f) {nearest.Tangent=axis;MovePole(nearest,position,axis,road);}

            return nearest;

        }

        private void ReleasePoles(Entity road,RoadVisual old,RoadVisual replacement)

        {

            foreach(var pole in old.Poles)

            {

                if(replacement==null || !replacement.Poles.Contains(pole)) pole.Roads.Remove(road);

                if(--pole.Uses!=0) continue;

                // A split/replacement can reuse this pole during the edit burst.

                pole.OrphanTick=_tick;_orphans.Add(pole);

            }

        }

        private void RetireOrphans()

        {

            if(_pending.Count>0) return;var retired=new List<Pole>();

            foreach(var pole in _orphans)

            {

                if(pole.Uses!=0) {retired.Add(pole);continue;}

                if(_tick-pole.OrphanTick<3) continue;

                InvalidatePole(pole);Delete(pole.Entity);RemoveFromPool(pole);_poleEntities.Remove(pole.Entity);retired.Add(pole);

            }

            foreach(var pole in retired) _orphans.Remove(pole);

        }

        private bool BuildRoad(Entity road)

        {

            if(!Live(road)||!EntityManager.HasComponent<MGRVisualPowerlineUpgrade>(road)) {RemoveRoad(road);return true;}

            if(!EntityManager.HasComponent<Edge>(road)||!EntityManager.HasComponent<EdgeGeometry>(road)||!EntityManager.HasComponent<Composition>(road)) return false;

            var composed=EntityManager.GetComponentData<Composition>(road).m_Edge;

            if(!EntityManager.HasComponent<NetCompositionData>(composed)||!EntityManager.HasBuffer<NetCompositionLane>(composed)) return false;

            float roadWidth=EntityManager.GetComponentData<NetCompositionData>(composed).m_Width;

            if(!math.isfinite(roadWidth)||roadWidth<=.1f||EntityManager.GetBuffer<NetCompositionLane>(composed,true).Length==0) return false;

            var curves=new List<Bezier4x3>();var sides=new List<bool>();

            var marking=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>();marking.RefreshDefinitionsIfNeeded();

            byte mask=EntityManager.GetComponentData<MGRVisualPowerlineUpgrade>(road).Sides;

            if(!marking.TryVisualPowerlineCurves(road,mask,curves,sides)) {RemoveRoad(road);Status="Selected road has no supported above-ground roadside geometry";return true;}

            var edge=EntityManager.GetComponentData<Edge>(road);

            int hash=mask;foreach(var c in curves) unchecked {hash=hash*31+c.GetHashCode();}

            if(_visuals.TryGetValue(road,out var previous)&&previous.Hash==hash && previous.StartNode==edge.m_Start && previous.EndNode==edge.m_End)

            {

                bool intact=true;foreach(var pole in previous.Poles) if(!Live(pole.Entity)) {intact=false;break;}

                if(intact) foreach(var wire in previous.Wires) if(!Live(wire)) {intact=false;break;}

                if(intact) return true;

            }

            if(previous!=null) {foreach(var wire in previous.Wires) DeleteWire(wire);foreach(var end in previous.Ends) {ClearNodeSpans(end.Node);_dirtyNodes.Add(end.Node);}}

            var visual=new RoadVisual {Hash=hash,StartNode=edge.m_Start,EndNode=edge.m_End};var used=new HashSet<Pole>();

            for(int side=0;side<2;side++)

            {

                // Arc-length samples span both road halves, avoiding a mandatory

                // middle pole and independent endpoint poles for each half.

                var positions=new List<float3>();var tangents=new List<float3>();var distances=new List<float>();float length=0;

                for(int c=0;c<curves.Count;c++) if(sides[c]==(side==0))

                {

                    var curve=curves[c];

                    for(int k=0;k<=32;k++)

                    {

                        var point=MathUtils.Position(curve,k/32f);

                        if(positions.Count>0) length+=math.distance(positions[positions.Count-1],point);

                        positions.Add(point);tangents.Add(MathUtils.Tangent(curve,k/32f));distances.Add(length);

                    }

                }

                int count=VisualPowerlinePolicy.PoleCount(length,_spacing,_minimumGap);if(count==0 || positions.Count<2) continue;

                Pole before=null;

                for(int i=0;i<count;i++)

                {

                    float at=count==1?length*.5f:length*i/(count-1);int sample=1;while(sample<distances.Count-1 && distances[sample]<at) sample++;

                    float t=math.saturate((at-distances[sample-1])/math.max(.0001f,distances[sample]-distances[sample-1]));

                    var pole=AcquirePole(road,math.lerp(positions[sample-1],positions[sample],t),math.lerp(tangents[sample-1],tangents[sample],t),side==0,previous,used);visual.Poles.Add(pole);visual.Sides.Add(side==0);

                    if(before!=null && before!=pole) JoinPoles(road,before,pole,visual.Wires);before=pole;

                    if(i==0) {visual.Ends.Add(new End {Node=edge.m_Start,Pole=pole});_dirtyNodes.Add(edge.m_Start);}

                    if(i==count-1) {visual.Ends.Add(new End {Node=edge.m_End,Pole=pole});_dirtyNodes.Add(edge.m_End);}

                }

            }

            if(previous!=null) ReleasePoles(road,previous,visual);_visuals[road]=visual;

            Status="Visual powerlines on "+_visuals.Count+" road sections; no electrical service.";return true;

        }

        private bool IsAlleyPole(Pole pole)
        {
            if(pole.Roads.Count==0)return false;
            foreach(var road in pole.Roads)if(Live(road)&&!RoadSurfacePolicy.IsAlley(EntityManager,road))return false;
            return true;
        }

        private bool BuildNode(Entity node)

        {

            ClearNodeSpans(node);

            if(!Live(node)||!EntityManager.HasBuffer<ConnectedEdge>(node)) return true;

            foreach(var connected in EntityManager.GetBuffer<ConnectedEdge>(node,true)) if(_pending.Contains(connected.m_Edge)) return false;

            var poles=new List<Pole>();var points=new List<float3>();

            foreach(var connected in EntityManager.GetBuffer<ConnectedEdge>(node,true)) if(Live(connected.m_Edge)&&EntityManager.HasComponent<MGRVisualPowerlineUpgrade>(connected.m_Edge)&&_visuals.TryGetValue(connected.m_Edge,out var visual))

                foreach(var end in visual.Ends) if(end.Node==node&&end.Pole.Uses>0&&!end.Pole.Invalidated&&Live(end.Pole.Entity)&&!poles.Contains(end.Pole)) {poles.Add(end.Pole);points.Add(end.Pole.Position);}

            var priorities=new List<bool>();foreach(var pole in poles)priorities.Add(IsAlleyPole(pole));
            var tangents=new List<float3>();foreach(var pole in poles)tangents.Add(pole.Tangent);
            var pairs=VisualPowerlinePolicy.StraightConnections(points,tangents,priorities,EntityManager.GetComponentData<Node>(node).m_Position);

            if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("powerline.route","node="+node+" points="+string.Join(";",points)+" tangents="+string.Join(";",tangents)+" selected="+string.Join(";",pairs));
            bool rotated=false;

            for(int i=0;i<poles.Count;i++)

            {

                var pole=poles[i];var junctions=new HashSet<Entity>();

                foreach(var road in pole.Roads) if(_visuals.TryGetValue(road,out var own)) foreach(var end in own.Ends) if(end.Pole==pole) junctions.Add(end.Node);

                if(junctions.Count>1) continue;

                var neighbours=new HashSet<Entity>();var axis=float3.zero;

                foreach(var wire in _spans.At(pole.Entity)) if(_spans.Other(wire,pole.Entity,out var other) && _poleEntities.TryGetValue(other,out var neighbour) && neighbours.Add(other))

                {

                    if(!priorities[i]&&IsAlleyPole(neighbour))continue;
                    var direction=math.normalizesafe(neighbour.Position-pole.Position);direction.y=0;if(math.dot(direction,pole.Tangent)<0) direction=-direction;axis+=direction;

                }

                foreach(var pair in pairs) if((pair.x==i || pair.y==i) && neighbours.Add(poles[pair.x==i?pair.y:pair.x].Entity))

                {

                    int otherIndex=pair.x==i?pair.y:pair.x;if(!priorities[i]&&priorities[otherIndex])continue;
                    var direction=math.normalizesafe(poles[otherIndex].Position-pole.Position);direction.y=0;if(math.dot(direction,pole.Tangent)<0) direction=-direction;axis+=direction;

                }

                axis=math.normalizesafe(axis,pole.Tangent);

                if(math.dot(axis,pole.Axis)<.996f) {MovePole(pole,pole.Position,axis,Entity.Null);rotated=true;}

            }

            if(rotated) return false;

            var spans=new List<Entity>();foreach(var pair in pairs) JoinPoles(node,poles[pair.x],poles[pair.y],spans);

            if(spans.Count>0) _joins[node]=spans;return true;

        }

        protected override void OnUpdate()

        {

            if(!_active||Mod.Saving) return;
            if(_pending.Count==0&&_visuals.Count==0&&_joins.Count==0&&_orphans.Count==0&&_dirtyNodes.Count==0&&_roads.IsEmptyIgnoreFilter)return;

            try

            {

                Dependency.Complete();_tick++;
                foreach(var road in _withdrawals)RemoveRoad(road,true);
                _withdrawals.Clear();


                float spacing=math.clamp(Mod.Settings.PowerlinePoleSpacing,15f,80f),minimum=math.clamp(Mod.Settings.PowerlineMinimumGap,4f,20f);

                if(!math.isfinite(spacing)) spacing=35f;if(!math.isfinite(minimum)) minimum=10f;

                float corner=math.clamp(Mod.Settings.PowerlineCornerSetback,2f,15f);if(!math.isfinite(corner)) corner=6f;

                float height=PoleHeightPolicy.Requested(Mod.Settings.PowerlineHeight);

                if(spacing!=_spacing || minimum!=_minimumGap || corner!=_cornerSetback || height!=_preferredHeight) {Clear();_pole=Entity.Null;_attachments=null;_assetsUnavailable=false;_spacing=spacing;_minimumGap=minimum;_cornerSetback=corner;_preferredHeight=height;}

                using(var gone=_deleted.ToEntityArray(Allocator.Temp)) foreach(var road in gone) RemoveRoad(road);

                try

                {

                    if(!_initial) _roads.SetChangedVersionFilter(new[]{ComponentType.ReadOnly<EdgeGeometry>(),ComponentType.ReadOnly<Composition>()});

                    using(var roads=_roads.ToEntityArray(Allocator.Temp)) foreach(var road in roads) EnqueueRoad(road);

                    if(!_initial)

                    {

                        _roads.SetChangedVersionFilter(ComponentType.ReadOnly<Edge>());

                        using(var roads=_roads.ToEntityArray(Allocator.Temp)) foreach(var road in roads) EnqueueRoad(road);

                    }

                }

                finally {_roads.ResetFilter();}

                _initial=false;

                using(var nodes=_nodes.ToEntityArray(Allocator.Temp)) foreach(var node in nodes) {_dirtyNodes.Add(node);foreach(var edge in EntityManager.GetBuffer<ConnectedEdge>(node,true)) if(EntityManager.HasComponent<MGRVisualPowerlineUpgrade>(edge.m_Edge)) EnqueueRoad(edge.m_Edge);}

                if(_pending.Count>0)

                {

                    if(!FindAssets()) return;

                    Entity road=_roadQueue.Dequeue();_pending.Remove(road);if(!BuildRoad(road)) EnqueueRoad(road);

                }

                int budget=2;var done=new List<Entity>();foreach(var node in new List<Entity>(_dirtyNodes)) {if(budget--<=0) break;if(BuildNode(node)) done.Add(node);}foreach(var node in done) _dirtyNodes.Remove(node);RetireOrphans();

            }

            catch(Exception error) {Clear();Enabled=false;Status="Powerlines stopped: "+error.GetType().Name;Mod.Log.Error("Visual powerlines stopped and owned visuals cleared: "+error);}

        }

    }

}

