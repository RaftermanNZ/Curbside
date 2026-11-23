using System;using System.Collections.Generic;using Unity.Entities;
namespace MedianGrassReplacement.Systems { internal sealed partial class PaintOwnerNotifications { private sealed partial class SharedInputs {
            private readonly Dictionary<ulong, Chunk> chunks = new Dictionary<ulong, Chunk>();
            private readonly HashSet<Source> absent = new HashSet<Source>(), dirtySources = new HashSet<Source>();
            private readonly List<Field> managed = new List<Field>();
            private readonly HashSet<ulong> unusedChunks = new HashSet<ulong>(), pendingArms = new HashSet<ulong>();
            private readonly Dictionary<ulong, uint> armingMasks = new Dictionary<ulong, uint>();
            private readonly Dictionary<ulong, Entity> armingSamples = new Dictionary<ulong, Entity>();
            private bool remap, cohortObserved, cohortAvailable, cohortSamplesComplete;
            private SharedInputs comparisonPeer;
            private int demandRequests;
            internal bool PreferCohorts, PreferLocalCohort;
            // A sparse request must not sweep a much larger retained graph.
            // Both proof routes have the same conservative fallback; this only
            // selects their scope. Keep dense/small graphs on a bounded pass.
            internal int CohortThreshold => Math.Max(4, Math.Min(256, owners.Count / 4));

