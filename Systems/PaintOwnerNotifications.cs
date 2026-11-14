using System;
using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using SubLane = Game.Net.SubLane;
using CarLane = Game.Net.CarLane;
using TrackLane = Game.Net.TrackLane;
using ParkingLane = Game.Net.ParkingLane;

namespace MedianGrassReplacement.Systems
{
    // A chunk write notification is not proof that this owner's inputs changed.
    // Consult this only for existing notifications, never as a recurring scan.
    // Explicit edits, full refreshes and recovery queues bypass it.
    internal sealed class PaintOwnerNotifications
    {
        private const int Capacity = 1024;
        private const int MaximumMembers = 2048;
        private readonly Dictionary<Entity, Entry> entries = new Dictionary<Entity, Entry>();
        private readonly Queue<Entity> order = new Queue<Entity>();
        // Installed once by the owning system. Resolve managed prefab identity,
        // not a cached guess based on lane flags or a native data dependency.
        internal Func<Entity, bool> KnownNonPaintPrefab;

        // This memo exists only during the caller's read-only notification
        // phase. No value survives EndBatch; explicit/recovery work is untouched.
        private sealed class BatchReads
        {
            internal readonly PaintInputReadScope Reads = new PaintInputReadScope();
            internal readonly Dictionary<Entity, Core> Cores = new Dictionary<Entity, Core>();
            internal readonly Dictionary<LaneKey, LaneInput> Lanes = new Dictionary<LaneKey, LaneInput>();
            internal void Clear() { Reads.End(); Cores.Clear(); Lanes.Clear(); }
        }
        private struct LaneKey : IEquatable<LaneKey>
        {
            internal Entity Lane, Container;
            internal bool RoadOnly;
            public bool Equals(LaneKey v) => Lane == v.Lane && Container == v.Container && RoadOnly == v.RoadOnly;
            public override bool Equals(object v) => v is LaneKey key && Equals(key);
            public override int GetHashCode() => unchecked((Lane.GetHashCode() * 397 ^ Container.GetHashCode()) * 397 ^ (RoadOnly ? 1 : 0));
        }
        private readonly BatchReads batchStorage = new BatchReads();
        private BatchReads batch;
        internal void BeginBatch(bool enabled = true)
        {
            batchStorage.Clear();
            batch = enabled && OwnerInputEquality.Supported ? batchStorage : null;
            if (batch != null && PaintInputReadScope.Supported) batch.Reads.Begin();
        }
        internal void EndBatch() { batch = null; batchStorage.Clear(); }


        // This value wrapper also retains the original EntityManager path for
        // unbatched callers and unsupported game versions. No system lookup
        // registration is added, and no native view outlives EndBatch.
        private static ReadAccess Access(EntityManager em, BatchReads batch) => new ReadAccess(em, batch?.Reads);
        private readonly struct ReadAccess
        {
            private readonly EntityManager manager;
            private readonly PaintInputReadScope scope;
            internal ReadAccess(EntityManager manager, PaintInputReadScope scope) { this.manager = manager; this.scope = scope; }
            internal bool Exists(Entity entity) => scope != null ? scope.Exists(manager, entity) : manager.Exists(entity);
            internal bool HasComponent<T>(Entity entity) where T : unmanaged, IComponentData
                => scope != null ? scope.HasComponent<T>(manager, entity) : manager.HasComponent<T>(entity);
            internal T GetComponentData<T>(Entity entity) where T : unmanaged, IComponentData
                => scope != null ? scope.Component<T>(manager, entity) : manager.GetComponentData<T>(entity);
            internal bool HasBuffer<T>(Entity entity) where T : unmanaged, IBufferElementData
                => scope != null ? scope.HasBuffer<T>(manager, entity) : manager.HasBuffer<T>(entity);
            internal DynamicBuffer<T> GetBuffer<T>(Entity entity, bool readOnly) where T : unmanaged, IBufferElementData
                => scope != null ? scope.Buffer<T>(manager, entity) : manager.GetBuffer<T>(entity, readOnly);
        }

        private struct Value<T> where T : unmanaged, IComponentData
        {
            internal bool Present;
            internal T Data;
            internal static Value<T> Read(EntityManager em, Entity entity, BatchReads batch = null)
                => Access(em, batch).HasComponent<T>(entity)
                    ? new Value<T> { Present = true, Data = Access(em, batch).GetComponentData<T>(entity) } : default;
            internal bool Same(Value<T> other) => Present == other.Present
                && (!Present || OwnerInputEquality<T>.Same(Data, other.Data));
        }

