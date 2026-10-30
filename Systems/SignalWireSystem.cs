using System.Collections.Generic;
using Colossal.Mathematics;using Colossal.Serialization.Entities;
using Game;using Game.Common;using Game.Net;using Game.Prefabs;using Game.Serialization;
using Unity.Collections;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
    public struct MGRSignalWire:IComponentData{}
    internal static class SignalWirePolicy
    {
        internal static float Sag(float span)=>math.clamp(span*.015f,.06f,.55f);
        internal static float Drop(float span,float t)=>4*Sag(span)*math.saturate(t)*(1-math.saturate(t));
        internal static bool Valid(float3 a,float3 b)=>math.all(math.isfinite(a))&&math.all(math.isfinite(b))&&math.distance(a.xz,b.xz)>=2&&math.distance(a.xz,b.xz)<=60;
        internal static Bezier4x3 Span(float3 a,float3 b)
        {
            float drop=4*Sag(math.distance(a.xz,b.xz))/3;
            var first=math.lerp(a,b,1f/3);var last=math.lerp(a,b,2f/3);first.y-=drop;last.y-=drop;
            return new Bezier4x3(a,first,last,b);
        }
    }
    // The game's cable mesh/material, curved and batched through its normal lane
    // renderer. These derived visuals have no electrical graph or saved network.
    public partial class SignalWireSystem:GameSystemBase,IPreSerialize
    {
        private EntityQuery assets,owned,styles;private NetLaneGeometryPrefab pending;private Entity cable;
        private bool registered,active,notified;private float retry,next;
        private sealed class Entry {internal float3[] Supports;internal int2[] Pairs;internal readonly List<Entity> Wires=new List<Entity>();}
        private readonly Dictionary<Entity,Entry> entries=new Dictionary<Entity,Entry>();private readonly List<Entity> removed=new List<Entity>();
        protected override void OnCreate()
        {
            base.OnCreate();assets=GetEntityQuery(ComponentType.ReadOnly<UtilityLaneData>(),ComponentType.ReadOnly<NetLaneArchetypeData>(),ComponentType.ReadOnly<NetLaneGeometryData>());
            owned=GetEntityQuery(ComponentType.ReadOnly<MGRSignalWire>());
            styles=GetEntityQuery(ComponentType.ReadOnly<MGRGardenCitySignals>(),ComponentType.ReadOnly<MGRSignalStyle>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
        }
        internal bool Ready=>cable!=Entity.Null&&EntityManager.Exists(cable)&&EntityManager.HasComponent<NetLaneArchetypeData>(cable)&&EntityManager.HasComponent<NetLaneGeometryData>(cable)&&EntityManager.HasBuffer<SubMesh>(cable)&&EntityManager.GetBuffer<SubMesh>(cable,true).Length>0;
        internal void RegisterPrefab()
        {
            if(!active||Mod.Saving||!GardenCityLightPolicy.PackPresent())return;
            if(Ready)
            {
                if(!notified)
                {
                    notified=true;
                    using(var nodes=styles.ToEntityArray(Allocator.Temp))foreach(var node in nodes)if(EntityManager.GetComponentData<MGRSignalStyle>(node).Value==1)FarSideLightSystem.Queue(node);
                }
                return;
            }
            var prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();
            if(!registered&&prefabs.TryGetPrefab(new PrefabID("NetLaneGeometryPrefab","CSG Visual Signal Cable"),out var existing))
            {
                if(!(existing is NetLaneGeometryPrefab candidate)||candidate.m_PathfindPrefab!=null||!candidate.TryGet<Game.Prefabs.UtilityLane>(out var definition)||definition.m_UtilityType!=UtilityTypes.None||definition.m_Hanging!=0)
                {Mod.Log.Error("Existing signal cable prefab does not match the expected visual-only configuration; refusing duplicate registration.");return;}
                pending=candidate;registered=true;prefabs.TryGetEntity(candidate,out cable);
                Mod.Log.Info("Signal cable reused after world load: entity="+cable+"; no duplicate prefab registered.");return;
            }
            if(registered){if(pending!=null)prefabs.TryGetEntity(pending,out cable);return;}
            if(UnityEngine.Time.realtimeSinceStartup<retry)return;retry=UnityEngine.Time.realtimeSinceStartup+2;
            Dependency.Complete();NetLaneGeometryPrefab source=null;
            using(var items=assets.ToEntityArray(Allocator.Temp))foreach(var item in items)
            {
                var data=EntityManager.GetComponentData<UtilityLaneData>(item);
                if((data.m_UtilityTypes&UtilityTypes.LowVoltageLine)==0||data.m_Hanging<=0||!prefabs.TryGetPrefab<NetLaneGeometryPrefab>(item,out var model)||model.m_Meshes==null||model.m_Meshes.Length==0)continue;
                source=model;if(model.name=="Low-voltage Cable Lane")break;
            }
            if(source==null)return;
            foreach(var mesh in source.m_Meshes)if(mesh.m_Mesh is RenderPrefab render)Mod.Log.Info("Native cable render bounds="+render.bounds+" mesh="+render.name);

            pending=(NetLaneGeometryPrefab)source.Clone("CSG Visual Signal Cable");pending.m_PathfindPrefab=null;
            var utility=pending.GetComponent<Game.Prefabs.UtilityLane>();
            utility.m_UtilityType=UtilityTypes.None;utility.m_LocalConnectionLane=null;utility.m_LocalConnectionLane2=null;utility.m_NodeObject=null;utility.m_VisualCapacity=0;
            // Sag is explicit in the curve so the attachment rods stay connected.
            // Disable extra wind displacement; preserve the native cable shader.
            utility.m_Hanging=0;if(utility.m_Width<=0)utility.m_Width=.02f;
            registered=prefabs.AddPrefab(pending);if(registered)prefabs.TryGetEntity(pending,out cable);
            Mod.Log.Info("Signal cable native visual registration: "+registered+"; source="+source.name+"; shared meshes/materials, no utility connections.");
        }
        internal void Set(Entity node,IList<float3> supports,IList<int2> pairs)
        {
            if(!Ready||!EntityManager.Exists(node)||EntityManager.HasComponent<Deleted>(node)||EntityManager.HasComponent<Game.Tools.Temp>(node))return;
            if(entries.TryGetValue(node,out var previous))
            {
                bool same=previous.Supports.Length==supports.Count&&previous.Pairs.Length==pairs.Count;
                for(int i=0;same&&i<supports.Count;i++)same=math.distancesq(previous.Supports[i],supports[i])<.000001f;
                for(int i=0;same&&i<pairs.Count;i++)same=math.all(previous.Pairs[i]==pairs[i]);
                foreach(var entity in previous.Wires)if(!EntityManager.Exists(entity)||EntityManager.HasComponent<Deleted>(entity))same=false;
                if(same)return;Remove(node);
            }
            var entry=new Entry{Supports=new float3[supports.Count],Pairs=new int2[pairs.Count]};for(int i=0;i<supports.Count;i++)entry.Supports[i]=supports[i];for(int i=0;i<pairs.Count;i++)entry.Pairs[i]=pairs[i];
            entries[node]=entry;var seen=new HashSet<int2>();
            foreach(var pair in pairs)
            {
                if(pair.x<0||pair.y<0||pair.x>=supports.Count||pair.y>=supports.Count||pair.x==pair.y)continue;
                var key=new int2(math.min(pair.x,pair.y),math.max(pair.x,pair.y));if(!seen.Add(key)||!SignalWirePolicy.Valid(supports[pair.x],supports[pair.y]))continue;
                foreach(float height in new[]{7.45f,5.3f})
                {
                    var curve=SignalWirePolicy.Span(supports[pair.x]+new float3(0,height,0),supports[pair.y]+new float3(0,height,0));
                    var entity=World.GetOrCreateSystemManaged<CurbLineEmissionSystem>().Spawn(node,curve,cable,false);
                    EntityManager.AddComponent<MGRSignalWire>(entity);
                    if(ModDiagnostics.TraceBuffer.Enabled)Mod.Log.Info("Signal runtime cable created: entity="+entity+" node="+node);
                    if(EntityManager.HasComponent<EdgeMapping>(entity))EntityManager.RemoveComponent<EdgeMapping>(entity);
                    if(EntityManager.HasBuffer<SubFlow>(entity))EntityManager.RemoveComponent<SubFlow>(entity);
                    float width=EntityManager.GetComponentData<NetLaneData>(cable).m_Width;
                    if(width>0)EntityManager.AddComponentData(entity,new NodeLane{m_WidthOffset=new float2(width*.35f)});
                    entry.Wires.Add(entity);
                }
            }
            Mod.Log.Info("Native signal cables applied: node="+node+" supports="+supports.Count+" spans="+seen.Count+" lanes="+entry.Wires.Count+" thicknessScale=1.35; shared native cable geometry/material, shallow static sag.");
        }
        internal void RecordSnapshot()
        {
            foreach(var item in entries)
            {
                for(int i=0;i<item.Value.Supports.Length;i++)Mod.Log.Info("Wire support snapshot: node="+item.Key+" index="+i+" base="+item.Value.Supports[i]+" upperAnchor="+(item.Value.Supports[i]+new float3(0,7.45f,0))+" expectedTimberTop="+(item.Value.Supports[i].y+7.8f));
                foreach(var wire in item.Value.Wires)if(EntityManager.Exists(wire)&&EntityManager.HasComponent<Curve>(wire))
                {var curve=EntityManager.GetComponentData<Curve>(wire).m_Bezier;Mod.Log.Info("Wire endpoint snapshot: node="+item.Key+" wire="+wire+" start="+curve.a+" end="+curve.d);}
            }
        }
        internal void Remove(Entity node)
        {
            if(!entries.TryGetValue(node,out var entry))return;
            foreach(var entity in entry.Wires)if(EntityManager.Exists(entity)&&!EntityManager.HasComponent<Deleted>(entity))GeneratedVisualLifecycle.Retire(World,entity);
            entries.Remove(node);
        }
        protected override void OnUpdate()
        {long started=ModDiagnostics.RuntimeCosts.Begin();try{UpdateMeasuredCore();}finally{ModDiagnostics.RuntimeCosts.End("SignalWireSystem",started);}}
        private void UpdateMeasuredCore()
        {
            if(Mod.Saving||entries.Count==0||UnityEngine.Time.realtimeSinceStartup<next)return;next=UnityEngine.Time.realtimeSinceStartup+1;
            Dependency.Complete();removed.Clear();foreach(var item in entries)
            {
                var node=item.Key;if(!EntityManager.Exists(node)||EntityManager.HasComponent<Deleted>(node)||!EntityManager.HasComponent<TrafficLights>(node)||!EntityManager.HasComponent<MGRGardenCitySignals>(node)||!EntityManager.HasComponent<MGRSignalStyle>(node)||EntityManager.GetComponentData<MGRSignalStyle>(node).Value!=1)removed.Add(node);
            }
            foreach(var node in removed)Remove(node);
        }
        internal void Clear()
        {
            Dependency.Complete();using(var items=owned.ToEntityArray(Allocator.Temp))foreach(var entity in items)GeneratedVisualLifecycle.Retire(World,entity);entries.Clear();
        }
        public void PreSerialize(Context context){Clear();}
        protected override void OnGameLoadingComplete(Purpose purpose,GameMode mode){base.OnGameLoadingComplete(purpose,mode);active=(mode&GameMode.GameOrEditor)!=GameMode.None;retry=next=0;}
        protected override void OnGamePreload(Purpose purpose,GameMode mode){Clear();cable=Entity.Null;pending=null;notified=registered=active=false;base.OnGamePreload(purpose,mode);}
        protected override void OnDestroy(){Clear();base.OnDestroy();}
    }
}
