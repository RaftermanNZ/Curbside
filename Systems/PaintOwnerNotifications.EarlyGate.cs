using System;
using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using SubLane = Game.Net.SubLane;
using ParkingLane = Game.Net.ParkingLane;
using PedestrianLane = Game.Net.PedestrianLane;

namespace MedianGrassReplacement.Systems
{
    internal sealed partial class PaintOwnerNotifications
    {
        // A certificate covers every ECS chunk read by the exact owner snapshot,
        // including raw access members that the paint projection may ignore.
        // It is tested before ChangedCore can acquire a shared SubLane view.
        private const int CertificateChunks = 64;
        private const int CertificateEntities = 1024;
        private const int CertificateCatalog = 32;
        private const int CertificateAbsent = 32;
        private const ulong CertificateAge = 128;
        private static readonly ComponentType[] CertificateTypes = {
            ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Node>(),
            ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Composition>(),
            ComponentType.ReadOnly<EdgeGeometry>(), ComponentType.ReadOnly<NetCompositionData>(),
            ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Curve>(),
            ComponentType.ReadOnly<EdgeLane>(), ComponentType.ReadOnly<ParkingLane>(),
            ComponentType.ReadOnly<PedestrianLane>(), ComponentType.ReadOnly<NetLaneData>(),
            ComponentType.ReadOnly<NetLaneGeometryData>(), ComponentType.ReadOnly<ParkingLaneData>(),
            ComponentType.ReadOnly<SubLane>(), ComponentType.ReadOnly<ConnectedEdge>(),
            ComponentType.ReadOnly<NetCompositionLane>(), ComponentType.ReadOnly<NetCompositionPiece>(),
            ComponentType.ReadOnly<NetGeometrySection>(),
            ComponentType.ReadOnly<Game.Net.CarLane>(), ComponentType.ReadOnly<Game.Net.Lane>()
        };

        private sealed class ChunkCertificate
        {
            internal Entity Representative;
            internal ulong Sequence;
            internal uint Order, ReadMask;
            internal readonly uint[] Versions = new uint[CertificateTypes.Length];
        }
        private sealed class InputCertificate
        {
            internal readonly List<ChunkCertificate> Chunks = new List<ChunkCertificate>();
            internal readonly List<Entity> Catalog = new List<Entity>();
            internal readonly List<Entity> Absent = new List<Entity>();
            internal readonly List<bool> NonPaint = new List<bool>();
            internal int ChunkCount, CatalogCount, AbsentCount;
            internal uint ReadMask;
            internal readonly List<OwnerProofInput> SemanticInputs = new List<OwnerProofInput>();
            internal readonly List<CertificateEntity> Tracked = new List<CertificateEntity>();
            internal bool Valid, Disabled;
            internal ulong Epoch, RetryEpoch, RenewEpoch;
            internal ExternalPaintOwnership External;
            internal Func<Entity, bool> Resolver;
        }

