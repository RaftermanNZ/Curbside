using System.Collections.Generic;
using Game.Net;
using Game.Objects;
using Unity.Entities;
using Unity.Mathematics;

namespace MedianGrassReplacement.Systems
{
    internal static class SignalEventRoutingPolicy
    {
        internal static bool NearJunction(float3 point, float3 centre, float3 opposite)
        {
            // Unknown/broken native positions retain the broad notification.
            if (!math.all(math.isfinite(point)) || !math.all(math.isfinite(centre))
                || !math.all(math.isfinite(opposite))) return true;
            float distance = math.distancesq(point.xz, centre.xz);
            return distance <= 2500f && distance < math.distancesq(point.xz, opposite.xz)
                && math.abs(point.y - centre.y) <= 2f;
        }
    }

    public partial class FarSideLightSystem
    {
        // Derived from the existing actor tickets, not a second world scan.
        // Reverse lookup: an actor event -> every ticket which used that actor.
        private const int MaximumSignalRouteLinks = 32768;
        private readonly NotificationLookup<Entity> signalTicketRoutes = new NotificationLookup<Entity>();
        private readonly List<Entity> signalRouteActors = new List<Entity>();
        private int signalRouteLinks;
        private bool signalRoutesReady = true;

        private void ReplaceSignalTicket(Entity node, SignalNodeTicket ticket)
        {
            signalTickets.TryGetValue(node, out var old);
            signalTickets[node] = ticket;
            if (!signalRoutesReady) return;
            int next = signalRouteLinks - (old == null ? 0 : old.Actors.Count) + ticket.Actors.Count;
            if (next > MaximumSignalRouteLinks)
            {
                // Without complete old membership, current-position filtering
                // would be unsafe. Keep the original broad route until reset.
                signalTicketRoutes.Clear(); signalRouteActors.Clear();
                signalRouteLinks = 0; signalRoutesReady = false; return;
            }
            try
            {
                signalRouteActors.Clear();
                foreach (var input in ticket.Actors) signalRouteActors.Add(input.Actor);
                signalTicketRoutes.Replace(node, signalRouteActors);
                signalRouteLinks = next;
            }
            catch
            {
                signalTicketRoutes.Clear(); signalRouteLinks = 0; signalRoutesReady = false;
            }
            finally { signalRouteActors.Clear(); }
        }

        private void RemoveSignalTicket(Entity node)
        {
            if (signalTickets.TryGetValue(node, out var old) && signalRoutesReady)
                signalRouteLinks -= old.Actors.Count;
            signalTickets.Remove(node);
            signalTicketRoutes.Remove(node);
        }

        private void ClearSignalTickets()
        {
            signalTickets.Clear(); signalTicketRoutes.Clear(); signalRouteActors.Clear();
            signalRouteLinks = 0; signalRoutesReady = true;
        }

        private bool RoadSignalEventRelevant(Entity actor, Entity node, Entity opposite)
        {
            if (!signalRoutesReady || !EntityManager.HasComponent<Transform>(actor)
                || !EntityManager.HasComponent<Node>(node) || !EntityManager.HasComponent<Node>(opposite)) return true;
            // This is the same original pose and geometric domain used by
            // EdgeMedianSignals. Previously used tickets are notified separately.
            return SignalEventRoutingPolicy.NearJunction(OriginalTransform(actor).m_Position,
                EntityManager.GetComponentData<Node>(node).m_Position,
                EntityManager.GetComponentData<Node>(opposite).m_Position);
        }
    }
}