            private bool DemandOrCohort(EntityManager em, ExternalPaintOwnership foreign)
            {
                if (world == null || !ReferenceEquals(world, em.World) || external != foreign
                    || resolver != parent.KnownNonPaintPrefab || requestedOwner == null) return false;
                // Use demand proofs for small local cohorts. A wide notification
                // uses one chunk pass instead of repeating per-owner metadata.
                if (requestedOwner.DemandEpoch != proofEpoch)
                {
                    requestedOwner.DemandEpoch = proofEpoch;
                    demandRequests++;
                }
                // Without a collector cardinality hint retain the original
                // early dense crossover. A known sparse cohort can stay local.
                int limit = PreferLocalCohort ? CohortThreshold : Math.Max(4, Math.Min(32, owners.Count / 32));
                if (PreferCohorts || demandRequests > limit)
                {
                    if (!cohortObserved)
                    {
                        cohortObserved = true;
                        cohortAvailable = ObserveCohorts(em, foreign);
                    }
                    if (cohortAvailable) return !requestedOwner.Dirty;
                }
                // A globally busy unrelated type must not defeat a ready local
                // owner. Pending real dependencies still reject its own proof.
                return ObserveRequestedOwner(em, requestedOwner);
            }
            private void FlushArms()
            {
                if (pendingArms.Count != 0) cohortSamplesComplete = false;
                foreach (var sequence in pendingArms)
                    if (chunks.TryGetValue(sequence, out var chunk)) ArmConsistentChunk(sequence, chunk);
                pendingArms.Clear();
            }
            private void ForgetCohortSource(Source source)
            {
                dirtySources.Remove(source); absent.Remove(source);
                if (source.Linked && chunks.TryGetValue(source.Sequence, out var chunk))
                {
                    chunk.Sources.Remove(source);
                    if (chunk.Sources.Count == 0) { chunks.Remove(source.Sequence); pendingArms.Remove(source.Sequence); }
                    else
                    {
                        chunk.Mask = 0; chunk.Rebound = false;
                        foreach (var remaining in chunk.Sources) { chunk.Mask |= remaining.Mask; chunk.Rebound |= remaining.Rebound; }
                    }
                }
                source.Linked = false;
            }
            private void ClearCohorts()
            {
                chunks.Clear(); absent.Clear(); dirtySources.Clear(); managed.Clear();
                unusedChunks.Clear(); pendingArms.Clear(); armingMasks.Clear(); armingSamples.Clear();
                remap = cohortObserved = cohortAvailable = PreferCohorts = PreferLocalCohort = false; demandRequests = 0;
            }
            private sealed class Chunk
            {
                internal readonly HashSet<Source> Sources = new HashSet<Source>();
                internal uint Mask, ArmedMask, Order;
                internal uint CurrentOrder;
                internal readonly uint[] Versions = new uint[CertificateTypes.Length];
                internal readonly uint[] Current = new uint[CertificateTypes.Length];
                internal bool Armed, Rebound;
            }
            private static void ArmConsistentChunk(ulong sequence, Chunk chunk)
            {
                bool orderKnown = true, haveOrder = false; uint order = 0;
                uint haveVersions = 0, conflictingVersions = 0;
                // ArmCapturedInputs has already copied every contributor's
                // sampled version into its field. Current is now reusable
                // scratch: gather agreement once rather than walking all fields
                // again for every component type. Every masked field uses its
                // ReadType as its key and has exactly that single type bit.
                foreach (var source in chunk.Sources)
                {
                    foreach (var field in source.Fields.Values)
                    {
                        if (field.Managed) continue;
                        if (!field.Stamped || field.StampSequence != sequence)
                        {
                            orderKnown = false;
                            conflictingVersions |= field.Mask;
                            continue;
                        }
                        if (!haveOrder) { order = field.StampOrder; haveOrder = true; }
                        else if (order != field.StampOrder) orderKnown = false;
                        uint bit = field.Mask;
                        if (bit == 0) continue;
                        int type = field.Key;
                        if ((haveVersions & bit) == 0)
                        {
                            chunk.Current[type] = field.StampVersion;
                            haveVersions |= bit;
                        }
                        else if (chunk.Current[type] != field.StampVersion) conflictingVersions |= bit;
                    }
                }
                if (orderKnown && haveOrder)
                {
                    chunk.Order = order; chunk.Armed = true;
                    // Cohort agreement, not iteration order, authorizes clearing
                    // rebound provenance. Conflicting contributors retain it.
                    foreach (var source in chunk.Sources) source.Rebound = false;
                }
                uint consistent = chunk.Mask & haveVersions & ~conflictingVersions;
                for (int i = 0; i < CertificateTypes.Length; i++)
                {
                    uint bit = 1u << i;
                    if ((consistent & bit) != 0) { chunk.Versions[i] = chunk.Current[i]; chunk.ArmedMask |= bit; }
                }
                chunk.Rebound = false; foreach (var source in chunk.Sources) chunk.Rebound |= source.Rebound;
            }
            private void Remap(EntityManager em)
            {
                cohortSamplesComplete = false;
                unusedChunks.Clear();
                armingMasks.Clear(); armingSamples.Clear();
                foreach (var source in dirtySources)
                {
                    ulong previousSequence=source.Sequence;
                    if (source.Linked) pendingArms.Add(previousSequence);
                    bool previousRebound=source.Rebound;
                    Chunk previous=null;
                    bool wasLinked=source.Linked && chunks.TryGetValue(previousSequence,out previous);
                    bool live=source.Fields.Count!=0 && sources.TryGetValue(source.Entity,out var retained) && retained==source;
                    bool exists=live && em.Exists(source.Entity);
                    absent.Remove(source);
                    if(live)
                    {
                        source.Rebound |= !source.Bound || source.Exists!=exists;
                        source.Exists=exists;
                        if(exists)
                        {
                            var storage=em.GetStorageInfo(source.Entity);
                            source.Rebound |= source.Sequence!=storage.Chunk.SequenceNumber && source.ValidatedSequence!=storage.Chunk.SequenceNumber
                                || !source.Archetype.Equals(storage.Chunk.Archetype);
                            source.Sequence=storage.Chunk.SequenceNumber;
                            if(!source.Bound)source.Archetype=storage.Chunk.Archetype;
                        }
                        else absent.Add(source);
                        source.Bound=true;
                    }
                    bool stays=wasLinked && exists && previousSequence==source.Sequence;
                    if(wasLinked && !stays)
                    {
                        previous.Sources.Remove(source);
                        unusedChunks.Add(previousSequence);
                    }
                    source.Linked=false;
                    if(!exists)continue;
                    if(!chunks.TryGetValue(source.Sequence,out var chunk))
                    {chunk=new Chunk();chunks.Add(source.Sequence,chunk);}
                    if(!stays)
                    {
                        chunk.Sources.Add(source);
                        unusedChunks.Add(source.Sequence);
                    }
                    else if(source.LinkedMask!=source.Mask || previousRebound!=source.Rebound || source.Rebound && !chunk.Rebound)
                        unusedChunks.Add(source.Sequence);
                    pendingArms.Add(source.Sequence);
                    source.Linked=true;
                    source.LinkedMask=source.Mask;
                }
                // Recompute only cohorts whose membership or aggregate inputs
                // changed. Stable re-registration preserves links and stamps.
                foreach(var sequence in unusedChunks)
                {
                    if(!chunks.TryGetValue(sequence,out var chunk))continue;
                    if(chunk.Sources.Count==0){chunks.Remove(sequence);continue;}
                    chunk.Mask=0;chunk.Rebound=false;
                    foreach(var source in chunk.Sources){chunk.Mask|=source.Mask;chunk.Rebound|=source.Rebound;}
                }
                unusedChunks.Clear();dirtySources.Clear();remap=false;
            }
            private void CheckSource(EntityManager em, Source source, uint mask, bool structural, bool force = false)
            {
                bool schemaChanged = false;
                var currentArchetype = source.Archetype;
                ulong currentSequence = source.Sequence;
                if (structural)
                {
                    bool exists = em.Exists(source.Entity);
                    schemaChanged = source.Exists != exists;
                    if (exists)
                    {
                        var storage = em.GetStorageInfo(source.Entity);
                        currentArchetype = storage.Chunk.Archetype; currentSequence = storage.Chunk.SequenceNumber;
                        schemaChanged |= !source.Archetype.Equals(currentArchetype);
                        if (source.Sequence != currentSequence) { mask = uint.MaxValue; dirtySources.Add(source); remap = true; }
                    }
                    else if (source.Exists) { schemaChanged = true; dirtySources.Add(source); remap = true; }
                }
                foreach (var field in source.Fields.Values)
                    if (!field.Managed && (force || schemaChanged || (field.Mask & mask) != 0)
                        && !field.CurrentSame(em, this)) field.DirtyOwners();
                if (structural || force)
                {
                    source.Archetype = currentArchetype; source.ValidatedSequence = currentSequence; source.Rebound = false;
                }
            }
            private bool ObserveCohorts(EntityManager em, ExternalPaintOwnership foreign)
            {
                if (world == null || !ReferenceEquals(world, em.World) || external != foreign || resolver != parent.KnownNonPaintPrefab) return false;
                if (remap || dirtySources.Count != 0) Remap(em);
                FlushArms();
                uint mask = 0; foreach (var chunk in chunks.Values) mask |= chunk.Mask;
                // Pending real inputs retain the established path. No new wait,
                // partial watermark advance, or borrowed busy view is permitted.
                if (!parent.CertificateReadsReady(em, false, mask)) return false;
                var peerInputs = CurrentCohortPeer();
                comparisonPeer = peerInputs;
                try
                {
                    foreach (var field in managed) if (!field.CurrentSame(em, this)) field.DirtyOwners();
                    foreach (var source in absent) CheckSource(em, source, uint.MaxValue, true, true);
                    bool samplesComplete = peerInputs == null ? ObserveNativeChunks(em) : ObservePeerChunks(em, peerInputs);
                    // Rebuild grouping only after a structural move, never while
                    // enumerating chunks. New groups are conservatively unarmed.
                    if (remap) { Remap(em); samplesComplete = false; }
                    cohortSamplesComplete = samplesComplete;
                    return true;
                }
                finally { comparisonPeer = null; }
            }
            private bool ObserveNativeChunks(EntityManager em)
            {
                bool samplesComplete = true;
                foreach (var pair in chunks)
                {
                    var saved = pair.Value; var sample = Entity.Null;
                    foreach (var source in saved.Sources) { sample = source.Entity; break; }
                    bool exists = em.Exists(sample);
                    var chunk = exists ? em.GetStorageInfo(sample).Chunk : default;
                    if (!exists || chunk.SequenceNumber != pair.Key)
                    {
                        foreach (var source in saved.Sources) CheckSource(em, source, uint.MaxValue, true, true);
                        samplesComplete = false; remap = true; continue;
                    }
                    uint order = chunk.GetOrderVersion(), changed = 0;
                    for (int i = 0; i < CertificateTypes.Length; i++)
                    {
                        uint bit = 1u << i; if ((saved.Mask & bit) == 0) continue;
                        uint version = chunk.GetChangeVersion(ref parent.certificateHandles[i]); saved.Current[i] = version;
                        if (!saved.Armed || (saved.ArmedMask & bit) == 0 || version != saved.Versions[i]) changed |= bit;
                    }
                    bool structural = !saved.Armed || order != saved.Order;
                    if (structural || saved.Rebound || changed != 0)
                        foreach (var source in saved.Sources) CheckSource(em, source, changed, structural || source.Rebound, !saved.Armed || source.Rebound);
                    for (int i = 0; i < CertificateTypes.Length; i++) if ((saved.Mask & (1u << i)) != 0) saved.Versions[i] = saved.Current[i];
                    saved.Order = order; saved.ArmedMask = saved.Mask; saved.Armed = true; saved.Rebound = false;
                }
                return samplesComplete;
            }
            private bool ObservePeerChunks(EntityManager em, SharedInputs peerInputs)
            {
                bool samplesComplete = true;
                foreach (var pair in chunks)
                {
                    var saved = pair.Value; var sample = Entity.Null;
                    foreach (var source in saved.Sources) { sample = source.Entity; break; }
                    bool exists = em.Exists(sample);
                    var chunk = exists ? em.GetStorageInfo(sample).Chunk : default;
                    if (!exists || chunk.SequenceNumber != pair.Key)
                    {
                        foreach (var source in saved.Sources) CheckSource(em, source, uint.MaxValue, true, true);
                        samplesComplete = false; remap = true; continue;
                    }
                    var peer = peerInputs != null ? SampledPeerChunk(peerInputs, pair.Key, saved.Mask) : null;
                    uint order = peer != null ? peer.Order : chunk.GetOrderVersion(), changed = 0;
                    if (peer == null)
                    {
                        for (int i = 0; i < CertificateTypes.Length; i++)
                        {
                            uint bit = 1u << i; if ((saved.Mask & bit) == 0) continue;
                            uint version = chunk.GetChangeVersion(ref parent.certificateHandles[i]); saved.Current[i] = version;
                            if (!saved.Armed || (saved.ArmedMask & bit) == 0 || version != saved.Versions[i]) changed |= bit;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < CertificateTypes.Length; i++)
                        {
                            uint bit = 1u << i; if ((saved.Mask & bit) == 0) continue;
                            uint version = peer.Versions[i]; saved.Current[i] = version;
                            if (!saved.Armed || (saved.ArmedMask & bit) == 0 || version != saved.Versions[i]) changed |= bit;
                        }
                    }
                    bool structural = !saved.Armed || order != saved.Order;
                    if (structural || saved.Rebound || changed != 0)
                        foreach (var source in saved.Sources) CheckSource(em, source, changed, structural || source.Rebound, !saved.Armed || source.Rebound);
                    for (int i = 0; i < CertificateTypes.Length; i++) if ((saved.Mask & (1u << i)) != 0) saved.Versions[i] = saved.Current[i];
                    saved.Order = order; saved.ArmedMask = saved.Mask; saved.Armed = true; saved.Rebound = false;
                }
                return samplesComplete;
            }
            // The authority's two contexts have distinct expected values and
            // acknowledgements. Share only sampled scalar native metadata while
            // both are in the same non-mutating batch. Begin/End and a readonly
            // disable reset cohortAvailable, so no sample crosses those bounds.
            private SharedInputs CurrentCohortPeer()
            {
                var peer = parent.cohortPeer?.sharedInputs;
                return peer != null && peer.cohortObserved && peer.cohortAvailable && peer.cohortSamplesComplete
                    && ReferenceEquals(world, peer.world) && external == peer.external
                    && resolver == peer.resolver ? peer : null;
            }
            private static Chunk SampledPeerChunk(SharedInputs peer, ulong sequence, uint mask)
            {
                if (peer == null || !peer.chunks.TryGetValue(sequence, out var chunk)
                    || !chunk.Armed || (mask & ~chunk.ArmedMask) != 0)
                    return null;
                return chunk;
            }
            private Chunk CurrentPeerChunk(ulong sequence, uint mask)
                => SampledPeerChunk(CurrentCohortPeer(), sequence, mask);
}}}