        private struct Core
        {
            internal bool Exists, Deleted, Temporary, Lights, IsRoad;
            internal Value<Edge> Edge;
            internal Value<Node> Node;
            internal Value<PrefabRef> Prefab;
            internal Value<Composition> Composition;
            internal Value<EdgeGeometry> Geometry;
            internal Value<NetCompositionData> CompositionData;
            internal Entity CompositionEntity;
            internal static Core Read(EntityManager em, Entity owner, BatchReads batch = null)
            {
                if (batch != null && batch.Cores.TryGetValue(owner, out var known)) return known;
                var v = new Core { Exists = Access(em, batch).Exists(owner) };
                if (!v.Exists) return v;
                v.Deleted = Access(em, batch).HasComponent<Deleted>(owner);
                v.Temporary = Access(em, batch).HasComponent<Game.Tools.Temp>(owner);
                v.Lights = Access(em, batch).HasComponent<TrafficLights>(owner);
                v.Edge = Value<Edge>.Read(em, owner, batch); v.Node = Value<Node>.Read(em, owner, batch);
                // Marking logic tests road identity, not its simulation traffic
                // counters. Reading Road data here would add unrelated waits.
                v.IsRoad = Access(em, batch).HasComponent<Road>(owner); v.Prefab = Value<PrefabRef>.Read(em, owner, batch);
                v.Composition = Value<Composition>.Read(em, owner, batch);
                v.Geometry = Value<EdgeGeometry>.Read(em, owner, batch);
                if (v.Composition.Present)
                {
                    v.CompositionEntity = v.Composition.Data.m_Edge;
                    if (Access(em, batch).Exists(v.CompositionEntity)) v.CompositionData = Value<NetCompositionData>.Read(em, v.CompositionEntity, batch);
                }
                if (batch != null && batch.Cores.Count < 256) batch.Cores[owner] = v;
                return v;
            }
            internal bool Same(Core v) => Exists == v.Exists && Deleted == v.Deleted && Temporary == v.Temporary
                && Lights == v.Lights && IsRoad == v.IsRoad && Edge.Same(v.Edge) && Node.Same(v.Node)
                && Prefab.Same(v.Prefab) && Composition.Same(v.Composition) && Geometry.Same(v.Geometry)
                && CompositionEntity == v.CompositionEntity && CompositionData.Same(v.CompositionData);
        }

