using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal sealed partial class PaintOwnerNotifications
    {
        // A coarse Node/ConnectedEdge notification can cover a whole unchanged
        // native chunk. This never acknowledges an owner or replaces discovery
        // of unknown members. Unsupported and small chunks retain the old path.
        internal bool UnchangedTopologyChunk(EntityManager manager, ExternalPaintOwnership external,
            ArchetypeChunk chunk, int candidateCount, ref bool phaseStarted)
            => UnchangedGeometryChunk(manager, external, chunk, candidateCount, false, ref phaseStarted);

        internal bool UnchangedGeometryChunk(EntityManager manager, ExternalPaintOwnership external,
            ArchetypeChunk chunk, int candidateCount, bool roadContext, ref bool phaseStarted)
        {
            if (chunk.Count < 8 || candidateCount <= 32 || !OwnerInputEquality.Supported || !PaintInputReadScope.Supported
                || sharedClient == null || !sharedClient.Matches(manager, external)) return false;
            if (!phaseStarted) { BeginBatch(); phaseStarted = true; }
            return sharedBatch && batch != null && batch.ValuesEnabled
                && sharedClient.UnchangedGeometryChunk(manager, chunk, candidateCount, roadContext);
        }
        internal bool UsesTopologyChunks(EntityManager manager, ExternalPaintOwnership external)
            => sharedClient != null && OwnerInputEquality.Supported && PaintInputReadScope.Supported
                && sharedClient.Matches(manager, external);

        private sealed partial class SharedInputs
        {
            internal bool UnchangedTopologyChunk(EntityManager manager, ExternalPaintOwnership foreign,
                ArchetypeChunk native, int candidateCount, SharedAuthority.SharedClient client)
                => UnchangedGeometryChunk(manager, foreign, native, candidateCount, false, client);

            internal bool UnchangedGeometryChunk(EntityManager manager, ExternalPaintOwnership foreign,
                ArchetypeChunk native, int candidateCount, bool roadContext, SharedAuthority.SharedClient client)
            {
                if (candidateCount <= CohortThreshold) return false;
                ulong sequence = native.SequenceNumber;
                if (!chunks.TryGetValue(sequence, out var recorded)) return false;
                bool acknowledged = false;
                foreach (var source in recorded.Sources)
                {
                    if (!owners.TryGetValue(source.Entity, out var owner) || owner.Entry == null || owner.Entry.RoadContext != roadContext) continue;
                    acknowledged = client.AcknowledgesGeometry(source.Entity, owner.Entry, roadContext);
                    break;
                }
                if (!acknowledged) return false;
                if (!cohortObserved)
                {
                    cohortObserved = true;
                    try { cohortAvailable = ObserveCohorts(manager, foreign); }
                    catch { cohortAvailable = false; }
                }
                if (!cohortAvailable || !cohortSamplesComplete
                    || !chunks.TryGetValue(sequence, out var saved) || !saved.Armed
                    || native.GetOrderVersion() != saved.Order) return false;

                int known = 0;
                var archetype = native.Archetype;
                foreach (var source in saved.Sources)
                {
                    if (!owners.TryGetValue(source.Entity, out var owner)) continue;
                    var entry = owner.Entry;
                    if (entry == null || entry.RoadContext != roadContext) continue;
                    if (!owner.Complete || owner.Dirty || !source.Linked || source.Sequence != sequence
                        || !source.Archetype.Equals(archetype)
                        || !entry.Owner.State.Exists || (roadContext ? !entry.Owner.State.IsRoad || !entry.Owner.State.Edge.Present : !entry.Owner.State.Node.Present || entry.Owner.State.IsRoad)
                        || !parent.entries.TryGetValue(source.Entity, out var current)
                        || !ReferenceEquals(current, entry)
                        || !client.AcknowledgesGeometry(source.Entity, entry, roadContext)) return false;
                    parent.ObserveCacheOwner(source.Entity);
                    known++;
                }
                // Node and ConnectedEdge, Deleted and Temp are not enableable
                // components in the supported game. Query membership is whole
                // chunk membership. A new/unknown entity prevents suppression.
                return known == native.Count;
            }
        }
    }
}
