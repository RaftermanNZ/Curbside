using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal sealed partial class PaintOwnerNotifications
    {
        // Reuse cold slots above the original soft size. A working set larger
        // than that size must be retained rather than called changed forever.
        // Only existing notification phases advance this age; quiet updates do
        // not age out a cohort and there is no periodic cache scan.
        private const int CacheVictimVisits = 8;
        private const ulong CacheRetentionPhases = 64;
        private const int MaximumCachedOwners = 4096;
        private const long MaximumRetainedSlots = 262144;
        private const int MaximumBudgetEvictions = 16;
        private long retainedSlots;
        private bool cachePhase, cacheObserved;
        private ulong cacheEpoch;

        private void BeginCacheBatch() { cachePhase = true; cacheObserved = false; }
        private void EndCacheBatch() { cachePhase = cacheObserved = false; }
        private void ClearCacheAge() { retainedSlots = 0; cacheEpoch = 0; cachePhase = cacheObserved = false; }

        private void ObserveCacheOwner(Entity owner)
        {
            if (!cachePhase) return;
            if (!cacheObserved) { cacheEpoch++; cacheObserved = true; }
            if (entries.TryGetValue(owner, out var entry)) entry.SeenCacheEpoch = cacheEpoch;
        }

        private bool TryChooseCacheVictim(out Entity victim)
        {
            victim = Entity.Null;
            if (entries.Count < Capacity) return true;
            // Without a notification epoch there is no evidence that another
            // observed owner is cold. Do not evict an active cohort arbitrarily.
            if (!cachePhase) return true;
            int visits = CacheVictimVisits;
            while (visits-- > 0)
            {
                var candidate = order.Peek();
                var entry = entries[candidate];
                if (cacheEpoch >= entry.SeenCacheEpoch && cacheEpoch - entry.SeenCacheEpoch >= CacheRetentionPhases)
                {
                    victim = candidate;
                    return true;
                }
                order.Enqueue(order.Dequeue());
            }
            // All inspected entries are still active. Retain this owner's real
            // snapshot too; the next exact comparison can then reject a no-op.
            // Cold entries remain reusable on later insertions. Per-owner
            // member limits and World/load Clear boundaries remain unchanged.
            return true;
        }

        private static long SnapshotSlots(OwnerInput input)
        {
            if (input == null) return 0;
            return (input.Members?.LongLength ?? 0) + (input.Lanes?.LongLength ?? 0)
                + (input.Connections?.LongLength ?? 0) + (input.ConnectedRoads?.LongLength ?? 0)
                + (input.CompositionLanes?.LongLength ?? 0) + (input.CompositionPieces?.LongLength ?? 0)
                + (input.Sections?.LongLength ?? 0);
        }

        private void EvictCacheHead()
        {
            var victim = order.Peek();
            var old = entries[victim];
            sharedInputs?.Remove(victim);
            entries.Remove(victim); order.Dequeue(); retainedSlots -= old.RetainedSlots;
        }

        private bool MakeCacheRoom(Entity owner, Entry previous, long slots)
        {
            if (slots > MaximumRetainedSlots) return false;
            long replacing = previous?.RetainedSlots ?? 0;
            // Reuse a genuinely cold soft-limit slot, not an owner whose cohort
            // was simply absent from the previous one or two notifications.
            if (previous == null && TryChooseCacheVictim(out var cold) && cold != Entity.Null)
                EvictCacheHead();
            int evictions = 0;
            while (entries.Count + (previous == null ? 1 : 0) > MaximumCachedOwners
                || retainedSlots - replacing + slots > MaximumRetainedSlots)
            {
                // A large snapshot cannot turn one insertion into an unbounded
                // cache drain. An unavailable memo still takes full processing.
                if (evictions++ == MaximumBudgetEvictions) return false;
                // The previous snapshot being replaced is accounted for above;
                // keep it intact until the successful commit below.
                if (order.Peek() == owner) order.Enqueue(order.Dequeue());
                EvictCacheHead();
            }
            retainedSlots -= replacing;
            return true;
        }
    }
}
