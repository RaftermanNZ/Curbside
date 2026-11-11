using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using ObjectLight = Game.Objects.TrafficLight;

namespace MedianGrassReplacement.Systems
{
    public partial class FarSideLightSystem
    {
        private readonly SignalMaintenanceIndex<Entity> maintenance = new SignalMaintenanceIndex<Entity>();
        private readonly List<SignalMaintenanceIndex<Entity>.Entry> maintenanceWork = new List<SignalMaintenanceIndex<Entity>.Entry>();
        private readonly HashSet<Entity> forcedSignalWork = new HashSet<Entity>();
        private long maintenanceChecks, recoveryChecks, unchangedSignalNotifications;
        private void TrackLayoutNode(Entity node) => maintenance.Track(node,node,SignalMaintenanceIndex<Entity>.LayoutNode);
        private bool ForgetMoved(Entity actor) { maintenance.Forget(actor, SignalMaintenanceIndex<Entity>.Moved); return _moved.Remove(actor); }
        private bool ForgetLamp(Entity actor) { maintenance.Forget(actor, SignalMaintenanceIndex<Entity>.Lamp); return _lamps.Remove(actor); }
        private void MaintainActor(SignalMaintenanceIndex<Entity>.Entry entry, HashSet<Entity> work)
        {
            maintenanceChecks++;
            bool live = Live(entry.Actor); byte state = MaintenanceNode(entry.Node);
            if((entry.Kinds&SignalMaintenanceIndex<Entity>.LayoutNode)!=0&&((state&1)==0||!signalLayouts.ContainsKey(entry.Node)))
            {signalLayouts.Remove(entry.Node);signalTickets.Remove(entry.Node);maintenance.Forget(entry.Actor,SignalMaintenanceIndex<Entity>.LayoutNode);}
            if(!Live(entry.Node)){signalLayouts.Remove(entry.Node);signalTickets.Remove(entry.Node);}
            if(!SignalMaintenancePolicy.KeepCustom(state))UnwatchLampRoads(entry.Node);
            if ((entry.Kinds & SignalMaintenanceIndex<Entity>.Moved) != 0 && _moved.TryGetValue(entry.Actor, out var moved))
            {
                var owner=live&&EntityManager.HasComponent<Owner>(entry.Actor)?EntityManager.GetComponentData<Owner>(entry.Actor).m_Owner:Entity.Null;
                if (owner!=moved.Owner||!SignalMaintenancePolicy.KeepMoved(live, state, moved.AppliedPrefab == moved.Prefab))
                { if (!Live(entry.Node)) signalLayouts.Remove(entry.Node); Undo(entry.Actor, moved); ForgetMoved(entry.Actor); }
            }
            if ((entry.Kinds & SignalMaintenanceIndex<Entity>.Lamp) != 0 && _lamps.TryGetValue(entry.Actor, out var lamp))
            {
                var owner=live&&EntityManager.HasComponent<Owner>(entry.Actor)?EntityManager.GetComponentData<Owner>(entry.Actor).m_Owner:Entity.Null;
                if (!live || owner!=lamp.Owner || !SignalMaintenancePolicy.KeepCustom(state)) { Undo(entry.Actor, lamp); ForgetLamp(entry.Actor); }
            }
            if ((entry.Kinds & SignalMaintenanceIndex<Entity>.Hidden) != 0 && _hiddenSignals.ContainsKey(entry.Actor))
            {
                bool signal=_suppressedPrefabs.TryGetValue(entry.Actor,out var source)&&EntityManager.HasComponent<TrafficLightData>(source.Original)&&!EntityManager.HasComponent<StreetLightData>(source.Original);
                if (!live || !OwnsSuppressedActor(entry.Actor,entry.Node) || !SignalMaintenancePolicy.KeepSuppressed(state,signal)) ReleaseHiddenSignal(entry.Actor);
                else if (_suppressedPrefabs.TryGetValue(entry.Actor, out var suppressed) &&
                    (!EntityManager.HasComponent<PrefabRef>(entry.Actor) || EntityManager.GetComponentData<PrefabRef>(entry.Actor).m_Prefab != suppressed.Applied))
                { ReleaseHiddenSignal(entry.Actor); work.Add(entry.Node); }
            }
            if ((entry.Kinds & SignalMaintenanceIndex<Entity>.GeneratedLamp) != 0)
            {
                if (!SignalMaintenancePolicy.KeepCustom(state)) RetireGeneratedLamps(entry.Node);
                else if (!live) { maintenance.Forget(entry.Actor, SignalMaintenanceIndex<Entity>.GeneratedLamp); work.Add(entry.Node); }
            }
        }
        private void MaintainChangedSignals(HashSet<Entity> work, bool recovery)
        {
            _maintenanceNodes.Clear();
            try
            {
                // Snapshot the work set: retirement can enqueue one of its owners.
                _maintenanceChangedNodes.Clear(); _maintenanceChangedNodes.AddRange(work);
                foreach (var node in _maintenanceChangedNodes)
                { maintenance.CopyNode(node, maintenanceWork); foreach (var entry in maintenanceWork) MaintainActor(entry, work); }
                if (!recovery) return;
                maintenance.TakeRecovery(64, maintenanceWork);
                foreach (var entry in maintenanceWork)
                {
                    recoveryChecks++; MaintainActor(entry, work);
                    if (TrackedSignalChanged(entry.Actor, entry.Node)) work.Add(entry.Node);
                }
            }
            finally { _maintenanceNodes.Clear(); }
        }
        private readonly List<Entity> _maintenanceChangedNodes = new List<Entity>();
        private bool TrackedSignalChanged(Entity actor, Entity node)
        {
            if (!Live(actor)) return true;
            if (_moved.TryGetValue(actor, out var moved))
            {
                if (!EntityManager.HasComponent<Owner>(actor) || EntityManager.GetComponentData<Owner>(actor).m_Owner != moved.Owner ||
                    !EntityManager.HasComponent<PrefabRef>(actor) || !EntityManager.HasComponent<Transform>(actor) || !EntityManager.HasComponent<ObjectLight>(actor)) return true;
                var binding = EntityManager.GetComponentData<ObjectLight>(actor);
                return EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab != moved.AppliedPrefab ||
                    !EntityManager.GetComponentData<Transform>(actor).Equals(moved.Applied) ||
                    binding.m_GroupMask0 != moved.AppliedVehicleMask || binding.m_GroupMask1 != moved.AppliedPedMask;
            }
            if (_lamps.TryGetValue(actor, out var lamp))
                return !EntityManager.HasComponent<Owner>(actor) || EntityManager.GetComponentData<Owner>(actor).m_Owner != lamp.Owner ||
                    !EntityManager.HasComponent<PrefabRef>(actor) || EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab != lamp.AppliedPrefab;
            if (_suppressedPrefabs.TryGetValue(actor, out var suppressed))
            {
                if(!OwnsSuppressedActor(actor,node))return true;
                if (!EntityManager.HasComponent<PrefabRef>(actor) || EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab != suppressed.Applied) return true;
                if (EntityManager.HasComponent<ObjectLight>(actor))
                { var b = EntityManager.GetComponentData<ObjectLight>(actor); return b.m_GroupMask0 != suppressed.VehicleMask || b.m_GroupMask1 != suppressed.PedestrianMask; }
            }
            return false;
        }
        private bool OwnsSuppressedActor(Entity actor,Entity node)
        {
            if(!EntityManager.HasComponent<Owner>(actor))return false;
            var owner=EntityManager.GetComponentData<Owner>(actor).m_Owner;if(owner==node)return true;
            if(!EntityManager.HasComponent<Edge>(owner)||!EntityManager.HasComponent<Node>(node)||!EntityManager.HasComponent<Transform>(actor))return false;
            var edge=EntityManager.GetComponentData<Edge>(owner);if(edge.m_Start!=node&&edge.m_End!=node)return false;
            var other=edge.m_Start==node?edge.m_End:edge.m_Start;if(!EntityManager.HasComponent<Node>(other))return false;
            var point=EntityManager.GetComponentData<Transform>(actor).m_Position;
            return math.distancesq(point.xz,EntityManager.GetComponentData<Node>(node).m_Position.xz)<math.distancesq(point.xz,EntityManager.GetComponentData<Node>(other).m_Position.xz);
        }
        private void ChangedSignalActor(Entity actor, HashSet<Entity> nodes)
        {
            if (maintenance.TryGet(actor, out var entry))
            { if (!TrackedSignalChanged(actor, entry.Node)) return; nodes.Add(entry.Node); }
            if (!EntityManager.HasComponent<Owner>(actor)) return;
            var owner = EntityManager.GetComponentData<Owner>(actor).m_Owner;
            if (EnabledNode(owner)) {signalPresentationChecks.Add(owner);nodes.Add(owner);}
            else if (EntityManager.HasComponent<Edge>(owner))
            { var edge = EntityManager.GetComponentData<Edge>(owner); if (EnabledNode(edge.m_Start)) {signalPresentationChecks.Add(edge.m_Start);nodes.Add(edge.m_Start);} if (EnabledNode(edge.m_End)) {signalPresentationChecks.Add(edge.m_End);nodes.Add(edge.m_End);} }
        }
    }
}
