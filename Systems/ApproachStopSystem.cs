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
    // Directions survive replacement of the connected road entity. No shared
    // composition or prefab is edited for an individual junction's priority.
    public struct MGRStopApproach : IBufferElementData,ISerializable
    {
        public float3 Outward;
        public byte Stop;
        public void Serialize<T>(T writer)where T:IWriter{writer.Write(Outward);writer.Write(Stop);}
        public void Deserialize<T>(T reader)where T:IReader{reader.Read(out Outward);reader.Read(out Stop);}
    }
    internal static class ApproachStopPolicy
    {
        internal const CarLaneFlags Mask=CarLaneFlags.Stop|CarLaneFlags.Yield|CarLaneFlags.RightOfWay;
        internal static CarLaneFlags Apply(CarLaneFlags flags,bool stop)=>ApplyMode(flags,stop?(byte)1:(byte)0);
        internal static CarLaneFlags ApplyMode(CarLaneFlags flags,byte mode)=>(flags&~Mask)|(mode==1?CarLaneFlags.Stop:mode==2?CarLaneFlags.Yield:CarLaneFlags.RightOfWay);
        internal static bool OwnsPriority(CarLaneFlags current,CarLaneFlags applied)=>(current&Mask)==(applied&Mask);
        internal static bool Opposite(float3 a,float3 b)=>math.dot(math.normalizesafe(a.xz),math.normalizesafe(b.xz))<-.85f;
    }
    public partial class ApproachStopSystem:GameSystemBase,IPreSerialize
    {
        private EntityQuery _nodes,_changedRoads;
        private bool _initial=true;
        private readonly Dictionary<Entity,CarLaneFlags> _original=new Dictionary<Entity,CarLaneFlags>();
        private readonly Dictionary<Entity,CarLaneFlags> _applied=new Dictionary<Entity,CarLaneFlags>();
        private void Forget(Entity entity){_original.Remove(entity);_applied.Remove(entity);}
        private readonly HashSet<Entity> _queued=new HashSet<Entity>();
        internal void Queue(Entity node){_queued.Add(node);}
        protected override void OnCreate(){base.OnCreate();_nodes=GetEntityQuery(ComponentType.ReadOnly<MGRStopApproach>(),ComponentType.ReadOnly<Node>(),ComponentType.ReadOnly<ConnectedEdge>(),ComponentType.ReadOnly<Game.Net.SubLane>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());_changedRoads=GetEntityQuery(ComponentType.ReadOnly<Road>(),ComponentType.ReadOnly<Edge>(),ComponentType.ReadOnly<Updated>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());}
        protected override void OnGamePreload(Purpose purpose,GameMode mode){base.OnGamePreload(purpose,mode);_original.Clear();_applied.Clear();_queued.Clear();_initial=true;}
        protected override void OnGameLoadingComplete(Purpose purpose,GameMode mode){base.OnGameLoadingComplete(purpose,mode);_initial=true;}
        internal static float3 Away(EntityManager em,Entity road,Entity node)
        {
            if(!em.HasComponent<Edge>(road)||!em.HasComponent<Curve>(road))return float3.zero;
            bool start=em.GetComponentData<Edge>(road).m_Start==node;
            return (start?1:-1)*math.normalizesafe(MathUtils.Tangent(em.GetComponentData<Curve>(road).m_Bezier,start?0:1));
        }
        public void PreSerialize(Context context)
        {
            Dependency.Complete();
            foreach(var pair in _original)if(EntityManager.HasComponent<Game.Net.CarLane>(pair.Key)&&!EntityManager.HasComponent<Deleted>(pair.Key))
            {var lane=EntityManager.GetComponentData<Game.Net.CarLane>(pair.Key);if(!_applied.TryGetValue(pair.Key,out var applied)||!ApproachStopPolicy.OwnsPriority(lane.m_Flags,applied))continue;lane.m_Flags=(lane.m_Flags&~ApproachStopPolicy.Mask)|pair.Value;EntityManager.SetComponentData(pair.Key,lane);}
            _original.Clear();_applied.Clear();_initial=true;
        }
        protected override void OnDestroy(){PreSerialize(default);base.OnDestroy();}
        internal void ResetApproach(Entity road,Entity node)
        {
            Dependency.Complete();if(!EntityManager.HasBuffer<MGRStopApproach>(node))return;
            if(EntityManager.HasBuffer<Game.Net.SubLane>(node))foreach(var entry in EntityManager.GetBuffer<Game.Net.SubLane>(node,true))
            {
                var lane=entry.m_SubLane;if(!EntityManager.HasComponent<Game.Net.Lane>(lane)||!EntityManager.HasComponent<Game.Net.CarLane>(lane)||EntityManager.GetComponentData<Game.Net.Lane>(lane).m_StartNode.GetOwnerIndex()!=road.Index||!_original.TryGetValue(lane,out var flags))continue;
                var value=EntityManager.GetComponentData<Game.Net.CarLane>(lane);if(_applied.TryGetValue(lane,out var applied)&&ApproachStopPolicy.OwnsPriority(value.m_Flags,applied)){value.m_Flags=(value.m_Flags&~ApproachStopPolicy.Mask)|flags;EntityManager.SetComponentData(lane,value);}Forget(lane);
            }
            var direction=Away(EntityManager,road,node);var choices=EntityManager.GetBuffer<MGRStopApproach>(node);
            for(int i=choices.Length-1;i>=0;i--)if(CrosswalkIntentPolicy.Score(direction,choices[i].Outward)>.85f)choices.RemoveAt(i);
            if(choices.Length==0)EntityManager.RemoveComponent<MGRStopApproach>(node);
            if(!EntityManager.HasComponent<Updated>(node))EntityManager.AddComponent<Updated>(node);Queue(node);
        }
        protected override void OnUpdate()
        {
            if(Mod.Saving)return;
            using(var roads=_changedRoads.ToEntityArray(Allocator.Temp))foreach(var road in roads)
            {
                var edge=EntityManager.GetComponentData<Edge>(road);
                if(EntityManager.HasBuffer<MGRStopApproach>(edge.m_Start))_queued.Add(edge.m_Start);
                if(EntityManager.HasBuffer<MGRStopApproach>(edge.m_End))_queued.Add(edge.m_End);
            }
            if(!_initial)_nodes.SetChangedVersionFilter(new[]{ComponentType.ReadOnly<Game.Net.SubLane>(),ComponentType.ReadOnly<ConnectedEdge>()});
            using(var nodes=_nodes.ToEntityArray(Allocator.Temp))foreach(var node in nodes)_queued.Add(node);
            _nodes.ResetFilter();_initial=false;
            if(_queued.Count==0)return;
            Dependency.Complete();
            foreach(var node in _queued)
            {
                if(!EntityManager.HasBuffer<MGRStopApproach>(node)||!EntityManager.HasBuffer<Game.Net.SubLane>(node)||!EntityManager.HasBuffer<ConnectedEdge>(node))continue;
                bool nativeAllWay=EntityManager.HasComponent<Upgraded>(node)&&(EntityManager.GetComponentData<Upgraded>(node).m_Flags.m_General&CompositionFlags.General.AllWayStop)!=0;
                if(EntityManager.HasComponent<TrafficLights>(node)||nativeAllWay)
                {
                    foreach(var lane in EntityManager.GetBuffer<Game.Net.SubLane>(node,true))Forget(lane.m_SubLane);
                    if(nativeAllWay)EntityManager.RemoveComponent<MGRStopApproach>(node);
                    continue;
                }
                var choices=EntityManager.GetBuffer<MGRStopApproach>(node,true);
                using(var lanes=EntityManager.GetBuffer<Game.Net.SubLane>(node,true).ToNativeArray(Allocator.Temp))foreach(var entry in lanes)
                {
                    var entity=entry.m_SubLane;
                    if(!EntityManager.HasComponent<Game.Net.CarLane>(entity)||!EntityManager.HasComponent<Game.Net.Lane>(entity)||EntityManager.HasComponent<Deleted>(entity))continue;
                    if(!EntityManager.HasComponent<Owner>(entity)||EntityManager.GetComponentData<Owner>(entity).m_Owner!=node)continue;
                    var lane=EntityManager.GetComponentData<Game.Net.CarLane>(entity);
                    int incoming=EntityManager.GetComponentData<Game.Net.Lane>(entity).m_StartNode.GetOwnerIndex();
                    bool matched=false;byte mode=0;
                    foreach(var road in EntityManager.GetBuffer<ConnectedEdge>(node,true))if(road.m_Edge.Index==incoming)
                    {
                        var direction=Away(EntityManager,road.m_Edge,node);
                        foreach(var choice in choices)if(math.dot(math.normalizesafe(choice.Outward.xz),math.normalizesafe(direction.xz))>.85f){matched=true;mode=choice.Stop;break;}
                        break;
                    }
                    if(!matched)continue;
                    if(!_original.ContainsKey(entity))_original[entity]=lane.m_Flags&ApproachStopPolicy.Mask;
                    var next=ApproachStopPolicy.ApplyMode(lane.m_Flags,mode);
                    _applied[entity]=next&ApproachStopPolicy.Mask;
                    if(next!=lane.m_Flags){lane.m_Flags=next;EntityManager.SetComponentData(entity,lane);}
                }
            }
            _queued.Clear();
            // Discard dead handles only when a selected junction changes.
            foreach(var entity in new List<Entity>(_original.Keys))if(!EntityManager.Exists(entity))Forget(entity);
        }
    }
}
