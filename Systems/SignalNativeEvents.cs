using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using ObjectLight=Game.Objects.TrafficLight;

namespace MedianGrassReplacement.Systems
{
    internal static class SignalNativePhasePolicy
    {
        // Native secondary creation/role binding and barrier playback finish
        // in 4B. Placement must still precede search/override in phase 5.
        internal static bool CanApply(SystemUpdatePhase phase,bool saving,bool active)
            =>active&&!saving&&phase==SystemUpdatePhase.Modification5;
    }
    public partial class FarSideLightSystem
    {
        private EntityQuery nativeSignalNodes,nativeSignalRoads,nativeSignalActors;
        private UpdateSystem signalUpdateSystem;
        private long nativeNodeEvents,nativeRoadEvents,nativeActorEvents;
        private static readonly ComponentType[] SignalEventTags={ComponentType.ReadOnly<Created>(),ComponentType.ReadOnly<Updated>(),ComponentType.ReadOnly<Deleted>()};
        private void InitializeNativeSignalEvents()
        {
            signalUpdateSystem=World.GetOrCreateSystemManaged<UpdateSystem>();
            nativeSignalNodes=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Node>(),ComponentType.ReadOnly<MGRFarSideLights>()},Any=SignalEventTags,None=new[]{ComponentType.ReadOnly<Game.Tools.Temp>()}});
            nativeSignalRoads=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Road>(),ComponentType.ReadOnly<Edge>()},Any=SignalEventTags,None=new[]{ComponentType.ReadOnly<Game.Tools.Temp>()}});
            var common=new[]{ComponentType.ReadOnly<Owner>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.ReadOnly<Transform>()};
            nativeSignalActors=GetEntityQuery(
                new EntityQueryDesc{All=new[]{common[0],common[1],common[2],ComponentType.ReadOnly<ObjectLight>()},Any=SignalEventTags,None=new[]{ComponentType.ReadOnly<Game.Tools.Temp>()}},
                new EntityQueryDesc{All=new[]{common[0],common[1],common[2],ComponentType.ReadOnly<StreetLight>()},Any=SignalEventTags,None=new[]{ComponentType.ReadOnly<Game.Tools.Temp>()}});
        }
        private void GatherNativeSignalEvents(System.Collections.Generic.HashSet<Entity> nodes)
        {
            nativeSignalChildren.BeginPass();signalPresentationChecks.Clear();
            using(var changed=nativeSignalNodes.ToEntityArray(Allocator.Temp))foreach(var node in changed){nativeNodeEvents++;nodes.Add(node);}
            using(var changed=nativeSignalRoads.ToEntityArray(Allocator.Temp))foreach(var road in changed)
            {
                nativeRoadEvents++;var edge=EntityManager.GetComponentData<Edge>(road);
                if(EnabledNode(edge.m_Start))nodes.Add(edge.m_Start);
                if(EnabledNode(edge.m_End))nodes.Add(edge.m_End);
            }
            using(var changed=nativeSignalActors.ToEntityArray(Allocator.Temp))foreach(var actor in changed)
            {
                nativeActorEvents++;
                if(Live(actor)&&EntityManager.HasComponent<Owner>(actor))nativeSignalChildren.Record(EntityManager.GetComponentData<Owner>(actor).m_Owner,actor);
                ChangedSignalActor(actor,nodes);
            }
        }
    }
}
