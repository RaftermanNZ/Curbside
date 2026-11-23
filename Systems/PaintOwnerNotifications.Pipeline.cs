using System;
using System.Collections.Generic;
using Unity.Entities;
using Game.Net;

namespace MedianGrassReplacement.Systems
{
    internal sealed partial class PaintOwnerNotifications
    {
        private SharedAuthority.SharedClient sharedClient;
        private PaintOwnerNotifications cohortPeer;
        private bool sharedBatch;

        internal void AttachSharedAuthority(SharedAuthority authority, ExternalPaintOwnership external)
        {
            EndBatch();
            sharedClient?.Detach();
            sharedClient = authority.Connect(this, external);
        }

        // One authority retains current input facts. Every consumer keeps its own
        // acknowledgement; observing a change never consumes another system's work.
        internal sealed class SharedAuthority
        {
            private readonly object world;
            private readonly ExternalPaintOwnership external;
            private readonly PaintOwnerNotifications road, owner;
            private readonly List<SharedClient> clients = new List<SharedClient>();
            private SharedClient lease;
            private bool roadStarted, ownerStarted, readOnly;
            private int preparedCount;

            internal SharedAuthority(object world, Func<Entity, bool> resolver, ExternalPaintOwnership external)
            {
                this.world = world;
                this.external = external;
                road = new PaintOwnerNotifications { SharedInputDiscovery = true, KnownNonPaintPrefab = resolver };
                owner = new PaintOwnerNotifications { SharedInputDiscovery = true, KnownNonPaintPrefab = resolver };
                road.cohortPeer = owner;
                owner.cohortPeer = road;
            }

            internal SharedClient Connect(PaintOwnerNotifications consumer, ExternalPaintOwnership foreign)
            {
                var client = new SharedClient(this, consumer, foreign);
                clients.Add(client);
                return client;
            }

            internal void Reset()
            {
                // Called at preload, never as ordinary feature maintenance.
                // Include private fallback histories and their query/read scopes;
                // no fact or native handle may survive a load boundary.
                foreach (var client in clients) client.ClearConsumer();
                road.Clear();
                owner.Clear();
                external.Clear();
                lease = null;
            }

            private PaintOwnerNotifications Engine(bool roadContext)
            {
                var engine = roadContext ? road : owner;
                if (!(roadContext ? roadStarted : ownerStarted))
                {
                    if (roadContext) roadStarted = true; else ownerStarted = true;
                    engine.BeginBatch(readOnly);
                    bool active = true;
                    engine.PrepareInputCohort(preparedCount, ref active);
                }
                return engine;
            }

            private void ReadOnly(bool enabled)
            {
                readOnly = enabled;
                if (roadStarted) road.SetReadOnlyValues(enabled);
                if (ownerStarted) owner.SetReadOnlyValues(enabled);
            }

            private void Prepare(int count)
            {
                // PreferCohorts dominates a smaller local hint within a batch.
                preparedCount = Math.Max(preparedCount, count);
                bool active = true;
                if (roadStarted) road.PrepareInputCohort(count, ref active);
                if (ownerStarted) owner.PrepareInputCohort(count, ref active);
            }

            private bool Enter(SharedClient client, bool enabled)
            {
                if (lease != null) return false;
                try
                {
                    roadStarted = ownerStarted = false;
                    readOnly = enabled; preparedCount = 0;
                    lease = client;
                    return true;
                }
                catch
                {
                    if (roadStarted) road.EndBatch();
                    if (ownerStarted) owner.EndBatch();
                    return false;
                }
            }

            private void Leave(SharedClient client)
            {
                if (lease != client) return;
                try { if (roadStarted) road.EndBatch(); }
                finally { if (ownerStarted) owner.EndBatch(); roadStarted = ownerStarted = false; lease = null; }
            }

            private readonly struct Key : IEquatable<Key>
            {
                internal readonly Entity Entity;
                internal readonly bool Road;
                internal Key(Entity entity, bool road) { Entity = entity; Road = road; }
                public bool Equals(Key other) => Entity == other.Entity && Road == other.Road;
                public override bool Equals(object other) => other is Key key && Equals(key);
                public override int GetHashCode() => unchecked(Entity.GetHashCode() * 397 ^ (Road ? 1 : 0));
            }

            internal sealed class SharedClient
            {
                private const int MaximumAcknowledgements = 4096;
                private readonly SharedAuthority authority;
                private readonly PaintOwnerNotifications consumer;
                private readonly ExternalPaintOwnership callerExternal;
                private readonly Func<Entity, bool> callerResolver;
                private readonly Dictionary<Key, Entry> seen = new Dictionary<Key, Entry>();
                private readonly HashSet<Key> privateOwners = new HashSet<Key>();
                private long seenSlots;

