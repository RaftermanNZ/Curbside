using System.Collections.Generic;
using Game.Net;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // Derived IDs and copied values only. Maintained by the existing road
    // notifications; no new ECS tags, update callbacks, timers or sweeps.
    internal sealed class PowerlineJunctionScope
    {
        internal static bool Supported => MarkingChangeNotifications.Supported;
        private readonly Dictionary<Entity, Edge> roads = new Dictionary<Entity, Edge>();
        private readonly Dictionary<Entity, int> nodes = new Dictionary<Entity, int>();
        internal bool HasJunctions => nodes.Count != 0;

        internal void TrackRoad(Entity road, Edge edge)
        {
            if (roads.TryGetValue(road, out var previous))
            {
                if (previous.m_Start == edge.m_Start && previous.m_End == edge.m_End) return;
                ForgetRoad(road);
            }
            roads[road] = edge;
            AddNode(edge.m_Start); AddNode(edge.m_End);
        }

        private void AddNode(Entity node)
        {
            if (node == Entity.Null) return;
            nodes.TryGetValue(node, out int count); nodes[node] = count + 1;
        }

        private void RemoveNode(Entity node)
        {
            if (!nodes.TryGetValue(node, out int count)) return;
            if (count <= 1) nodes.Remove(node); else nodes[node] = count - 1;
        }

        internal void ForgetRoad(Entity road)
        {
            if (!roads.TryGetValue(road, out var previous)) return;
            roads.Remove(road); RemoveNode(previous.m_Start); RemoveNode(previous.m_End);
        }

        internal bool Relevant(Entity node, bool existingJoin) => existingJoin || nodes.ContainsKey(node);

        internal void Clear() { roads.Clear(); nodes.Clear(); }
    }
}
