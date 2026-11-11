using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using ObjectLight = Game.Objects.TrafficLight;
using ObjectChild = Game.Objects.SubObject;

namespace MedianGrassReplacement.Systems
{
    public partial class FarSideLightSystem
    {
        private struct SignalActorInput
        {
            internal Entity Actor, Owner, Prefab;
            internal Transform Pose;
            internal ushort Vehicle, Pedestrian;
        }
        private sealed class SignalNodeTicket
        {
            internal SignalLayoutEntry Network;
            internal byte Style;
            internal bool Custom, Enabled;
            internal List<SignalActorInput> Actors;
        }
        private readonly Dictionary<Entity, SignalNodeTicket> signalTickets = new Dictionary<Entity, SignalNodeTicket>();
        private SignalNodeTicket ReadSignalTicket(Entity node)
        {
            signalLayouts.TryGetValue(node, out var network);
            var ticket = new SignalNodeTicket { Network = network, Enabled = EnabledNode(node), Custom = EntityManager.HasComponent<MGRGardenCitySignals>(node),
                Style = EntityManager.HasComponent<MGRSignalStyle>(node) ? EntityManager.GetComponentData<MGRSignalStyle>(node).Value : (byte)0,
                Actors = new List<SignalActorInput>() };
            AddTicketActors(node,node, ticket.Actors);
            if (EntityManager.HasBuffer<ConnectedEdge>(node)) foreach (var road in EntityManager.GetBuffer<ConnectedEdge>(node, true)) AddTicketActors(road.m_Edge,node, ticket.Actors);
            ticket.Actors.Sort((a,b) => { int index = a.Actor.Index.CompareTo(b.Actor.Index); return index != 0 ? index : a.Actor.Version.CompareTo(b.Actor.Version); });
            return ticket;
        }
        private void AddTicketActors(Entity owner, Entity junction, List<SignalActorInput> output)
        {
            CopySignalChildren(owner,signalTicketChildren);
            foreach(var actor in signalTicketChildren)
            {
                if (!Live(actor) || !EntityManager.HasComponent<PrefabRef>(actor) || !EntityManager.HasComponent<Transform>(actor)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab;
                if (!EntityManager.HasComponent<TrafficLightData>(prefab) && !EntityManager.HasComponent<StreetLightData>(prefab)) continue;
                if(!EntityManager.HasComponent<Owner>(actor)||EntityManager.GetComponentData<Owner>(actor).m_Owner!=owner)continue;
                var pose=EntityManager.GetComponentData<Transform>(actor);
                if(owner!=junction)
                {
                    if(!EntityManager.HasComponent<Edge>(owner)||!EntityManager.HasComponent<Node>(junction))continue;
                    var ends=EntityManager.GetComponentData<Edge>(owner);var other=ends.m_Start==junction?ends.m_End:ends.m_Start;
                    if(!EntityManager.HasComponent<Node>(other))continue;
                    var centre=EntityManager.GetComponentData<Node>(junction).m_Position;
                    float distance=math.distancesq(pose.m_Position.xz,centre.xz);
                    if(distance>2500||distance>=math.distancesq(pose.m_Position.xz,EntityManager.GetComponentData<Node>(other).m_Position.xz)||math.abs(pose.m_Position.y-centre.y)>2)continue;
                }
                var binding = EntityManager.HasComponent<ObjectLight>(actor) ? EntityManager.GetComponentData<ObjectLight>(actor) : default;
                output.Add(new SignalActorInput { Actor=actor, Owner=EntityManager.HasComponent<Owner>(actor)?EntityManager.GetComponentData<Owner>(actor).m_Owner:Entity.Null,
                    Prefab=prefab, Pose=pose, Vehicle=binding.m_GroupMask0, Pedestrian=binding.m_GroupMask1 });
            }
        }
        private bool UnchangedSignalNode(Entity node)
        {
            if (forcedSignalWork.Contains(node) || waitingSignalDefinitions.Contains(node) || !signalTickets.TryGetValue(node, out var before)) return false;
            if (before.Network == null || !SameNetwork(node,before.Network)) return false;
            var now = ReadSignalTicket(node);
            if (now.Network == null || !ReferenceEquals(now.Network,before.Network) || now.Enabled != before.Enabled || now.Custom != before.Custom || now.Style != before.Style || now.Actors.Count != before.Actors.Count) return false;
            for (int i=0;i<now.Actors.Count;i++) if (!now.Actors[i].Equals(before.Actors[i])) return false;
            unchangedSignalNotifications++; return true;
        }
        private void RememberSignalNode(Entity node) => signalTickets[node] = ReadSignalTicket(node);
    }
}
