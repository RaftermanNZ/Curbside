using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal static partial class GeneratedVisualLifecycle
    {
        internal static void DetachLaneMembership(EntityManager manager, Entity owner, Entity entity)
        {
            bool found = false;
            {
                var lanes = manager.GetBuffer<Game.Net.SubLane>(owner, true);
                foreach (var lane in lanes)
                    if (lane.m_SubLane == entity) { found = true; break; }
            }
            if (!found) return;

            // The caller has already completed native readers. Reacquire only
            // after finding a real removal; keep all duplicate-removal behavior.
            var writable = manager.GetBuffer<Game.Net.SubLane>(owner);
            for (int i = writable.Length - 1; i >= 0; i--)
                if (writable[i].m_SubLane == entity) writable.RemoveAt(i);
        }
    }
}