        // Collect references already visited by the original equality pass.
        // No second SubLane traversal or component-data read is needed to arm.
        private sealed class CertificateCapture
        {
            internal readonly List<Entity> Entities = new List<Entity>();
            internal readonly List<Entity> Catalog = new List<Entity>();
            internal readonly Dictionary<Entity, uint> Masks = new Dictionary<Entity, uint>();
            internal readonly List<OwnerProofInput> SemanticInputs = new List<OwnerProofInput>();
            internal uint ReadMask;
            internal bool Overflow;
            internal void Add(Entity entity, uint mask = 0)
            {
                if (entity == Entity.Null || Overflow) return;
                if (Masks.TryGetValue(entity, out uint previous)) { Masks[entity] = previous | mask; ReadMask |= mask; return; }
                if (Entities.Count == CertificateEntities) { Overflow = true; return; }
                Entities.Add(entity); Masks.Add(entity, mask); ReadMask |= mask;
            }
            internal void Core(Entity entity, Core state)
            {
                uint mask = 0;
                if (state.Edge.Present) mask |= Bit(ReadType.Edge);
                if (state.Node.Present) mask |= Bit(ReadType.Node);
                if (state.Prefab.Present) mask |= Bit(ReadType.Prefab);
                if (state.Composition.Present) mask |= Bit(ReadType.Composition);
                if (state.Geometry.Present) mask |= Bit(ReadType.EdgeGeometry);
                Add(entity, mask);
                Add(state.CompositionEntity, state.CompositionData.Present ? Bit(ReadType.CompositionData) : 0);
                if (state.Prefab.Present) Add(state.Prefab.Data.m_Prefab);
            }
            internal void Buffers(Entity owner, OwnerInput input)
            {
                SemanticInputs.Add(new OwnerProofInput { Owner=owner, Input=input });
                Add(owner, (input.Members != null ? Bit(ReadType.SubLane) : 0) | (input.Connections != null ? Bit(ReadType.ConnectedEdge) : 0));
                Add(input.State.CompositionEntity, (input.CompositionLanes != null ? Bit(ReadType.CompositionLane) : 0) | (input.CompositionPieces != null ? Bit(ReadType.CompositionPiece) : 0));
                if (input.State.Prefab.Present) Add(input.State.Prefab.Data.m_Prefab, input.Sections != null ? Bit(ReadType.GeometrySection) : 0);
            }
            internal void Lane(Entity entity, LaneInput lane)
            {
                uint mask = 0;
                if (lane.Prefab.Present) mask |= Bit(ReadType.Prefab);
                if (lane.Owner.Present) mask |= Bit(ReadType.Owner);
                if (lane.Curve.Present) mask |= Bit(ReadType.Curve);
                if (lane.EdgeLane.Present) mask |= Bit(ReadType.EdgeLane);
                if (lane.Car) mask |= Bit(ReadType.CarPaint);
                if (lane.Car && lane.Lane) mask |= Bit(ReadType.LaneRoute);
                if (!lane.AccessOnly && lane.Parking) mask |= Bit(ReadType.Parking);
                if (!lane.AccessOnly && lane.Pedestrian) mask |= Bit(ReadType.Pedestrian);
                Add(entity, mask);
                if (!lane.Prefab.Present || lane.Prefab.Data.m_Prefab == Entity.Null || Overflow) return;
                var prefab = lane.Prefab.Data.m_Prefab;
                Add(prefab, (lane.Data.Present ? Bit(ReadType.LaneData) : 0) | (lane.Geometry.Present ? Bit(ReadType.LaneGeometry) : 0) | (lane.ParkingData.Present ? Bit(ReadType.ParkingData) : 0));
                if (Catalog.Contains(prefab)) return;
                if (Catalog.Count == CertificateCatalog) { Overflow = true; return; }
                Catalog.Add(prefab);
            }
            internal void Clear() { Entities.Clear(); Catalog.Clear(); Masks.Clear(); SemanticInputs.Clear(); ReadMask = 0; Overflow = false; }
        }
        private readonly CertificateCapture certificateCaptureStorage = new CertificateCapture();
        private CertificateCapture activeCertificateCapture;

        // The private query supplies only a dependency handle; it is never
        // enumerated or registered on the owning system's Dependency. Each World
        // owns its query storage; Clear also disposes it while the World is live.
        private const int MaximumDependencyProfiles = 32;
        private enum ReadType { Edge, Node, Prefab, Composition, EdgeGeometry, CompositionData, Owner, Curve, EdgeLane, Parking, Pedestrian, LaneData, LaneGeometry, ParkingData, SubLane, ConnectedEdge, CompositionLane, CompositionPiece, GeometrySection, CarPaint, LaneRoute }
        private static uint Bit(ReadType type) => 1u << (int)type;
        private sealed class DependencyProfile
        {
            internal EntityQuery Query;
            internal ulong Epoch;
            internal bool Ready;
            internal int Probes;
        }
        private readonly Dictionary<uint, DependencyProfile> certificateProfiles = new Dictionary<uint, DependencyProfile>();
        private World certificateWorld;
        private readonly DynamicComponentTypeHandle[] certificateHandles = new DynamicComponentTypeHandle[CertificateTypes.Length];
        private readonly Dictionary<ulong, ChunkCertificate> certificateChunks = new Dictionary<ulong, ChunkCertificate>();
        private bool certificateBatch;
        private int certificateCaptureFailures;
        private ulong certificateEpoch;
        private int certificateEntityCount;