        private struct LaneInput
        {
            internal Entity Entity;
            internal bool Exists, Deleted, Temporary, Generated, External, Lane, Car, Track, Parking, Pedestrian, PaintGeometry, HiddenJunction, AccessOnly;
            internal ParkingLaneFlags ParkingFlags;
            internal PedestrianLaneFlags PedestrianFlags;
            internal Value<Curve> Curve;
            internal Value<PrefabRef> Prefab;
            internal Value<Owner> Owner;
            internal Value<EdgeLane> EdgeLane;
            internal Value<NetLaneData> Data;
            internal Value<NetLaneGeometryData> Geometry;
            internal Value<ParkingLaneData> ParkingData;
            internal static LaneInput Read(EntityManager em, ExternalPaintOwnership external, Entity lane, Func<Entity, bool> nonPaint, bool roadOnly, Entity container, BatchReads batch = null)
            {
                var key = new LaneKey { Lane = lane, RoadOnly = roadOnly, Container = roadOnly ? container : Entity.Null };
                if (batch != null && batch.Lanes.TryGetValue(key, out var known)) return known;
                var v = new LaneInput { Entity = lane, Exists = Access(em, batch).Exists(lane) };
                if (!v.Exists) return v;
                v.Deleted = Access(em, batch).HasComponent<Deleted>(lane); v.Temporary = Access(em, batch).HasComponent<Game.Tools.Temp>(lane);
                v.Generated = Access(em, batch).HasComponent<MGRCurbMarking>(lane); v.External = external.Contains(em, lane);
                v.Lane = Access(em, batch).HasComponent<Lane>(lane); v.Car = Access(em, batch).HasComponent<CarLane>(lane);
                v.Track = Access(em, batch).HasComponent<TrackLane>(lane); v.Parking = Access(em, batch).HasComponent<ParkingLane>(lane);
                v.Pedestrian = Access(em, batch).HasComponent<Game.Net.PedestrianLane>(lane);
                v.PaintGeometry = Access(em, batch).HasComponent<Game.Net.LaneGeometry>(lane);
                v.HiddenJunction = Access(em, batch).HasComponent<MGRHiddenJunctionLine>(lane);
                v.Prefab = Value<PrefabRef>.Read(em, lane, batch);
                // Ownership can affect building-access/parking overlap policy,
                // even when the path itself contributes no marking geometry.
                v.Owner = Value<Owner>.Read(em, lane, batch);
                // A road-owned, resolved path-only prefab cannot supply stop/line
                // geometry. Node pedestrian guides and vehicle boundary curves DO
                // feed the planners and must retain their full input checks.
                // Preserve actor identity and role changes,
                // but do not join Curve/PedestrianLane/EdgeLane writers for its path.
                // Parking, hybrids, missing registrations and foreign private copies
                // retain the complete input path.
                v.AccessOnly = roadOnly && !v.Car && !v.Track && !v.Parking && !v.PaintGeometry && v.Prefab.Present
                    && v.Owner.Present && v.Owner.Data.m_Owner == container
                    && Access(em, batch).Exists(v.Prefab.Data.m_Prefab)
                    && !Access(em, batch).HasComponent<NetLaneGeometryData>(v.Prefab.Data.m_Prefab)
                    && nonPaint != null && nonPaint(v.Prefab.Data.m_Prefab);
                if (v.AccessOnly) { if (batch != null && batch.Lanes.Count < 2048) batch.Lanes[key] = v; return v; }
                if (v.Parking) v.ParkingFlags = Access(em, batch).GetComponentData<ParkingLane>(lane).m_Flags;
                if (v.Pedestrian) v.PedestrianFlags = Access(em, batch).GetComponentData<Game.Net.PedestrianLane>(lane).m_Flags;
                v.Curve = Value<Curve>.Read(em, lane, batch);
                v.EdgeLane = Value<EdgeLane>.Read(em, lane, batch);
                if (v.Prefab.Present && Access(em, batch).Exists(v.Prefab.Data.m_Prefab))
                {
                    var prefab = v.Prefab.Data.m_Prefab;
                    v.Data = Value<NetLaneData>.Read(em, prefab, batch);
                    v.Geometry = Value<NetLaneGeometryData>.Read(em, prefab, batch);
                    v.ParkingData = Value<ParkingLaneData>.Read(em, prefab, batch);
                }
                if (batch != null && batch.Lanes.Count < 2048) batch.Lanes[key] = v;
                return v;
            }
            internal bool IgnorableAccess => AccessOnly && Exists && !Deleted && !Temporary
                && !Generated && !External && !HiddenJunction;
            internal bool Same(LaneInput v) => Entity == v.Entity && Exists == v.Exists
                && Deleted == v.Deleted && Temporary == v.Temporary && Generated == v.Generated && External == v.External
                && AccessOnly == v.AccessOnly && Lane == v.Lane && Car == v.Car && Track == v.Track && Parking == v.Parking && ParkingFlags == v.ParkingFlags
                && Pedestrian == v.Pedestrian && PedestrianFlags == v.PedestrianFlags && PaintGeometry == v.PaintGeometry && HiddenJunction == v.HiddenJunction
                && Curve.Same(v.Curve) && Prefab.Same(v.Prefab) && Owner.Same(v.Owner) && EdgeLane.Same(v.EdgeLane)
                && Data.Same(v.Data) && Geometry.Same(v.Geometry) && ParkingData.Same(v.ParkingData);
        }

        private sealed class OwnerInput
        {
            internal Core State;
            internal SubLane[] Members;
            internal int MemberCount;
            internal bool ProjectAccess;
            internal LaneInput[] Lanes;
            internal ConnectedEdge[] Connections;
            internal Core[] ConnectedRoads;
            internal NetCompositionLane[] CompositionLanes;
            internal NetCompositionPiece[] CompositionPieces;
            internal NetGeometrySection[] Sections;

