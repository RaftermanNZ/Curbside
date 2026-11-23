using System;
using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    internal sealed partial class PaintOwnerNotifications
    {
        // Experimental input discovery. This index is derived from retained
        // comparisons, never serialized. Its notification phase is read-only;
        // only scalar chunk stamps and immutable value copies survive EndBatch.
        private SharedInputs sharedInputs;
        internal bool SharedInputDiscovery;

        internal void PrepareInputCohort(int count, ref bool phaseStarted)
        {
            if (sharedClient != null)
            {
                if (!phaseStarted) { BeginBatch(); phaseStarted = true; }
                if (sharedBatch) { sharedClient.Prepare(count); return; }
            }
            if (count <= 32 || !SharedInputDiscovery || sharedInputs == null
                || !OwnerInputEquality.Supported || !PaintInputReadScope.Supported) return;
            if (!phaseStarted) { BeginBatch(); phaseStarted = true; }
            if (count > sharedInputs.CohortThreshold) sharedInputs.PreferCohorts = true;
            else sharedInputs.PreferLocalCohort = true;
        }

        private void RegisterSharedInputs(EntityManager em, ExternalPaintOwnership external, Entity owner, Entry entry)
        {
            if (!SharedInputDiscovery || !OwnerInputEquality.Supported || !PaintInputReadScope.Supported) return;
            if (sharedInputs == null) sharedInputs = new SharedInputs(this);
            sharedInputs.Register(em, external, owner, entry);
        }

        internal bool UnchangedForInputNotification(EntityManager em, ExternalPaintOwnership external,
            Entity owner, bool roadContext, ref bool phaseStarted)
        {
            if (TrySharedProbe(em, external, owner, roadContext, false, false, ref phaseStarted, out bool sharedSame)) return sharedSame;
            if (!SharedInputDiscovery || sharedInputs == null || !OwnerInputEquality.Supported || !PaintInputReadScope.Supported
                || !entries.TryGetValue(owner, out var entry) || entry.RoadContext != roadContext
                || !sharedInputs.HasOwner(owner, entry)) return false;
            if (!phaseStarted) { BeginBatch(); phaseStarted = true; }
            if (batch == null || !batch.ValuesEnabled) return false;
            ObserveCacheOwner(owner);
            return sharedInputs.Unchanged(em, external, owner, entry);
        }

        internal bool UnchangedForCollectedInput(EntityManager em, ExternalPaintOwnership external,
            Entity owner, bool roadContext, ref bool phaseStarted)
        {
            if (TrySharedProbe(em, external, owner, roadContext, false, true, ref phaseStarted, out bool sharedSame)) return sharedSame;
            if (phaseStarted && batch != null && batch.ValuesEnabled && SharedInputDiscovery
                && sharedInputs != null && OwnerInputEquality.Supported && PaintInputReadScope.Supported
                && entries.TryGetValue(owner, out var entry) && entry.RoadContext == roadContext
                && sharedInputs.TryCollectedProof(em, external, owner, entry, out bool unchanged))
            {
                ObserveCacheOwner(owner);
                return unchanged;
            }
            // Parking/overlap owners deliberately do not enter the shared
            // graph. Their existing authoritative certificate can still prove
            // an unchanged notification. Reuse that proof without acknowledging
            // new inputs; busy, expired, unknown and changed inputs fall through.
            if (OwnerInputEquality.Supported && PaintInputReadScope.Supported
                && entries.TryGetValue(owner, out var captured) && captured.RoadContext == roadContext
                && captured.Certificate != null && captured.Certificate.Valid)
            {
                if (!phaseStarted) { BeginBatch(false); phaseStarted = true; }
                ObserveCacheOwner(owner);
                var proof = captured.Certificate;
                if (CertificateReadsReady(em, false, proof.ReadMask))
                {
                    if (CertificateSame(em, proof, external)) return true;
                    proof.Valid = false;
                }
            }
            return UnchangedForInputNotification(em, external, owner, roadContext, ref phaseStarted);
        }

        private sealed partial class SharedInputs
        {
            // Bounds are for this optional index, not the authoritative cache.
            // Unsupported/over-budget owners keep the established comparison.
            private const int MaximumSources = 32768, MaximumFields = 262144;
            private readonly PaintOwnerNotifications parent;
            private readonly Dictionary<Entity, SharedOwner> owners = new Dictionary<Entity, SharedOwner>();
            private readonly Dictionary<Entity, Source> sources = new Dictionary<Entity, Source>();
            private readonly HashSet<Field> previousBindings = new HashSet<Field>();
            private readonly HashSet<Field> registering = new HashSet<Field>();
            private readonly Dictionary<Entity, LanePrefabInput> registeringPrefabs = new Dictionary<Entity, LanePrefabInput>();
            private readonly Dictionary<Type, Field> observations = new Dictionary<Type, Field>();
            private object world;
            private ExternalPaintOwnership external;
            private Func<Entity, bool> resolver;
            private int fieldCount;

            private sealed class SharedOwner
            {
                internal Entity Entity;
                internal Entry Entry;
                internal bool Complete, Dirty, RetryBlocked;
                internal uint RequiredMask;
                internal ulong DemandEpoch;
                internal readonly List<Field> Fields = new List<Field>();
                internal readonly List<Source> ReadSources = new List<Source>();
            }
            private sealed class Source
            {
                internal Entity Entity;
                internal bool Exists, Bound, Rebound, Linked;
                internal ulong Sequence, ValidatedSequence;
                internal EntityArchetype Archetype;
                internal uint LinkedMask;
                internal uint Mask;
                internal ulong ProofEpoch, ProofSequence;
                internal uint ProofOrder;
                internal bool ProofExists;
                internal EntityArchetype ProofArchetype;
                internal ulong CheckedEpoch;
                internal uint CheckedMask;
                internal bool CheckedRoles;
                internal readonly Dictionary<int, Field> Fields = new Dictionary<int, Field>();
            }
            private abstract class Field
            {
                internal Source Source;
                internal int Key;
                internal uint Mask;
                internal bool Managed;
                internal bool Stamped;
                internal ulong ProofEpoch;
                internal bool ProofSame;
                internal ulong StampSequence;
                internal EntityArchetype StampArchetype;
                internal uint StampOrder, StampVersion;
                // Incoming observations are temporary when this field already
                // exists. Allocate reverse links only for retained fields.
                internal List<SharedOwner> Owners;
                internal abstract bool ExpectedSame(Field other);
                internal abstract void Replace(Field other);
                internal abstract bool CurrentSame(EntityManager em, SharedInputs index);
                internal void DirtyOwners() { foreach (var owner in Owners) owner.Dirty = true; }
            }
            private sealed class ComponentField<T> : Field where T : unmanaged, IComponentData
            {
                internal static readonly Func<Field> Create = () => new ComponentField<T>();
                internal Value<T> Expected;
                internal override bool ExpectedSame(Field other) => Expected.Same(((ComponentField<T>)other).Expected);
                internal override void Replace(Field other) { Expected = ((ComponentField<T>)other).Expected; Mask = other.Mask; }
                internal override bool CurrentSame(EntityManager em, SharedInputs index)
                {
                    var peer = index.comparisonPeer;
                    if (peer != null && peer.sources.TryGetValue(Source.Entity, out var source)
                        && source.Fields.TryGetValue(Key, out var field)
                        && field is ComponentField<T> sampled && sampled.ProofEpoch == peer.proofEpoch
                        && Expected.Same(sampled.Expected))
                    {
                        ProofSame = sampled.ProofSame; ProofEpoch = index.proofEpoch;
                        return ProofSame;
                    }
                    bool present = em.Exists(Source.Entity) && Access(em, index.parent.batch).HasComponent<T>(Source.Entity);
                    bool same = present == Expected.Present && (!present
                        || Expected.Same(Value<T>.ReadKnown(em, Source.Entity, true, index.parent.batch)));
                    ProofSame = same; ProofEpoch = index.proofEpoch;
                    return same;
                }
            }
            private sealed class PresenceField<T> : Field where T : unmanaged, IComponentData
            {
                internal static readonly Func<Field> Create = () => new PresenceField<T>();
                internal bool Expected;
                internal override bool ExpectedSame(Field other) => Expected == ((PresenceField<T>)other).Expected;
                internal override void Replace(Field other) { Expected = ((PresenceField<T>)other).Expected; }
                internal override bool CurrentSame(EntityManager em, SharedInputs index)
                    => Expected == (em.Exists(Source.Entity) && Access(em, index.parent.batch).HasComponent<T>(Source.Entity));
            }
            private sealed class CoreRolesField : Field
            {
                internal static readonly Func<Field> Create = () => new CoreRolesField();
                internal bool Deleted, Temporary, Lights, Road;
                internal override bool ExpectedSame(Field other)
                { var v = (CoreRolesField)other; return Deleted == v.Deleted && Temporary == v.Temporary && Lights == v.Lights && Road == v.Road; }
                internal override void Replace(Field other)
                { var v = (CoreRolesField)other; Deleted = v.Deleted; Temporary = v.Temporary; Lights = v.Lights; Road = v.Road; }
                internal override bool CurrentSame(EntityManager em, SharedInputs index)
                {
                    if (!em.Exists(Source.Entity)) return false;
                    var read = Access(em, index.parent.batch);
                    return Deleted == read.HasComponent<Game.Common.Deleted>(Source.Entity)
                        && Temporary == read.HasComponent<Game.Tools.Temp>(Source.Entity)
                        && Lights == read.HasComponent<TrafficLights>(Source.Entity) && Road == read.HasComponent<Game.Net.Road>(Source.Entity);
                }
            }
            private sealed class LaneRolesField : Field
            {
                internal static readonly Func<Field> Create = () => new LaneRolesField();
                internal PaintLaneRoles Expected;
                internal bool External;
                private static bool Same(PaintLaneRoles a, PaintLaneRoles b)
                    => a.Deleted == b.Deleted && a.Temporary == b.Temporary && a.Generated == b.Generated
                    && a.Lane == b.Lane && a.Car == b.Car && a.Track == b.Track && a.Parking == b.Parking
                    && a.Pedestrian == b.Pedestrian && a.PaintGeometry == b.PaintGeometry && a.HiddenJunction == b.HiddenJunction
                    && (!a.Car || a.Master == b.Master);
                internal override bool ExpectedSame(Field other)
                { var v = (LaneRolesField)other; return External == v.External && Same(Expected, v.Expected); }
                internal override void Replace(Field other)
                { var v = (LaneRolesField)other; Expected = v.Expected; External = v.External; }
                internal override bool CurrentSame(EntityManager em, SharedInputs index)
                {
                    if (!em.Exists(Source.Entity)) return false;
                    // ExternalPaintOwnership is an immutable archetype-name
                    // predicate in production. Role/structure checks cover it;
                    // it does not need a poll for every lane every callback.
                    return Same(Expected, index.parent.batch.Roles.Read(em, Source.Entity))
                        && External == index.external.Contains(em, Source.Entity);
                }
            }
            private sealed class ExistenceField : Field
            {
                internal static readonly Func<Field> Create = () => new ExistenceField();
                internal bool Expected;
                internal override bool ExpectedSame(Field other) => Expected == ((ExistenceField)other).Expected;
                internal override void Replace(Field other) { Expected = ((ExistenceField)other).Expected; }
                internal override bool CurrentSame(EntityManager em, SharedInputs index) => em.Exists(Source.Entity) == Expected;
            }
            private sealed class BufferField<T> : Field where T : unmanaged, IBufferElementData
            {
                internal static readonly Func<Field> Create = () => new BufferField<T>();
                internal T[] Expected;
                internal override bool ExpectedSame(Field other)
                {
                    var values = ((BufferField<T>)other).Expected;
                    if (Expected == null || values == null) return Expected == values;
                    if (Expected.Length != values.Length) return false;
                    for (int i = 0; i < values.Length; i++) if (!OwnerInputEquality<T>.Same(Expected[i], values[i])) return false;
                    return true;
                }
                internal override void Replace(Field other) { Expected = ((BufferField<T>)other).Expected; Mask = other.Mask; }
                internal override bool CurrentSame(EntityManager em, SharedInputs index)
                    => BufferSame(em, Source.Entity, Expected, index.parent.batch);
            }
            private sealed class CarField : Field
            {
                internal static readonly Func<Field> Create = () => new CarField();
                internal Game.Net.CarLaneFlags Expected;
                private const CarLaneFlags Relevant = CarLaneFlags.Twoway | CarLaneFlags.LeftLimit | CarLaneFlags.RightLimit | CarLaneFlags.Forbidden;
                internal override bool ExpectedSame(Field other) => Expected == ((CarField)other).Expected;
                internal override void Replace(Field other) { Expected = ((CarField)other).Expected; }
                internal override bool CurrentSame(EntityManager em, SharedInputs index)
                    => em.Exists(Source.Entity) && Access(em, index.parent.batch).HasComponent<Game.Net.CarLane>(Source.Entity)
                    && (Access(em, index.parent.batch).GetComponentData<Game.Net.CarLane>(Source.Entity).m_Flags & Relevant) == Expected;
            }
            private sealed class RouteField : Field
            {
                internal static readonly Func<Field> Create = () => new RouteField();
                internal int Start, End;
                internal override bool ExpectedSame(Field other) => Start == ((RouteField)other).Start && End == ((RouteField)other).End;
                internal override void Replace(Field other) { Start = ((RouteField)other).Start; End = ((RouteField)other).End; }
                internal override bool CurrentSame(EntityManager em, SharedInputs index)
                {
                    if (!em.Exists(Source.Entity) || !Access(em, index.parent.batch).HasComponent<Game.Net.Lane>(Source.Entity)) return false;
                    var lane = Access(em, index.parent.batch).GetComponentData<Game.Net.Lane>(Source.Entity);
                    return Start == lane.m_StartNode.GetOwnerIndex() && End == lane.m_EndNode.GetOwnerIndex();
                }
            }
            private sealed class ParkingField : Field
            {
                internal static readonly Func<Field> Create = () => new ParkingField();
                internal ParkingLaneFlags Expected;
                internal override bool ExpectedSame(Field other) => Expected == ((ParkingField)other).Expected;
                internal override void Replace(Field other) { Expected = ((ParkingField)other).Expected; }
                internal override bool CurrentSame(EntityManager em, SharedInputs index)
                    => em.Exists(Source.Entity) && Access(em, index.parent.batch).HasComponent<Game.Net.ParkingLane>(Source.Entity)
                    && Access(em, index.parent.batch).GetComponentData<Game.Net.ParkingLane>(Source.Entity).m_Flags == Expected;
            }
            private sealed class PedestrianField : Field
            {
                internal static readonly Func<Field> Create = () => new PedestrianField();
                internal PedestrianLaneFlags Expected;
                internal override bool ExpectedSame(Field other) => Expected == ((PedestrianField)other).Expected;
                internal override void Replace(Field other) { Expected = ((PedestrianField)other).Expected; }
                internal override bool CurrentSame(EntityManager em, SharedInputs index)
                    => em.Exists(Source.Entity) && Access(em, index.parent.batch).HasComponent<Game.Net.PedestrianLane>(Source.Entity)
                    && Access(em, index.parent.batch).GetComponentData<Game.Net.PedestrianLane>(Source.Entity).m_Flags == Expected;
            }
            private sealed class PrefabClassificationField : Field
            {
                internal static readonly Func<Field> Create = () => new PrefabClassificationField();
                internal bool Expected;
                internal override bool ExpectedSame(Field other) => Expected == ((PrefabClassificationField)other).Expected;
                internal override void Replace(Field other) { Expected = ((PrefabClassificationField)other).Expected; }
                internal override bool CurrentSame(EntityManager em, SharedInputs index)
                    => (index.parent.KnownNonPaintPrefab != null && index.parent.KnownNonPaintPrefab(Source.Entity)) == Expected;
            }

            internal SharedInputs(PaintOwnerNotifications value) { parent = value; }
            internal void Begin() { PreferCohorts = PreferLocalCohort = false; StartDemandPhase(); observations.Clear(); }
            // A real in-phase mutation invalidates the observation, not the
            // retained expected fields. The next proof rechecks current data.
            internal void InvalidateObservation() { StartDemandPhase(); }
            internal void End() { StartDemandPhase(); observations.Clear(); }
            internal bool HasOwner(Entity owner, Entry entry)
                => owners.TryGetValue(owner, out var link) && link.Entry == entry && link.Complete;
            internal bool NeedsAcknowledge(Entity owner)
                => owners.TryGetValue(owner, out var link) && link.Dirty && !link.RetryBlocked;

            internal void Remove(Entity owner)
            {
                if (!owners.TryGetValue(owner, out var link)) return;
                Unbind(link); owners.Remove(owner);
            }
            private void Detach(Field field, SharedOwner owner)
            {
                field.Owners.Remove(owner);
                if (field.Owners.Count != 0) return;
                var source = field.Source; source.Fields.Remove(field.Key); fieldCount--;
                if (field.Managed) managed.Remove(field);
                if (source.Fields.Count == 0) { sources.Remove(source.Entity); ForgetCohortSource(source); }
                else { RecomputeMask(source); dirtySources.Add(source); }

            }
            private void Unbind(SharedOwner owner)
            {
                foreach (var field in owner.Fields) Detach(field, owner);
                owner.Fields.Clear(); owner.Complete = false;
                owner.ReadSources.Clear();
            }
            private void ReleasePreviousBindings(SharedOwner owner)
            {
                foreach (var field in previousBindings) Detach(field, owner);
                previousBindings.Clear();
            }
            private static void RecomputeMask(Source source)
            { source.Mask = 0; foreach (var field in source.Fields.Values) source.Mask |= field.Mask; }

            internal void Register(EntityManager em, ExternalPaintOwnership foreign, Entity entity, Entry entry)
            {
                if (world != null && !ReferenceEquals(world, em.World)) { Clear(); }
                if (external != null && (external != foreign || resolver != parent.KnownNonPaintPrefab)) { Clear(); }
                world = em.World; external = foreign; resolver = parent.KnownNonPaintPrefab;
                bool fresh = true;
                if (!owners.TryGetValue(entity, out var owner)) { owner = new SharedOwner { Entity = entity }; owners.Add(entity, owner); }
                else
                {
                    fresh = owner.Entry != entry;
                    previousBindings.Clear();
                    foreach (var field in owner.Fields) previousBindings.Add(field);
                    owner.Fields.Clear();
                }
                owner.Entry = entry; owner.Dirty = owner.RetryBlocked = false; owner.Complete = true;
                owner.ReadSources.Clear(); owner.RequiredMask = 0; registering.Clear(); registeringPrefabs.Clear();
                if (!SupportedInput(entry.Owner) || entry.Start != null && !SupportedInput(entry.Start)
                    || entry.End != null && !SupportedInput(entry.End))
                {
                    ReleasePreviousBindings(owner);
                    owner.Complete = false; owner.RetryBlocked = true;
                    return;
                }
                try
                {
                    RegisterOwner(em, owner, entity, entry.Owner, fresh);
                    var edge = entry.Owner.State.Edge;
                    if (entry.RoadContext && edge.Present)
                    { RegisterOwner(em, owner, edge.Data.m_Start, entry.Start, fresh); RegisterOwner(em, owner, edge.Data.m_End, entry.End, fresh); }
                    ReleasePreviousBindings(owner);
                    if (owner.Complete) ArmCapturedInputs(em, owner);
                    else { Unbind(owner); owner.RetryBlocked = true; }
                }
                catch
                {
                    // Authoritative capture already succeeded. An optional
                    // index failure must not break its update or acknowledge it.
                    ReleasePreviousBindings(owner);
                    Unbind(owner); owner.Complete = false; owner.Dirty = true; owner.RetryBlocked = true;
                }
                finally { registering.Clear(); registeringPrefabs.Clear(); previousBindings.Clear(); if (parent.batch == null) { observations.Clear(); StartDemandPhase(); } }
            }

            private static bool SupportedInput(OwnerInput input)
            {
                if (input == null) return false;
                // Do not make an uncertifiable parking input part of the
                // shared observer's read mask either: its owners still use the
                // established path, so joining its writers would add pure cost.
                for (int i = 0; i < input.MemberCount; i++) if (input.Lanes[i].Parking) return false;
                return true;
            }

            private void Add(EntityManager em, SharedOwner owner, Entity entity, Field incoming)
            {
                if (entity == Entity.Null) return;
                bool changed = false;
                if (!sources.TryGetValue(entity, out var source))
                {
                    // Check both bounds before publishing an empty Source. A
                    // rejected first field must leave no unbound registry entry.
                    if (sources.Count == MaximumSources || fieldCount == MaximumFields) throw new InvalidOperationException();
                    source = new Source { Entity = entity }; sources.Add(entity, source);
                }
                if (!source.Fields.TryGetValue(incoming.Key, out var field))
                {
                    if (fieldCount == MaximumFields) throw new InvalidOperationException();
                    field = incoming; field.Source = source; field.Owners = new List<SharedOwner>();
                    if (field.Managed) managed.Add(field);
                    source.Fields.Add(field.Key, field); fieldCount++; source.CheckedEpoch = 0;
                    changed = true;
                }
                else
                {
                    // A newer capture may observe a shared source before its
                    // other owners have processed it. Keep those owners dirty.
                    if (!field.ExpectedSame(incoming))
                    {
                        // A first new value acknowledges this owner's successful
                        // capture, but must dirty every other dependent owner.
                        // Conflicting repeated reads within this same registration
                        // still dirty the registering owner, as before.
                        bool repeated = registering.Contains(field);
                        foreach (var dependent in field.Owners)
                            if (dependent != owner || repeated) dependent.Dirty = true;
                        field.Stamped = false; field.ProofEpoch = 0; source.CheckedEpoch = 0;
                        changed = true;
                    }
                    field.Replace(incoming);
                }
                if (registering.Add(field))
                {
                    if (!previousBindings.Remove(field)) field.Owners.Add(owner);
                    owner.Fields.Add(field);
                }
                uint previousMask = source.Mask;
                source.Mask |= field.Mask;
                // Rebinding the same observation does not change cohort
                // membership or its proof. Keep real value/schema changes on
                // the conservative remap/rearm path.
                if (changed || previousMask != source.Mask) dirtySources.Add(source);

            }

            private void ArmCapturedInputs(EntityManager em, SharedOwner owner)
            {
                owner.RequiredMask = 0;
                owner.ReadSources.Clear(); registeringSources.Clear();
                foreach (var field in owner.Fields)
                {
                    owner.RequiredMask |= field.Mask;
                    if (registeringSources.Add(field.Source)) owner.ReadSources.Add(field.Source);
                }
                registeringSources.Clear();
                // Captured values are authoritative, but only a completed read
                // set can stamp them. Never borrow a busy chunk or join its job.
                if (!parent.CertificateReadsReady(em, false, owner.RequiredMask)) return;
                foreach (var field in owner.Fields)
                {
                    field.ProofEpoch = 0;
                    field.Source.CheckedEpoch = 0;
                    if (field.Managed) continue;
                    ReadProofLocation(em, field.Source, field.Source.Mask & owner.RequiredMask);
                    var source = field.Source;
                    // The existing completed read also verifies physical
                    // membership. Equal values may have moved to another chunk;
                    // those sources must still remap before cohort reuse.
                    if (!source.Bound || source.Exists != source.ProofExists
                        || source.ProofExists && (!source.Linked || source.Sequence != source.ProofSequence
                            || !source.Archetype.Equals(source.ProofArchetype) || source.LinkedMask != source.Mask))
                        dirtySources.Add(source);
                    if (!field.Source.ProofExists) { field.Stamped = false; continue; }
                    StampField(em, field);
                }
            }
            // A spare is reused only while it is not a retained source field.
            // Add copies an existing observation immediately; newly retained
            // observations gain a Source and are never reused as scratch.
            private T Observation<T>(Func<Field> create) where T : Field
            {
                if (!observations.TryGetValue(typeof(T), out var field) || field.Source != null)
                { field = create(); observations[typeof(T)] = field; }
                return (T)field;
            }
            private void Exists(EntityManager em, SharedOwner owner, Entity entity, bool expected)
            {
                var field = Observation<ExistenceField>(ExistenceField.Create);
                field.Key = 200; field.Expected = expected; Add(em, owner, entity, field);
            }
            private void Component<T>(EntityManager em, SharedOwner owner, Entity entity, ReadType type, Value<T> value) where T : unmanaged, IComponentData
            {
                var field = Observation<ComponentField<T>>(ComponentField<T>.Create);
                field.Key = (int)type; field.Mask = value.Present ? Bit(type) : 0; field.Expected = value; Add(em, owner, entity, field);
            }
            private void Buffer<T>(EntityManager em, SharedOwner owner, Entity entity, ReadType type, T[] value) where T : unmanaged, IBufferElementData
            {
                var field = Observation<BufferField<T>>(BufferField<T>.Create);
                field.Key = (int)type; field.Mask = value != null ? Bit(type) : 0; field.Expected = value; Add(em, owner, entity, field);
            }
            private void Presence<T>(EntityManager em, SharedOwner owner, Entity entity, int id, bool value) where T : unmanaged, IComponentData
            {
                var field = Observation<PresenceField<T>>(PresenceField<T>.Create);
                field.Key = 100 + id; field.Expected = value; Add(em, owner, entity, field);
            }

            private void RegisterCore(EntityManager em, SharedOwner owner, Entity entity, Core state)
            {
                if (!state.Exists || !state.Edge.Present && !state.Node.Present) Exists(em, owner, entity, state.Exists);
                if (!state.Exists) return;
                var roles = Observation<CoreRolesField>(CoreRolesField.Create);
                roles.Key = 100; roles.Deleted = state.Deleted; roles.Temporary = state.Temporary;
                roles.Lights = state.Lights; roles.Road = state.IsRoad; Add(em, owner, entity, roles);
                Component(em, owner, entity, ReadType.Edge, state.Edge);
                Component(em, owner, entity, ReadType.Node, state.Node);
                Component(em, owner, entity, ReadType.Prefab, state.Prefab);
                Component(em, owner, entity, ReadType.Composition, state.Composition);
                Component(em, owner, entity, ReadType.EdgeGeometry, state.Geometry);
                Component(em, owner, state.CompositionEntity, ReadType.CompositionData, state.CompositionData);
            }
            private void RegisterLane(EntityManager em, SharedOwner owner, Entity entity, LaneInput lane)
            {
                if (!lane.Exists) { Exists(em, owner, entity, false); return; }
                var roles = Observation<LaneRolesField>(LaneRolesField.Create);
                roles.Key = 101; roles.External = lane.External;
                roles.Expected = new PaintLaneRoles { Deleted = lane.Deleted, Temporary = lane.Temporary,
                    Generated = lane.Generated, Lane = lane.Lane, Car = lane.Car, Track = lane.Track,
                    Parking = lane.Parking, Pedestrian = lane.Pedestrian, PaintGeometry = lane.PaintGeometry,
                    HiddenJunction = lane.HiddenJunction, Master = lane.Master };
                Add(em, owner, entity, roles);
                Component(em, owner, entity, ReadType.Prefab, lane.Prefab);
                Component(em, owner, entity, ReadType.Owner, lane.Owner);
                if (lane.Prefab.Present)
                {
                    var prefab = lane.Prefab.Data.m_Prefab;
                    Exists(em, owner, prefab, em.Exists(prefab));
                    var classification = Observation<PrefabClassificationField>(PrefabClassificationField.Create);
                    classification.Key = 201; classification.Managed = true;
                    classification.Expected = parent.KnownNonPaintPrefab != null && parent.KnownNonPaintPrefab(prefab);
                    Add(em, owner, prefab, classification);
                    if (lane.AccessOnly)
                    {
                        Presence<NetLaneGeometryData>(em, owner, prefab, 12, em.Exists(prefab) && Access(em, parent.batch).HasComponent<NetLaneGeometryData>(prefab));
                        return;
                    }
                    RegisterPrefabComponents(em, owner, prefab, lane);
                }
                if (lane.AccessOnly) return;
                Component(em, owner, entity, ReadType.Curve, lane.Curve);
                Component(em, owner, entity, ReadType.EdgeLane, lane.EdgeLane);
                if (lane.Car)
                {
                    var car = Observation<CarField>(CarField.Create);
                    car.Key = (int)ReadType.CarPaint; car.Mask = Bit(ReadType.CarPaint); car.Expected = lane.PaintFlags; Add(em, owner, entity, car);
                    if (lane.Lane)
                    {
                        var route = Observation<RouteField>(RouteField.Create);
                        route.Key = (int)ReadType.LaneRoute; route.Mask = Bit(ReadType.LaneRoute);
                        route.Start = lane.StartOwnerIndex; route.End = lane.EndOwnerIndex; Add(em, owner, entity, route);
                    }
                }
                if (lane.Parking)
                {
                    var value = Observation<ParkingField>(ParkingField.Create);
                    value.Key = (int)ReadType.Parking; value.Mask = Bit(ReadType.Parking); value.Expected = lane.ParkingFlags; Add(em, owner, entity, value);
                }
                if (lane.Pedestrian)
                {
                    var value = Observation<PedestrianField>(PedestrianField.Create);
                    value.Key = (int)ReadType.Pedestrian; value.Mask = Bit(ReadType.Pedestrian); value.Expected = lane.PedestrianFlags; Add(em, owner, entity, value);
                }
            }
            private void RegisterPrefabComponents(EntityManager em, SharedOwner owner, Entity prefab, LaneInput lane)
            {
                // These are copied snapshot values, not new native reads. Repeated
                // identical contributions to this owner's same fields need no
                // rebinding. Compare all values so conflicting captures retain
                // Add's dirty-owner behavior, even outside a read-only batch.
                if (registeringPrefabs.TryGetValue(prefab, out var prior)
                    && prior.Data.Same(lane.Data) && prior.Geometry.Same(lane.Geometry)
                    && prior.Parking.Same(lane.ParkingData)) return;
                Component(em, owner, prefab, ReadType.LaneData, lane.Data);
                Component(em, owner, prefab, ReadType.LaneGeometry, lane.Geometry);
                Component(em, owner, prefab, ReadType.ParkingData, lane.ParkingData);
                if (registeringPrefabs.Count < 512 || registeringPrefabs.ContainsKey(prefab))
                    registeringPrefabs[prefab] = new LanePrefabInput { Data = lane.Data, Geometry = lane.Geometry, Parking = lane.ParkingData };
            }

            private void RegisterOwner(EntityManager em, SharedOwner owner, Entity entity, OwnerInput input, bool fresh)
            {
                if (input == null) throw new InvalidOperationException();
                RegisterCore(em, owner, entity, input.State);
                Buffer(em, owner, entity, ReadType.ConnectedEdge, input.Connections);
                // Copy RAW membership only at a successful authoritative read.
                // Projected snapshots can omit path-only members; those members
                // still need role/prefab/owner watches for future conversions.
                bool rawSnapshot = fresh && (input.Members == null || input.MemberCount == input.Members.Length);
                var members = rawSnapshot ? input.Members : Copy<Game.Net.SubLane>(em, entity, parent.batch);
                Buffer(em, owner, entity, ReadType.SubLane, members);
                if (members != null)
                {
                    if (members.Length > MaximumMembers) throw new InvalidOperationException();
                    for (int i = 0; i < members.Length; i++)
                    {
                        var member = members[i];
                        var lane = rawSnapshot ? input.Lanes[i] : LaneInput.Read(em, external, member.m_SubLane, parent.KnownNonPaintPrefab,
                            input.State.IsRoad && !input.State.Node.Present, entity, parent.batch);
                        // Overlap-dependent parking policies need a separate
                        // complete dependency projection; do not certify them.
                        if (lane.Parking) owner.Complete = false;
                        RegisterLane(em, owner, member.m_SubLane, lane);
                    }
                }
                if (input.Connections != null)
                    for (int i = 0; i < input.Connections.Length; i++) RegisterCore(em, owner, input.Connections[i].m_Edge, input.ConnectedRoads[i]);
                Buffer(em, owner, input.State.CompositionEntity, ReadType.CompositionLane, input.CompositionLanes);
                Buffer(em, owner, input.State.CompositionEntity, ReadType.CompositionPiece, input.CompositionPieces);
                if (input.State.Prefab.Present) Buffer(em, owner, input.State.Prefab.Data.m_Prefab, ReadType.GeometrySection, input.Sections);
            }

            // Keep the recorder's existing Observe hook around the complete
            // requested-owner proof; no global graph sweep is performed.
            // Reuse a completed cohort observation only inside the caller's
            // non-mutating collection phase. Begin/End, disabling read-only
            // values and all existing invalidation boundaries revoke it.
            // Busy/partial proofs and unknown owners retain demand validation.
            internal bool TryCollectedProof(EntityManager em, ExternalPaintOwnership foreign,
                Entity entity, Entry entry, out bool unchanged)
            {
                unchanged = false;
                if (cohortObserved && cohortAvailable
                    && world != null && ReferenceEquals(world, em.World)
                    && external == foreign && resolver == parent.KnownNonPaintPrefab
                    && owners.TryGetValue(entity, out var owner)
                    && owner.Entry == entry && owner.Complete)
                {
                    unchanged = !owner.Dirty;
                    return true;
                }
                return false;
            }

            private bool Observe(EntityManager em, ExternalPaintOwnership foreign)
                => DemandOrCohort(em, foreign);

            internal bool Unchanged(EntityManager em, ExternalPaintOwnership foreign, Entity entity, Entry entry)
            {
                if (!owners.TryGetValue(entity, out var owner) || owner.Entry != entry || !owner.Complete || owner.Dirty) return false;
                requestedOwner = owner;
                try { return Observe(em, foreign) && !owner.Dirty; }
                catch { return false; } // An optional proof never suppresses fallback.
                finally { requestedOwner = null; }
            }
            internal void Clear()
            {
                owners.Clear(); sources.Clear(); registering.Clear(); registeringPrefabs.Clear(); observations.Clear(); previousBindings.Clear();
                world = null; external = null; resolver = null; fieldCount = 0;
                ClearCohorts(); registeringSources.Clear();
                proofChunks.Clear(); proofChunkPool.Clear(); requestedOwner = null; StartDemandPhase();
            }
        }
    }
}