        private void BeginCertificateBatch()
        {
            certificateBatch = true;
            certificateCaptureFailures = 0;
            certificateEpoch++;
        }
        private void EndCertificateBatch()
        {
            certificateBatch = false;
            certificateChunks.Clear();
        }
        private void ClearCertificates()
        {
            if (certificateWorld != null && certificateWorld.IsCreated)
                foreach (var profile in certificateProfiles.Values) profile.Query.Dispose();
            certificateProfiles.Clear(); certificateWorld = null;
            Array.Clear(certificateHandles, 0, certificateHandles.Length);
            certificateEpoch = 0;
        }
        private bool CanCollectCertificate(InputCertificate previous)
            => certificateCaptureFailures < 2 || previous != null && certificateProfiles.TryGetValue(previous.ReadMask, out var profile) && profile.Epoch == certificateEpoch && profile.Ready;

        private bool CertificateReadsReady(EntityManager em, bool retry, uint mask)
        {
            if (mask == 0) return true;
            if (certificateWorld != null && certificateWorld != em.World) { return false; }
            if (!certificateProfiles.TryGetValue(mask, out var profile))
            {
                if (certificateProfiles.Count == MaximumDependencyProfiles) { return false; }
                int count = 0; for (int i = 0; i < CertificateTypes.Length; i++) if ((mask & (1u << i)) != 0) count++;
                var types = new ComponentType[count]; int next = 0;
                for (int i = 0; i < CertificateTypes.Length; i++) if ((mask & (1u << i)) != 0) types[next++] = CertificateTypes[i];
                profile = new DependencyProfile { Query = em.CreateEntityQuery(new EntityQueryDesc { Any = types }) };
                certificateProfiles.Add(mask, profile); certificateWorld = em.World;
            }
            if (profile.Epoch != certificateEpoch) { profile.Epoch = certificateEpoch; profile.Ready = false; profile.Probes = 0; }
            if (profile.Ready) return true;
            if (profile.Probes >= (retry ? 2 : 1)) { return false; }
            profile.Probes++;
            var pending = profile.Query.GetDependency();
            if (!pending.IsCompleted) { return false; }
            // Only already completed handles are completed here. Irrelevant
            // writers are excluded; pending writers of real inputs still fall back.
            pending.Complete();
            for (int i = 0; i < certificateHandles.Length; i++)
                if ((mask & (1u << i)) != 0) certificateHandles[i] = em.GetDynamicComponentTypeHandle(CertificateTypes[i]);
            profile.Ready = true; return true;
        }

        // A miss never acknowledges new semantic inputs before source selection.
        internal bool UnchangedBeforeSources(EntityManager em, ExternalPaintOwnership external, Entity owner, ref bool phaseStarted)
        {
            if (TrySharedProbe(em, external, owner, false, true, false, ref phaseStarted, out bool sharedSame)) return sharedSame;
            if (!OwnerInputEquality.Supported || !PaintInputReadScope.Supported
                || !entries.TryGetValue(owner, out var previous) || previous.RoadContext) return false;
            // Input discovery was previously wired only to the road emitter.
            // Before source selection, a complete per-source proof can reject
            // an unrelated owner even when a chunk certificate cannot. A miss
            // never acknowledges inputs or replaces the retained snapshot.
            if (SharedInputDiscovery && sharedInputs != null && sharedInputs.HasOwner(owner, previous))
            {
                if (!phaseStarted) { BeginBatch(false); phaseStarted = true; }
                ObserveCacheOwner(owner);
                if (sharedInputs.Unchanged(em, external, owner, previous)) return true;
            }
            if (previous.Certificate == null || !previous.Certificate.Valid) return false;
            if (!phaseStarted) { BeginBatch(false); phaseStarted = true; }
            ObserveCacheOwner(owner);
            var proof = previous.Certificate;
            if (!CertificateReadsReady(em, false, proof.ReadMask)) return false;
            if (CertificateSame(em, proof, external)) return true;
            // Source selection still sees every miss. The following exact
            // comparison may establish a fresh proof in this same phase.
            // Do not discard that validation for another 32 owner batches.
            proof.Valid = false;
            return false;
        }