            internal static OwnerInput Capture(EntityManager em, ExternalPaintOwnership external, Entity owner, Func<Entity, bool> nonPaint, BatchReads batch = null)
            {
                var v = new OwnerInput { State = Core.Read(em, owner, batch) };
                v.Connections = Copy<ConnectedEdge>(em, owner, batch);
                if (v.State.Exists && Access(em, batch).HasBuffer<SubLane>(owner))
                {
                    var current = Access(em, batch).GetBuffer<SubLane>(owner, true);
                    // Existing highway/divider planners cap RAW membership at
                    // 256. Above that boundary, even a path-only insertion can
                    // change which paint inputs they see. Keep exact membership.
                    v.ProjectAccess = v.State.IsRoad && !v.State.Node.Present && current.Length <= 256;
                    v.Members = new SubLane[current.Length];
                    v.Lanes = new LaneInput[current.Length];
                    foreach (var member in current)
                    {
                        var lane = LaneInput.Read(em, external, member.m_SubLane, nonPaint, v.State.IsRoad && !v.State.Node.Present, owner, batch);
                        if (v.ProjectAccess && lane.IgnorableAccess) continue;
                        v.Members[v.MemberCount] = member; v.Lanes[v.MemberCount] = lane;
                        v.MemberCount++;
                    }
                }
                if (v.Connections != null)
                {
                    v.ConnectedRoads = new Core[v.Connections.Length];
                    for (int i = 0; i < v.Connections.Length; i++) v.ConnectedRoads[i] = Core.Read(em, v.Connections[i].m_Edge, batch);
                }
                v.CompositionLanes = Copy<NetCompositionLane>(em, v.State.CompositionEntity, batch);
                v.CompositionPieces = Copy<NetCompositionPiece>(em, v.State.CompositionEntity, batch);
                v.Sections = v.State.Prefab.Present ? Copy<NetGeometrySection>(em, v.State.Prefab.Data.m_Prefab, batch) : null;
                return v;
            }
            private bool SameLanes(EntityManager em, ExternalPaintOwnership external, Entity owner, Func<Entity, bool> nonPaint, BatchReads batch = null)
            {
                if (!Access(em, batch).Exists(owner) || !Access(em, batch).HasBuffer<SubLane>(owner)) return Members == null;
                if (Members == null) return false;
                var current = Access(em, batch).GetBuffer<SubLane>(owner, true);
                if (current.Length > MaximumMembers) return false;
                bool project = State.IsRoad && !State.Node.Present && current.Length <= 256;
                if (project != ProjectAccess) return false;
                int index = 0;
                foreach (var member in current)
                {
                    var lane = LaneInput.Read(em, external, member.m_SubLane, nonPaint, State.IsRoad && !State.Node.Present, owner, batch);
                    if (project && lane.IgnorableAccess) continue;
                    if (index >= MemberCount || !OwnerInputEquality<SubLane>.Same(member, Members[index])
                        || !Lanes[index].Same(lane)) return false;
                    index++;
                }
                return index == MemberCount;
            }
            internal bool Same(EntityManager em, ExternalPaintOwnership external, Entity owner, Func<Entity, bool> nonPaint, BatchReads batch = null)
            {
                if (!State.Same(Core.Read(em, owner, batch)) || !SameLanes(em, external, owner, nonPaint, batch)
                    || !BufferSame(em, owner, Connections, batch)
                    || !BufferSame(em, State.CompositionEntity, CompositionLanes, batch)
                    || !BufferSame(em, State.CompositionEntity, CompositionPieces, batch)
                    || (State.Prefab.Present && !BufferSame(em, State.Prefab.Data.m_Prefab, Sections, batch))) return false;
                if (Connections != null)
                    for (int i = 0; i < Connections.Length; i++)
                        if (!ConnectedRoads[i].Same(Core.Read(em, Connections[i].m_Edge, batch))) return false;
                return true;
            }
        }

        private sealed class Entry
        {
            internal bool RoadContext;
            internal OwnerInput Owner, Start, End;
        }

