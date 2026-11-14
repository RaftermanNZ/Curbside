using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Used only by an actual light enable/remove click. Native overlap flags
    // and secondary stop paint depend on the primary lanes at BOTH endpoints
    // of each rebuilt segment. Appearance-only edits never enter this path.
    internal static class NativeSignalRebuild
    {
        internal static HashSet<Entity> Request(EntityManager manager, Entity node)
        {
            var endpoints = new HashSet<Entity>();
            if (!LiveNode(manager, node)) return endpoints;
            endpoints.Add(node);
            if (manager.HasBuffer<ConnectedEdge>(node))
            {
                using (var edges = manager.GetBuffer<ConnectedEdge>(node, true).ToNativeArray(Allocator.Temp))
                foreach (var entry in edges)
                {
                    var road = entry.m_Edge;
                    if (!manager.Exists(road) || manager.HasComponent<Deleted>(road) || manager.HasComponent<Game.Tools.Temp>(road)
                        || !manager.HasComponent<Road>(road) || !manager.HasComponent<Edge>(road)) continue;
                    var ends = manager.GetComponentData<Edge>(road);
                    if (LiveNode(manager, ends.m_Start)) endpoints.Add(ends.m_Start);
                    if (LiveNode(manager, ends.m_End)) endpoints.Add(ends.m_End);
                    Updated(manager, road);
                }
            }
            foreach (var endpoint in endpoints)
            {
                // Finish borrowing the buffer before any structural writes.
                if (manager.HasBuffer<SubLane>(endpoint))
                using (var lanes = manager.GetBuffer<SubLane>(endpoint, true).ToNativeArray(Allocator.Temp))
                foreach (var entry in lanes)
                {
                    var lane = entry.m_SubLane;
                    if (!manager.Exists(lane) || manager.HasComponent<Deleted>(lane) || manager.HasComponent<Game.Tools.Temp>(lane)
                        || !manager.HasComponent<Lane>(lane) || manager.HasComponent<SecondaryLane>(lane)
                        || manager.HasComponent<MGRCurbMarking>(lane) || manager.HasComponent<MGRIntersectionPaint>(lane)
                        || !manager.HasComponent<Owner>(lane) || manager.GetComponentData<Owner>(lane).m_Owner != endpoint) continue;
                    Updated(manager, lane);
                }
                Updated(manager, endpoint);
            }
            return endpoints;
        }

        private static bool LiveNode(EntityManager manager, Entity node)
            => manager.Exists(node) && manager.HasComponent<Node>(node)
                && !manager.HasComponent<Deleted>(node) && !manager.HasComponent<Game.Tools.Temp>(node);

        private static void Updated(EntityManager manager, Entity entity)
        {
            if (!manager.HasComponent<Updated>(entity)) manager.AddComponent<Updated>(entity);
        }
    }
}