        internal bool Changed(EntityManager em, ExternalPaintOwnership external, Entity owner, bool roadContext = false)
        {
            if (TrySharedChange(em, external, owner, roadContext, out bool sharedChanged)) return sharedChanged;
            ObserveCacheOwner(owner);
            // The same provenance covers node owners and road/end-point inputs.
            // The entry's context remains part of its identity; a road proof
            // cannot answer an owner-only notification, or the reverse.
            // Unknown runtime versions retain the released comparison.
            if (!OwnerInputEquality.Supported || !PaintInputReadScope.Supported)
                return ChangedCore(em, external, owner, roadContext);
            bool standalone = !certificateBatch;
            if (standalone) BeginCertificateBatch();
            try
            {
                entries.TryGetValue(owner, out var previous);
                bool sameContext = previous != null && previous.RoadContext == roadContext;
                if (sameContext && SharedInputDiscovery && batch != null && batch.ValuesEnabled
                    && sharedInputs != null && sharedInputs.HasOwner(owner, previous)
                    && sharedInputs.Unchanged(em, external, owner, previous)) return false;
                var savedProof = sameContext ? previous.Certificate : null;
                bool eligible = savedProof == null || (!savedProof.Disabled && certificateEpoch >= savedProof.RetryEpoch);
                if (sameContext && savedProof != null && savedProof.Valid
                    && eligible && CertificateReadsReady(em, false, savedProof.ReadMask))
                {
                    if (CertificateSame(em, savedProof, external))
                    {
                        if (sharedInputs != null && sharedInputs.NeedsAcknowledge(owner)) RegisterSharedInputs(em, external, owner, previous);
                        return false;
                    }
                    savedProof.Valid = false;
                    // A failed shortcut requires the exact comparison below.
                    // If that comparison proves all inputs unchanged, retain
                    // its read set now instead of repeating it for 32 batches.
                    // Persistent writes still require validation each callback.
                    eligible = true;
                }

                activeCertificateCapture = sameContext
                    && eligible && CanCollectCertificate(savedProof)
                    ? certificateCaptureStorage : null;
                activeCertificateCapture?.Clear();
                bool changed = ChangedCore(em, external, owner, roadContext);
                if (entries.TryGetValue(owner, out var current) && current.RoadContext == roadContext)
                {
                    // A changing owner gets no certificate setup work.
                    // Only retained snapshots proven unchanged are armed.
                    if (!changed && eligible && (current.Certificate == null || !current.Certificate.Disabled)
                        && activeCertificateCapture != null && CertificateReadsReady(em, true, activeCertificateCapture.ReadMask))
                    {
                        // The old read may have joined a previously busy writer
                        // without changing these inputs. Reuse that proof.
                        // The exact comparison has just validated this read set.
                        // It may differ from an old proof's ignored-access graph;
                        // do not inspect old type stamps under the new mask.
                        CaptureCertificate(em, owner, current, external);
                    }
                    else
                    {
                        if (!changed && activeCertificateCapture != null) certificateCaptureFailures++;
                        if (changed && current.Certificate != null) current.Certificate.Valid = false;
                    }
                }
                return changed;
            }
            finally
            {
                activeCertificateCapture = null; certificateCaptureStorage.Clear();
                if (standalone) EndCertificateBatch();
            }
        }

        private bool CertificateSame(EntityManager em, InputCertificate proof, ExternalPaintOwnership external)
        {
            if(!proof.Valid) { return false; }
            if(proof.External!=external) { return false; }
            if(proof.Resolver!=KnownNonPaintPrefab) { return false; }
            if(certificateEpoch>=proof.RenewEpoch) { return false; }
            for (int i = 0; i < proof.AbsentCount; i++)
                if (em.Exists(proof.Absent[i])) {  return false; }
            uint changedMask=0;
            for (int i = 0; i < proof.ChunkCount; i++)
            {
                var saved = proof.Chunks[i];
                if (!em.Exists(saved.Representative)) {  return false; }
                var chunk = em.GetStorageInfo(saved.Representative).Chunk;
                if (chunk.SequenceNumber != saved.Sequence || chunk.GetOrderVersion() != saved.Order)
                {  return false; }
                for (int t = 0; t < certificateHandles.Length; t++)
                    if ((saved.ReadMask & (1u << t)) != 0 && chunk.GetChangeVersion(ref certificateHandles[t]) != saved.Versions[t])
                    { changedMask |= 1u << t;  }
            }
            if(changedMask!=0)
            {
#if CURBSIDE_SEMANTIC_CERTIFICATE_REPAIR
                try { if(!RevalidateStampOnlyInputs(em,proof,changedMask))return false; }
                catch { return false; }
#else
                return false;
#endif
            }
            // Managed prefab registration can become ready without changing the
            // lane entity. Recheck the small, bounded prefab classification set.
            for (int i = 0; i < proof.CatalogCount; i++)
                if ((KnownNonPaintPrefab != null && KnownNonPaintPrefab(proof.Catalog[i])) != proof.NonPaint[i]) {  return false; }
            // Commit stamps only after semantic checks AND managed classification
            // passed. A mismatch or exception leaves the old proof untouched.
            if(changedMask!=0)
            {
                for(int i=0;i<proof.ChunkCount;i++)
                {
                    var saved=proof.Chunks[i];var chunk=em.GetStorageInfo(saved.Representative).Chunk;
                    for(int t=0;t<certificateHandles.Length;t++)
                        if((saved.ReadMask & changedMask & (1u<<t))!=0) saved.Versions[t]=chunk.GetChangeVersion(ref certificateHandles[t]);
                }
            }
            return true;
        }