                internal SharedClient(SharedAuthority authority, PaintOwnerNotifications consumer, ExternalPaintOwnership external)
                {
                    this.authority = authority;
                    this.consumer = consumer;
                    callerExternal = external;
                    callerResolver = consumer.KnownNonPaintPrefab;
                }

                internal bool Matches(EntityManager manager, ExternalPaintOwnership external)
                    => ReferenceEquals(authority.world, manager.World) && external == callerExternal
                        && consumer.KnownNonPaintPrefab == callerResolver;

                internal bool Begin(bool enabled) => authority.Enter(this, enabled);
                internal void End() => authority.Leave(this);
                internal void Forget() { seen.Clear(); privateOwners.Clear(); seenSlots = 0; }
                internal void ClearConsumer() => consumer.Clear();
                internal void Detach() { authority.Leave(this); authority.clients.Remove(this); Forget(); }
                internal bool CanShare(Entity entity, bool roadContext) => !privateOwners.Contains(new Key(entity, roadContext));
                private void Remember(Key key, Entry current)
                {
                    long previousSlots = seen.TryGetValue(key, out var previous) ? previous.RetainedSlots : 0;
                    if (current.RetainedSlots > MaximumRetainedSlots) return;
                    if (!seen.ContainsKey(key) && seen.Count == MaximumAcknowledgements
                        || seenSlots - previousSlots + current.RetainedSlots > MaximumRetainedSlots)
                    { seen.Clear(); seenSlots = 0; previousSlots = 0; }
                    seen[key] = current;
                    seenSlots += current.RetainedSlots - previousSlots;
                }
                internal void ReadOnly(bool enabled)
                {
                    authority.ReadOnly(enabled);
                }
                internal void Prepare(int count)
                {
                    authority.Prepare(count);
                }
                internal bool AcknowledgesTopology(Entity entity, object current)
                    => AcknowledgesGeometry(entity, current, false);
                internal bool AcknowledgesGeometry(Entity entity, object current, bool roadContext)
                    => CanShare(entity, roadContext) && seen.TryGetValue(new Key(entity, roadContext), out var previous)
                        && ReferenceEquals(previous, current);

                internal bool UnchangedTopologyChunk(EntityManager manager, ArchetypeChunk chunk, int candidateCount)
                {
                    return UnchangedGeometryChunk(manager, chunk, candidateCount, false);
                }
                internal bool UnchangedGeometryChunk(EntityManager manager, ArchetypeChunk chunk, int candidateCount, bool roadContext)
                {
                    if (seen.Count == 0) return false;
                    var engine = authority.Engine(roadContext);
                    return engine.batch != null && engine.batch.ValuesEnabled && engine.sharedInputs != null
                        && engine.sharedInputs.UnchangedGeometryChunk(manager, authority.external, chunk, candidateCount, roadContext, this);
                }

                internal bool Changed(EntityManager manager, Entity entity, bool roadContext)
                {
                    var engine = authority.Engine(roadContext);
                    engine.entries.TryGetValue(entity, out var before);
                    bool changed = engine.Changed(manager, authority.external, entity, roadContext);
                    // Over-budget/unknown inputs can report change without a new
                    // retained snapshot. Never publish the old entry as current.
                    if (!engine.entries.TryGetValue(entity, out var current)
                        || current.RoadContext != roadContext
                        || changed && ReferenceEquals(before, current)) return true;
                    var key = new Key(entity, roadContext);
                    bool known = seen.TryGetValue(key, out var previous);
                    bool same = known && SameCaptured(previous, current);
                    if (same && !ReferenceEquals(previous, current))
                    {
                        // Equal inputs have returned after another stage's
                        // different observation. Avoid sharing this owner across
                        // those stages again; the original validator remains.
                        if (privateOwners.Count == MaximumAcknowledgements) privateOwners.Clear();
                        privateOwners.Add(key);
                        consumer.SeedPrivateSnapshot(manager, callerExternal, entity, current);
                        // The private validator now owns this history. Keeping
                        // a second cursor would retain evicted private inputs.
                        seen.Remove(key);
                        seenSlots -= previous.RetainedSlots;
                        return false;
                    }
                    bool deliver = !same;
                    Remember(key, current);
                    return deliver;
                }

