using Game.Common;
using Game.Net;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public partial class IntersectionPaintSystem
    {
        internal void RequestCommittedRoadPaint(Entity road)
        {
            if (!EntityManager.Exists(road) || EntityManager.HasComponent<Deleted>(road)
                || EntityManager.HasComponent<Game.Tools.Temp>(road)
                || !EntityManager.HasComponent<Edge>(road)) return;

            // Queue IDs only; existing source reconciliation reads the current
            // geometry and choices. Native owner/source events cover later children.
            var edge = EntityManager.GetComponentData<Edge>(road);
            RequestOwnerPaint(road);
            RequestOwnerPaint(edge.m_Start);
            if (edge.m_End != edge.m_Start) RequestOwnerPaint(edge.m_End);
        }

        private void RequestOwnerPaint(Entity owner)
        {
            if (!EntityManager.Exists(owner) || EntityManager.HasComponent<Deleted>(owner)
                || EntityManager.HasComponent<Game.Tools.Temp>(owner)) return;
            if (!EntityManager.HasBuffer<SubLane>(owner)) return;
            foreach (var entry in EntityManager.GetBuffer<SubLane>(owner, true))
            {
                var source = entry.m_SubLane;
                // Use the existing source query's archetype contract, without
                // its change-version filter. Never feed generated paint,
                // vehicle lanes or temporary actors into this commit path.
                if (!EntityManager.Exists(source) || !_sources.MatchesIgnoreFilter(source)) continue;
                RequestSource(source);
                _clipRetries.Remove(source);
            }
        }
    }
}