        private bool AddCertificateEntity(EntityManager em, InputCertificate proof, Entity entity, uint mask)
        {
            if (entity == Entity.Null) return true;
            if (++certificateEntityCount > CertificateEntities) return false;
            if (!em.Exists(entity))
            {
                if (mask != 0 || proof.AbsentCount == CertificateAbsent) return false;
                if (proof.AbsentCount == proof.Absent.Count) proof.Absent.Add(entity);
                else proof.Absent[proof.AbsentCount] = entity;
                proof.AbsentCount++;
                return true;
            }
            var chunk = em.GetStorageInfo(entity).Chunk;
            if (!certificateChunks.TryGetValue(chunk.SequenceNumber, out var saved))
            {
                if (proof.ChunkCount == CertificateChunks) return false;
                if (proof.ChunkCount == proof.Chunks.Count) proof.Chunks.Add(new ChunkCertificate());
                saved = proof.Chunks[proof.ChunkCount++]; saved.ReadMask = 0;
                saved.Representative = entity; saved.Sequence = chunk.SequenceNumber; saved.Order = chunk.GetOrderVersion();
                certificateChunks.Add(chunk.SequenceNumber, saved);
            }
            uint added = mask & ~saved.ReadMask; saved.ReadMask |= mask;
            for (int t = 0; t < certificateHandles.Length; t++)
                if ((added & (1u << t)) != 0) saved.Versions[t] = chunk.GetChangeVersion(ref certificateHandles[t]);
            return true;
        }
        private void CaptureCertificate(EntityManager em, Entity owner, Entry entry, ExternalPaintOwnership external)
        {
            var proof = entry.Certificate ?? (entry.Certificate = new InputCertificate());
            proof.Valid = false; proof.ChunkCount = proof.CatalogCount = proof.AbsentCount = 0;
            proof.SemanticInputs.Clear(); proof.Tracked.Clear();
            certificateEntityCount = 0; certificateChunks.Clear();
            try
            {
                var capture = activeCertificateCapture;
                if (capture == null || capture.Overflow) {  return; }
                foreach (var entity in capture.Entities)
                {
                    uint mask=capture.Masks[entity];
                    if (!AddCertificateEntity(em, proof, entity, mask)) {  return; }
                    proof.Tracked.Add(new CertificateEntity { Entity=entity, Mask=mask });
                }
                proof.SemanticInputs.AddRange(capture.SemanticInputs);
                foreach (var prefab in capture.Catalog)
                {
                    bool nonPaint = KnownNonPaintPrefab != null && KnownNonPaintPrefab(prefab);
                    if (proof.CatalogCount == proof.Catalog.Count) { proof.Catalog.Add(prefab); proof.NonPaint.Add(nonPaint); }
                    else { proof.Catalog[proof.CatalogCount] = prefab; proof.NonPaint[proof.CatalogCount] = nonPaint; }
                    proof.CatalogCount++;
                }
                proof.ReadMask = capture.ReadMask;
                proof.External = external; proof.Resolver = KnownNonPaintPrefab;
                proof.Epoch = certificateEpoch; proof.RetryEpoch = 0; proof.Valid = true;
                // Spread bounded renewals across owners instead of making a
                // whole hovered cohort repeat full validation in one callback.
                proof.RenewEpoch = certificateEpoch + CertificateAge / 2
                    + (uint)owner.GetHashCode() % CertificateAge;
            }
            finally
            {
                // A graph beyond these bounds stays on the released path until
                // its semantic snapshot is replaced. No recurring failed fills.
                proof.Disabled = !proof.Valid;
                certificateChunks.Clear();
            }
        }
    }
}