                internal bool Unchanged(EntityManager manager, Entity entity, bool roadContext, bool beforeSources, bool collected)
                {
                    var key = new Key(entity, roadContext);
                    if (!seen.TryGetValue(key, out var previous)) return false;
                    var engine = authority.Engine(roadContext);
                    bool active = true;
                    bool same = beforeSources
                        ? engine.UnchangedBeforeSources(manager, authority.external, entity, ref active)
                        : collected
                            ? engine.UnchangedForCollectedInput(manager, authority.external, entity, roadContext, ref active)
                            : engine.UnchangedForInputNotification(manager, authority.external, entity, roadContext, ref active);
                    return same && engine.entries.TryGetValue(entity, out var current)
                        && current.RoadContext == roadContext && SameCaptured(previous, current);
                }
            }
        }

        private void SeedPrivateSnapshot(EntityManager manager, ExternalPaintOwnership external, Entity entity, Entry current)
        {
            entries.TryGetValue(entity, out var previous);
            var copy = new Entry { Owner = current.Owner, Start = current.Start, End = current.End,
                RoadContext = current.RoadContext, RetainedSlots = current.RetainedSlots,
                SeenCacheEpoch = cachePhase ? cacheEpoch : 0 };
            if (!MakeCacheRoom(entity, previous, copy.RetainedSlots)) return;
            sharedInputs?.Remove(entity);
            if (previous == null) order.Enqueue(entity);
            entries[entity] = copy;
            retainedSlots += copy.RetainedSlots;
            // These immutable inputs were just validated by the authority. Do
            // not copy its certificate, handles or borrowed native views.
            RegisterSharedInputs(manager, external, entity, copy);
        }

        private static bool SameCopiedBuffer<T>(T[] a, T[] b) where T : unmanaged, IBufferElementData
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!OwnerInputEquality<T>.Same(a[i], b[i])) return false;
            return true;
        }
        private static bool SameCapturedOwner(OwnerInput a, OwnerInput b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || !a.State.Same(b.State)
                || a.ProjectAccess != b.ProjectAccess || a.MemberCount != b.MemberCount
                || (a.Members == null) != (b.Members == null)
                || !SameCopiedBuffer(a.Connections, b.Connections)
                || !SameCopiedBuffer(a.CompositionLanes, b.CompositionLanes)
                || !SameCopiedBuffer(a.CompositionPieces, b.CompositionPieces)
                || !SameCopiedBuffer(a.Sections, b.Sections)) return false;
            for (int i = 0; i < a.MemberCount; i++)
                if (!OwnerInputEquality<SubLane>.Same(a.Members[i], b.Members[i]) || !a.Lanes[i].Same(b.Lanes[i])) return false;
            if ((a.ConnectedRoads == null) != (b.ConnectedRoads == null)) return false;
            if (a.ConnectedRoads != null)
            {
                if (a.ConnectedRoads.Length != b.ConnectedRoads.Length) return false;
                for (int i = 0; i < a.ConnectedRoads.Length; i++) if (!a.ConnectedRoads[i].Same(b.ConnectedRoads[i])) return false;
            }
            return true;
        }
        private static bool SameCaptured(Entry a, Entry b)
            => ReferenceEquals(a, b) || a != null && b != null && a.RoadContext == b.RoadContext
                && SameCapturedOwner(a.Owner, b.Owner) && SameCapturedOwner(a.Start, b.Start) && SameCapturedOwner(a.End, b.End);

        private bool TrySharedProbe(EntityManager manager, ExternalPaintOwnership external,
            Entity entity, bool roadContext, bool beforeSources, bool collected, ref bool phaseStarted, out bool unchanged)
        {
            unchanged = false;
            if (sharedClient == null || !sharedClient.Matches(manager, external)
                || !sharedClient.CanShare(entity, roadContext)) return false;
            if (!phaseStarted) { BeginBatch(!beforeSources); phaseStarted = true; }
            if (!sharedBatch) return false;
            unchanged = sharedClient.Unchanged(manager, entity, roadContext, beforeSources, collected);
            return true;
        }

        private bool TrySharedChange(EntityManager manager, ExternalPaintOwnership external,
            Entity entity, bool roadContext, out bool changed)
        {
            changed = false;
            if (sharedClient == null || !sharedClient.Matches(manager, external)
                || !sharedClient.CanShare(entity, roadContext)) return false;
            bool standalone = !sharedBatch;
            // A nested or incompatible phase uses the original private engine.
            if (standalone && (batch != null || !sharedClient.Begin(true))) return false;
            try { changed = sharedClient.Changed(manager, entity, roadContext); return true; }
            finally { if (standalone) sharedClient.End(); }
        }
    }
}
