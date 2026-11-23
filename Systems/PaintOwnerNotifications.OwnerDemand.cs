using System;
using System.Collections.Generic;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal sealed partial class PaintOwnerNotifications
    {
        private sealed partial class SharedInputs
        {
            private sealed class ProofChunk
            {
                internal uint Mask;
                internal uint Order;
                internal readonly uint[] Versions = new uint[CertificateTypes.Length];
            }
            // Phase-local scalar metadata only. No native chunk, buffer or
            // lookup survives an access, mutation boundary, or notification batch.
            private readonly Dictionary<ulong, ProofChunk> proofChunks = new Dictionary<ulong, ProofChunk>();
            private readonly List<ProofChunk> proofChunkPool = new List<ProofChunk>();
            private int proofChunkCount;
            private ulong proofEpoch = 1;
            private SharedOwner requestedOwner;
            private readonly HashSet<Source> registeringSources = new HashSet<Source>();

            private void StartDemandPhase()
            {
                cohortSamplesComplete = false;
                comparisonPeer = null;
                proofChunks.Clear();
                proofChunkCount = 0; cohortObserved = cohortAvailable = false; demandRequests = 0;
                if (proofEpoch == ulong.MaxValue)
                {
                    foreach (var owner in owners.Values) owner.DemandEpoch = 0;
                    foreach (var source in sources.Values)
                    {
                        source.ProofEpoch = 0;
                        source.CheckedEpoch = 0;
                        foreach (var field in source.Fields.Values) field.ProofEpoch = 0;
                    }
                    proofEpoch = 1;
                }
                else proofEpoch++;
            }

            private void ReadProofLocation(EntityManager em, Source source, uint mask)
            {
                bool known = source.ProofEpoch == proofEpoch;
                if (known && (!source.ProofExists || (proofChunks[source.ProofSequence].Mask & mask) == mask)) return;
                if (!known) source.ProofExists = em.Exists(source.Entity);
                if (source.ProofExists)
                {
                    var chunk = em.GetStorageInfo(source.Entity).Chunk;
                    if (known && chunk.SequenceNumber != source.ProofSequence)
                        throw new InvalidOperationException("Owner proof changed during read phase");
                    source.ProofSequence = chunk.SequenceNumber;
                    source.ProofArchetype = chunk.Archetype;
                    if (!proofChunks.TryGetValue(source.ProofSequence, out var saved))
                    {
                        if (proofChunkCount == proofChunkPool.Count) proofChunkPool.Add(new ProofChunk());
                        saved = proofChunkPool[proofChunkCount++]; saved.Mask = 0;
                        saved.Order = chunk.GetOrderVersion();
                        proofChunks.Add(source.ProofSequence, saved);
                    }
                    source.ProofOrder = saved.Order;
                    uint missing = mask & ~saved.Mask;
                    for (int i = 0; i < CertificateTypes.Length; i++)
                        if ((missing & (1u << i)) != 0) saved.Versions[i] = chunk.GetChangeVersion(ref parent.certificateHandles[i]);
                    saved.Mask |= mask;
                }
                source.ProofEpoch = proofEpoch;
            }

            private uint ReadProofVersion(EntityManager em, Field field)
            {
                if (field.Mask == 0) return 0;
                var source = field.Source;
                var saved = proofChunks[source.ProofSequence];
                if ((saved.Mask & field.Mask) == 0)
                {
                    var chunk = em.GetStorageInfo(source.Entity).Chunk;
                    // A move after the location read is an unsupported boundary,
                    // never evidence that the retained value stayed unchanged.
                    if (chunk.SequenceNumber != source.ProofSequence || chunk.GetOrderVersion() != source.ProofOrder)
                        throw new InvalidOperationException("Owner proof changed during read phase");
                    saved.Versions[field.Key] = chunk.GetChangeVersion(ref parent.certificateHandles[field.Key]); saved.Mask |= field.Mask;
                }
                return saved.Versions[field.Key];
            }

            private void StampField(EntityManager em, Field field)
            {
                field.StampVersion = ReadProofVersion(em, field);
                field.StampSequence = field.Source.ProofSequence;
                field.StampArchetype = field.Source.ProofArchetype;
                field.StampOrder = field.Source.ProofOrder;
                field.Stamped = true;
            }

            private bool ObserveRequestedOwner(EntityManager em, SharedOwner owner)
            {
                // Readiness covers only this owner's complete dependency set.
                // An unrelated retained owner cannot add a wait or disable it.
                if (!parent.CertificateReadsReady(em, false, owner.RequiredMask)) return false;
                foreach (var source in owner.ReadSources)
                {
                    CheckRequestedSource(em, source, owner.RequiredMask);
                    if (owner.Dirty) return false;
                }
                return true;
            }

            private void CheckRequestedSource(EntityManager em, Source source, uint readyMask)
            {
                if (source.CheckedEpoch != proofEpoch)
                {
                    source.CheckedMask = 0; source.CheckedRoles = false;
                    source.CheckedEpoch = proofEpoch;
                }
                uint needed = source.Mask & readyMask & ~source.CheckedMask;
                if (needed == 0 && source.CheckedRoles) return;
                ReadProofLocation(em, source, source.Mask & readyMask);
                foreach (var field in source.Fields.Values)
                {
                    if (field.Mask == 0 ? source.CheckedRoles : (field.Mask & needed) == 0) continue;
                    if (field.ProofEpoch != proofEpoch)
                    {
                        bool same;
                        if (field.Managed) same = field.CurrentSame(em, this);
                        else
                        {
                            same = source.ProofExists && field.Stamped
                                && (field.Mask == 0
                                    ? field.StampArchetype.Equals(source.ProofArchetype)
                                    : field.StampSequence == source.ProofSequence
                                        && field.StampArchetype.Equals(source.ProofArchetype)
                                        && field.StampVersion == ReadProofVersion(em, field));
                            if (!same)
                            {
                                same = field.CurrentSame(em, this);
                                if (same && source.ProofExists) StampField(em, field);
                            }
                        }
                        field.ProofSame = same;
                        field.ProofEpoch = proofEpoch;
                        if (!same) field.DirtyOwners();
                    }
                }
                source.CheckedMask |= needed; source.CheckedRoles = true;
            }
        }
    }
}