        private static T[] Copy<T>(EntityManager em, Entity owner, BatchReads batch = null) where T : unmanaged, IBufferElementData
        {
            if (!Access(em, batch).Exists(owner) || !Access(em, batch).HasBuffer<T>(owner)) return null;
            var buffer = Access(em, batch).GetBuffer<T>(owner, true);
            var values = new T[buffer.Length];
            for (int i = 0; i < values.Length; i++) values[i] = buffer[i];
            return values;
        }
        private static bool BufferSame<T>(EntityManager em, Entity owner, T[] previous, BatchReads batch = null) where T : unmanaged, IBufferElementData
        {
            bool present = Access(em, batch).Exists(owner) && Access(em, batch).HasBuffer<T>(owner);
            if (!present) return previous == null;
            if (previous == null) return false;
            var buffer = Access(em, batch).GetBuffer<T>(owner, true);
            if (buffer.Length != previous.Length) return false;
            for (int i = 0; i < previous.Length; i++)
                if (!OwnerInputEquality<T>.Same(buffer[i], previous[i])) return false;
            return true;
        }

        internal bool Changed(EntityManager em, ExternalPaintOwnership external, Entity owner, bool roadContext = false)
        {
            if (!OwnerInputEquality.Supported) return true;
            if (!Access(em, batch).Exists(owner) || (!Access(em, batch).HasComponent<Road>(owner) && !Access(em, batch).HasComponent<Node>(owner))) return true;
            // Unusually large foreign graphs keep the existing path rather than
            // allocating an unbounded input snapshot.
            if (entries.TryGetValue(owner, out var previous) && previous.RoadContext == roadContext
                && previous.Owner.Same(em, external, owner, KnownNonPaintPrefab, batch))
            {
                var savedEdge = previous.Owner.State.Edge;
                if (!roadContext || !savedEdge.Present || (previous.Start.Same(em, external, savedEdge.Data.m_Start, KnownNonPaintPrefab, batch)
                    && previous.End.Same(em, external, savedEdge.Data.m_End, KnownNonPaintPrefab, batch))) return false;
            }

            // Bounds checks are needed only before allocating a new snapshot.
            // An existing snapshot detects new buffer sizes without copying them.
            if (!WithinBudget(em, owner, batch)) return true;
            var edge = Value<Edge>.Read(em, owner, batch);
            if (roadContext && edge.Present && (!WithinBudget(em, edge.Data.m_Start, batch) || !WithinBudget(em, edge.Data.m_End, batch))) return true;

            var next = new Entry { RoadContext = roadContext, Owner = OwnerInput.Capture(em, external, owner, KnownNonPaintPrefab, batch) };
            if (roadContext && edge.Present)
            {
                next.Start = OwnerInput.Capture(em, external, edge.Data.m_Start, KnownNonPaintPrefab, batch);
                next.End = OwnerInput.Capture(em, external, edge.Data.m_End, KnownNonPaintPrefab, batch);
            }
            if (!entries.ContainsKey(owner))
            {
                if (entries.Count == Capacity) entries.Remove(order.Dequeue());
                order.Enqueue(owner);
            }
            entries[owner] = next;
            return true;
        }
        private static bool WithinBudget(EntityManager em, Entity owner, BatchReads batch = null)
        {
            if (!Access(em, batch).Exists(owner)) return true;
            if (Access(em, batch).HasBuffer<SubLane>(owner) && Access(em, batch).GetBuffer<SubLane>(owner, true).Length > MaximumMembers) return false;
            if (Access(em, batch).HasBuffer<ConnectedEdge>(owner) && Access(em, batch).GetBuffer<ConnectedEdge>(owner, true).Length > 64) return false;
            if (Access(em, batch).HasComponent<Composition>(owner))
            {
                var composition = Access(em, batch).GetComponentData<Composition>(owner).m_Edge;
                if (!BufferWithin<NetCompositionLane>(em, composition, batch) || !BufferWithin<NetCompositionPiece>(em, composition, batch)) return false;
            }
            if (Access(em, batch).HasComponent<PrefabRef>(owner)
                && !BufferWithin<NetGeometrySection>(em, Access(em, batch).GetComponentData<PrefabRef>(owner).m_Prefab, batch)) return false;
            return true;
        }
        private static bool BufferWithin<T>(EntityManager em, Entity entity, BatchReads batch = null) where T : unmanaged, IBufferElementData
            => !Access(em, batch).Exists(entity) || !Access(em, batch).HasBuffer<T>(entity) || Access(em, batch).GetBuffer<T>(entity, true).Length <= MaximumMembers;
        internal void Clear() { EndBatch(); entries.Clear(); order.Clear(); }
    }
}
