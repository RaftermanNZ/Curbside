using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Game.Serialization;
using Unity.Collections;
using Unity.Entities;
using NetLane = Game.Net.Lane;
using CarLane = Game.Net.CarLane;
using ParkingLane = Game.Net.ParkingLane;
using TrackLane = Game.Net.TrackLane;
using PedestrianLane = Game.Net.PedestrianLane;
using SubLane = Game.Net.SubLane;

namespace MedianGrassReplacement.Systems
{
    internal readonly struct NativePaintRole : IEquatable<NativePaintRole>
    {
        internal readonly Entity Owner, Prefab;
        internal readonly NetLane Lane;
        internal NativePaintRole(Entity owner, Entity prefab, NetLane lane) { Owner = owner; Prefab = prefab; Lane = lane; }
        public bool Equals(NativePaintRole other) => Owner == other.Owner && Prefab == other.Prefab && Lane.Equals(other.Lane);
        public override bool Equals(object value) => value is NativePaintRole role && Equals(role);
        public override int GetHashCode()
        {
            unchecked { int h = Owner.GetHashCode() * 397 ^ Prefab.GetHashCode();
                h = h * 397 ^ Lane.m_StartNode.GetHashCode(); h = h * 397 ^ Lane.m_MiddleNode.GetHashCode();
                return h * 397 ^ Lane.m_EndNode.GetHashCode(); }
        }
    }

    [DisableAutoCreation]
    public partial class NativePaintProjectionSystem : GameSystemBase, IPreSerialize
    {
        private struct Lease
        {
            internal Entity Source, Owner, Original, NativePrefab, AppliedPrefab;
            internal TempFlags Flags;
            internal bool Hidden, Prefab, Keep;
        }
        private EntityQuery changed, active;
        private ToolSystem tools;
        private CrosswalkVisualSystem crossings;
        private NativeCrosswalkDragSystem drag;
        private bool includeActive, consumedPass;
        private uint lastPass;
        private int nextPrune = 256;
        private bool reportedBatch;
        private ModificationBarrier4B nativeBoundary;
        private readonly Dictionary<Entity, int> retainedSources = new Dictionary<Entity, int>();
        private readonly NativeProjectionLeaseIndex<Entity, Lease> leases = new NativeProjectionLeaseIndex<Entity, Lease>();
        private readonly HashSet<Entity> pendingSources = new HashSet<Entity>(), actorWork = new HashSet<Entity>();
        private readonly List<Entity> dependents = new List<Entity>();
        private readonly NativeProjectionRoleIndex<NativePaintRole, Entity> roles = new NativeProjectionRoleIndex<NativePaintRole, Entity>();
        private readonly NativeProjectionRoleIndex<NativePaintRole, Entity> endpointRoles = new NativeProjectionRoleIndex<NativePaintRole, Entity>();
        private readonly HashSet<Entity> indexed = new HashSet<Entity>();
        private readonly PreviewSourceLookup curveSources = new PreviewSourceLookup();
        private readonly Dictionary<Entity, TemporaryPreviewWrite> writes = new Dictionary<Entity, TemporaryPreviewWrite>();
        private readonly List<Entity> cleanup = new List<Entity>();
        private readonly Dictionary<int, Entity> pathOwners = new Dictionary<int, Entity>();

        protected override void OnCreate()
        {
            base.OnCreate(); tools = World.GetOrCreateSystemManaged<ToolSystem>();
            crossings = World.GetOrCreateSystemManaged<CrosswalkVisualSystem>();
            drag = World.GetExistingSystemManaged<NativeCrosswalkDragSystem>();
            nativeBoundary = World.GetOrCreateSystemManaged<ModificationBarrier4B>();
            active = GetEntityQuery(Description(false), ObjectDescription(false));
            changed = GetEntityQuery(Description(true), ObjectDescription(true));
            CreateOwnerQueries();
            tools.EventToolChanged += ToolChanged;
            Enabled = Allowed();
        }
        private static EntityQueryDesc Description(bool events) => new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<NetLane>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<LaneGeometry>(), ComponentType.ReadOnly<PrefabRef>() },
            Any = events ? new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() } : Array.Empty<ComponentType>(),
            None = new[] { ComponentType.ReadOnly<CarLane>(), ComponentType.ReadOnly<ParkingLane>(), ComponentType.ReadOnly<TrackLane>(), ComponentType.ReadOnly<MGRTemporaryPreviewPaint>() }
        };
        private static EntityQueryDesc ObjectDescription(bool events) => new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Game.Objects.ObjectGeometry>() },
            Any = events ? new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() } : Array.Empty<ComponentType>(),
            None = new[] { ComponentType.ReadOnly<Game.Objects.TrafficLight>(), ComponentType.ReadOnly<Game.Objects.StreetLight>() }
        };
        private bool Allowed() => NativeSignalProjectionSystem.Supported && Mod.Settings?.CurbsidePreviews == true
            && NativeSignalProjectionPolicy.AllowsTool(tools.activeTool?.toolID);
        private bool Live(Entity actor) => EntityManager.Exists(actor) && !EntityManager.HasComponent<Deleted>(actor);
        private void ToolChanged(ToolBaseSystem tool) { reportedBatch = false; ReleaseAndReset(); Enabled = Allowed(); }
        internal void ModeChanged() { reportedBatch = false; ReleaseAndReset(); Enabled = Allowed(); includeActive = Enabled; }
        internal void RunIfWork()
        {
            if (!Enabled || !Allowed() || Mod.Saving) return;
            uint pass = nativeBoundary.LastSystemVersion;
            if (awaitRefresh && pass == refreshPass) return;
            awaitRefresh = false;
            if (consumedPass && lastPass == pass && !includeActive && pendingSources.Count == 0 && pendingOwners.Count == 0 && refresh.Count == 0 && refreshOwners.Count == 0) return;
            if (!includeActive && pendingSources.Count == 0 && pendingOwners.Count == 0 && refresh.Count == 0 && refreshOwners.Count == 0 && changed.IsEmptyIgnoreFilter && changedOwners.IsEmptyIgnoreFilter) return;
            ProjectBatch(); lastPass = pass; consumedPass = true;
        }

        internal static void SourceChanged(World world, Entity source)
        {
            var system = world?.GetExistingSystemManaged<NativePaintProjectionSystem>();
            if (system == null || !system.Enabled || !system.Allowed() || Mod.Saving
                || system.leases.Count == 0 && system.paintOwnerWatch.Count == 0) return;
            system.pendingSources.Add(source);
            if (system.EntityManager.HasComponent<Owner>(source))
                system.pendingOwners.Add(system.EntityManager.GetComponentData<Owner>(source).m_Owner);
        }
        internal void RunPending() { if (pendingSources.Count != 0 || pendingOwners.Count != 0 || refresh.Count != 0 || refreshOwners.Count != 0) RunIfWork(); }
        internal void SelectionChanged() { includeActive = Enabled; }

        private bool OriginalOwner(Entity actor, out Entity parent, out Entity original)
        {
            parent = original = Entity.Null;
            if (!EntityManager.HasComponent<Owner>(actor)) return false;
            parent = EntityManager.GetComponentData<Owner>(actor).m_Owner;
            if (!Live(parent)) return false;
            original = EntityManager.HasComponent<Temp>(parent)
                ? EntityManager.GetComponentData<Temp>(parent).m_Original : parent;
            return Live(original) && !EntityManager.HasComponent<Temp>(original);
        }
        private Entity Canonical(Entity prefab) => crossings.Original(prefab);
        private bool Paint(Entity actor) => Live(actor) && EntityManager.HasComponent<NetLane>(actor)
            && EntityManager.HasComponent<PrefabRef>(actor) && EntityManager.HasComponent<Owner>(actor)
            && EntityManager.HasComponent<LaneGeometry>(actor) && !EntityManager.HasComponent<CarLane>(actor)
            && !EntityManager.HasComponent<ParkingLane>(actor) && !EntityManager.HasComponent<TrackLane>(actor);

        private void MapOwner(Entity entity)
        {
            if (!Live(entity) || !EntityManager.HasComponent<Temp>(entity)) return;
            var original = EntityManager.GetComponentData<Temp>(entity).m_Original;
            if (Live(original) && !EntityManager.HasComponent<Temp>(original)) pathOwners[entity.Index] = original;
        }
        private void MapEdge(Entity edge)
        {
            MapOwner(edge);
            if (!Live(edge) || !EntityManager.HasComponent<Edge>(edge)) return;
            var value = EntityManager.GetComponentData<Edge>(edge); MapOwner(value.m_Start); MapOwner(value.m_End);
        }
        private NetLane Normalize(NetLane lane)
        {
            if (pathOwners.TryGetValue(lane.m_StartNode.GetOwnerIndex(), out var a)) lane.m_StartNode.SetOwner(a);
            if (pathOwners.TryGetValue(lane.m_MiddleNode.GetOwnerIndex(), out var b)) lane.m_MiddleNode.SetOwner(b);
            if (pathOwners.TryGetValue(lane.m_EndNode.GetOwnerIndex(), out var c)) lane.m_EndNode.SetOwner(c);
            return lane;
        }
        private static NetLane Endpoints(NetLane lane) { lane.m_MiddleNode = default; return lane; }
        private Entity Resolve(Entity actor, Temp temp, Entity parent, Entity original)
        {
            if (Live(temp.m_Original))
            {
                var source = temp.m_Original;
                return !EntityManager.HasComponent<Temp>(source) && EntityManager.HasComponent<Owner>(source)
                    && !externalPaint.Contains(EntityManager, source)
                    && EntityManager.GetComponentData<Owner>(source).m_Owner == original ? source : Entity.Null;
            }
            if (!Paint(actor)) return Entity.Null; // Object decals require an actual native original link.
            if (indexed.Add(parent))
            {
                bool indexCurves = !curveSources.HasOwner(original);
                MapEdge(parent);
                if (EntityManager.HasBuffer<ConnectedEdge>(parent))
                    foreach (var edge in EntityManager.GetBuffer<ConnectedEdge>(parent, true)) MapEdge(edge.m_Edge);
                if (EntityManager.HasBuffer<SubLane>(original))
                    foreach (var entry in EntityManager.GetBuffer<SubLane>(original, true))
                    {
                        var source = entry.m_SubLane;
                        if (!Paint(source) || EntityManager.HasComponent<Temp>(source)
                            || EntityManager.HasComponent<MGRCurbMarking>(source) || EntityManager.HasComponent<MGRIntersectionPaint>(source)
                            || EntityManager.GetComponentData<Owner>(source).m_Owner != original) continue;
                        if (indexCurves) IndexEmptyAppearance(original, source);
                        if (externalPaint.Contains(EntityManager, source)) continue;
                        roles.Add(new NativePaintRole(original, Canonical(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab),
                            EntityManager.GetComponentData<NetLane>(source)), source);
                        endpointRoles.Add(new NativePaintRole(original, Canonical(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab),
                            Endpoints(EntityManager.GetComponentData<NetLane>(source))), source);
                        if (indexCurves && EntityManager.HasComponent<Curve>(source)
                            && !EntityManager.HasComponent<MGRCurbMarking>(source)
                            && !EntityManager.HasComponent<MGRIntersectionPaint>(source))
                            curveSources.Add(original, source, Canonical(EntityManager.GetComponentData<PrefabRef>(source).m_Prefab),
                                EntityManager.GetComponentData<Curve>(source).m_Bezier);
                    }
                curveSources.CompleteOwner(original);
            }
            var role = new NativePaintRole(original, Canonical(EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab), Normalize(EntityManager.GetComponentData<NetLane>(actor)));
            if (roles.TryUnique(role, out var found)) return found;
            if (roles.Contains(role)) return Entity.Null; // An existing identity is ambiguous, not missing.
            // Native regeneration may renumber the middle path node. Retain
            // appearance when both directed endpoints still identify one lane
            // within this owner/prefab. Parallel candidates remain ambiguous.
            if (endpointRoles.TryUnique(new NativePaintRole(original, role.Prefab, Endpoints(role.Lane)), out found)) return found;
            if (!EntityManager.HasComponent<Curve>(actor)) return Entity.Null;
            var path = EntityManager.GetComponentData<Curve>(actor).m_Bezier;
            if (EntityManager.HasComponent<Curve>(parent) && EntityManager.HasComponent<Curve>(original))
            {
                // Full-edge inverse mapping only. Partial native split sections
                // require their explicit lane identity rather than stretching
                // a fragment over the complete original road.
                if (!NativePreviewSection.TryRange(EntityManager, parent, original, out var range)
                    || range.x != 0 || range.y != 1
                    || !PreviewPaintGeometry.Map(path, EntityManager.GetComponentData<Curve>(parent).m_Bezier,
                        EntityManager.GetComponentData<Curve>(original).m_Bezier, out path)) return Entity.Null;
            }
            return curveSources.Resolve(original, path, Canonical(EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab));
        }
        private bool ParentHidden(Entity parent) => Live(parent) && EntityManager.HasComponent<Temp>(parent)
            && (EntityManager.GetComponentData<Temp>(parent).m_Flags & TempFlags.Hidden) != 0;

        private void Release(Entity actor, ref EntityCommandBuffer commands)
        {
            RemoveReplacement(actor);
            if (!leases.TryGet(actor, out var lease)) return;
            Forget(actor);
            if (!Live(actor) || !EntityManager.HasComponent<Temp>(actor) || !EntityManager.HasComponent<Owner>(actor) || !EntityManager.HasComponent<PrefabRef>(actor)) return;
            var temp = EntityManager.GetComponentData<Temp>(actor);
            var owner = EntityManager.GetComponentData<Owner>(actor).m_Owner;
            if (owner != lease.Owner || temp.m_Original != lease.Original) return;
            var prefab = EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab;
            if (lease.Prefab && prefab == lease.AppliedPrefab)
                writes[actor] = new TemporaryPreviewWrite { Prefab = lease.NativePrefab };
            if (!lease.Hidden || !NativeSignalProjectionPolicy.CanRelease(temp, owner, lease.Original, lease.Owner, lease.Flags, ParentHidden(owner))) return;
            EntityManager.SetComponentData(actor, NativeSignalProjectionPolicy.Release(temp));
            if (!EntityManager.HasComponent<BatchesUpdated>(actor)) commands.AddComponent<BatchesUpdated>(actor);
        }
        private void Project(Entity actor, ref EntityCommandBuffer commands)
        {
            if (!Live(actor) || !EntityManager.HasComponent<Temp>(actor)) { paintOwnerWatch.Remove(actor); RemoveReplacement(actor); Forget(actor); drag?.RemovePreview(actor); return; }
            var temp = EntityManager.GetComponentData<Temp>(actor);
            if (!OriginalOwner(actor, out var parent, out var original)
                || (temp.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) != 0
                || externalPaint.Contains(EntityManager, actor) || externalPaint.Contains(EntityManager, parent))
            { paintOwnerWatch.Remove(actor); drag?.RemovePreview(actor); Release(actor, ref commands); return; }
            if (Paint(actor)) paintOwnerWatch.Set(actor, original, original);
            var source = Resolve(actor, temp, parent, original);
            if (source == Entity.Null && !Live(temp.m_Original) && Paint(actor)) source = EmptyAppearanceSource(actor, original);
            Entity proposed = Entity.Null;
            bool ownedSuppression = leases.TryGet(actor, out var existing) && existing.Source == source
                && existing.Owner == parent && existing.Original == temp.m_Original
                && existing.Hidden && existing.Flags == temp.m_Flags;
            bool visible = ((temp.m_Flags & TempFlags.Hidden) == 0 || ownedSuppression) && !ParentHidden(parent);
            if (!visible) drag?.RemovePreview(actor);
            bool proposal = visible && (source == Entity.Null || !EntityManager.HasComponent<CarLane>(source)
                && !EntityManager.HasComponent<ParkingLane>(source) && !EntityManager.HasComponent<TrackLane>(source))
                && drag?.TryPreview(actor, source, out proposed) == true;
            Entity committedAlias = Entity.Null; bool invisibleCrossing = false;
            bool committed = !proposal && visible && TryCommittedCrossing(actor, parent, ref source, out committedAlias, out invisibleCrossing);
            if (!proposal && !committed && source == Entity.Null || EntityManager.HasComponent<CarLane>(source)
                || EntityManager.HasComponent<ParkingLane>(source) || EntityManager.HasComponent<TrackLane>(source))
            { drag?.RemovePreview(actor); Release(actor, ref commands); return; }
            bool routed = EntityManager.HasComponent<PedestrianLane>(actor);
            bool hidden = !routed && (EntityManager.HasComponent<MGRHiddenJunctionLine>(source)
                || EntityManager.HasComponent<MGRReplacedPaint>(source) || EntityManager.HasComponent<RDCHiddenDecal>(source));
            bool replacementReady = true;
            if (!proposal && visible && !invisibleCrossing) replacementReady = ProjectReplacement(actor, source, parent);
            else RemoveReplacement(actor);
            Entity alias = committed ? committedAlias : proposed;
            if (!proposal && !committed && routed && (EntityManager.GetComponentData<PedestrianLane>(actor).m_Flags & PedestrianLaneFlags.Crosswalk) != 0)
                crossings.TrySuppressedPreviewDefinition(source, out alias);
            if (!replacementReady)
            {
                // Do not suppress the only renderable output while its
                // authoritative replacement is unavailable.
                if (!EntityManager.HasComponent<MGRHiddenJunctionLine>(source) && !EntityManager.HasComponent<RDCHiddenDecal>(source)) hidden = false;
                if (!proposal) alias = Entity.Null;
            }
            bool bound = leases.TryGet(actor, out var old) && old.Owner == parent && old.Original == temp.m_Original && old.Source == source;
            bool ownedHidden = bound && old.Hidden && temp.m_Flags == old.Flags;
            if (!hidden && alias == Entity.Null)
            {
                Release(actor, ref commands);
                // Keep native paint subscribed: its committed hide/replacement
                // can be established later in this same modification pass.
                bool watch = Paint(actor) || EntityManager.HasComponent<RDCHiddenDecal>(source);
                if (!watch && EntityManager.HasComponent<PrefabRef>(source))
                {
                    var definition = EntityManager.GetComponentData<PrefabRef>(source).m_Prefab;
                    watch = EntityManager.HasComponent<LaneDirectionData>(definition) || EntityManager.HasComponent<TrafficSignData>(definition);
                }
                if (watch)
                    leases.Set(actor, source, new Lease { Source = source, Owner = parent, Original = temp.m_Original,
                        NativePrefab = EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab,
                        Flags = EntityManager.GetComponentData<Temp>(actor).m_Flags });
                return;
            }
            if (!ownedHidden && ((temp.m_Flags & TempFlags.Hidden) != 0 || ParentHidden(parent))) return;
            var current = EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab;
            var lease = new Lease { Source = source, Owner = parent, Original = temp.m_Original, Flags = temp.m_Flags,
                NativePrefab = bound && old.Prefab && current == old.AppliedPrefab ? old.NativePrefab : current };
            if (hidden)
            {
                if (!ownedHidden)
                {
                    temp = NativeSignalProjectionPolicy.Suppress(temp); EntityManager.SetComponentData(actor, temp);
                    if (!EntityManager.HasComponent<BatchesUpdated>(actor)) commands.AddComponent<BatchesUpdated>(actor);
                }
                lease.Hidden = true; lease.Flags = temp.m_Flags;
            }
            else if (alias != Entity.Null && TemporaryPreviewWrites.Ready(EntityManager, actor, alias))
            {
                lease.Prefab = lease.NativePrefab != alias; lease.AppliedPrefab = alias;
                TemporaryPreviewWrites.QueueLanePrefab(writes, actor, current, alias);
            }
            lease.Keep = !proposal && !invisibleCrossing && (lease.Hidden || lease.AppliedPrefab != Entity.Null) && EntityManager.HasComponent<MGRReplacedPaint>(source)
                && EntityManager.HasComponent<Curve>(source) && EntityManager.HasComponent<Curve>(actor)
                && MarkingPreviewPolicy.SameCurve(EntityManager.GetComponentData<Curve>(source).m_Bezier,
                    EntityManager.GetComponentData<Curve>(actor).m_Bezier);
            Forget(actor); leases.Set(actor, source, lease);
            if (lease.Keep) { retainedSources.TryGetValue(source, out int count); retainedSources[source] = count + 1; }
        }
        private void Forget(Entity actor)
        {
            if (!leases.TryGet(actor, out var lease)) return;
            leases.Remove(actor);
            if (!lease.Keep || !retainedSources.TryGetValue(lease.Source, out int count)) return;
            if (count == 1) retainedSources.Remove(lease.Source); else retainedSources[lease.Source] = count - 1;
        }
        internal bool KeepsSource(Entity source) => retainedSources.ContainsKey(source);
        protected override void OnUpdate() { }
        private void ProjectBatch()
        {
            roles.Clear(); endpointRoles.Clear(); curveSources.Clear(); emptyAppearance.Clear(); indexed.Clear(); pathOwners.Clear(); actorWork.Clear(); ClearCrossingWork();
            try
            {
                refresh.Drain(EntityManager, actorWork);
                bool nativeEvents = includeActive || !consumedPass || lastPass != nativeBoundary.LastSystemVersion;
                if (nativeEvents)
                    using (var actors = (includeActive ? active : changed).ToEntityArray(Allocator.Temp))
                        foreach (var actor in actors) actorWork.Add(actor);
                foreach (var source in pendingSources)
                {
                    leases.CopyDependents(source, dependents);
                    foreach (var actor in dependents) actorWork.Add(actor);
                }
                ProjectOwners(nativeEvents);
                using (var invalidation = new EntityCommandBuffer(Allocator.Temp))
                {
                    var commands = invalidation;
                    foreach (var actor in actorWork) Project(actor, ref commands);
                    int appearanceWrites = TemporaryPreviewWrites.Apply(World, writes);
                    if (!reportedBatch && (actorWork.Count != 0 || ownerWork.Count != 0))
                    {
                        Mod.Log.Info("Native paint preview first batch: tool=" + tools.activeTool?.toolID + " actors=" + actorWork.Count
                            + " matched=" + leases.Count + " waitingOwners=" + paintOwnerWatch.Count
                            + " appearanceWrites=" + appearanceWrites + " retained=" + retainedSources.Count + " replacementEntries=" + ReplacementPreviewCount);
                        reportedBatch = true;
                    }
                    if (!commands.IsEmpty) commands.Playback(EntityManager);
                }
                if (leases.Count + ownerLeases.Count + paintOwnerWatch.Count >= nextPrune)
                {
                    leases.CopyActors(cleanup);
                    foreach (var actor in cleanup)
                        if (!Live(actor) || !EntityManager.HasComponent<Temp>(actor))
                        { RemoveReplacement(actor); Forget(actor); }
                    cleanup.Clear(); ownerLeases.CopyActors(ownerDependents);
                    foreach (var owner in ownerDependents)
                        if (!Live(owner) || !EntityManager.HasComponent<Temp>(owner))
                        { ownerLeases.Remove(owner); RemoveReplacement(owner); }
                    paintOwnerWatch.CopyActors(cleanup);
                    foreach (var actor in cleanup)
                        if (!Live(actor) || !EntityManager.HasComponent<Temp>(actor)) paintOwnerWatch.Remove(actor);
                    cleanup.Clear();
                    int count = leases.Count + ownerLeases.Count + paintOwnerWatch.Count;
                    nextPrune = count > (int.MaxValue - 256) / 2 ? int.MaxValue : Math.Max(256, count * 2 + 256);
                }
            }
            finally { FlushReplacementRetirements(); ClearOwnerWork(); ClearCrossingWork(); includeActive = false; roles.Clear(); endpointRoles.Clear(); curveSources.Clear(); emptyAppearance.Clear(); indexed.Clear(); pathOwners.Clear(); writes.Clear(); actorWork.Clear(); pendingSources.Clear(); dependents.Clear(); }
        }
        internal void ReleaseAndReset()
        {
            ReleaseReplacements();
            nextPrune = 256;
            refresh.Clear(); refreshOwners.Clear(); awaitRefresh = false;
            ownerLeases.Clear(); paintOwnerWatch.Clear(); ClearOwnerWork(); ClearCrossingWork();
            leases.CopyActors(cleanup);
            try
            {
                if (cleanup.Count == 0) return;
                using (var invalidation = new EntityCommandBuffer(Allocator.Temp))
                {
                    var commands = invalidation;
                    foreach (var actor in cleanup) Release(actor, ref commands);
                    TemporaryPreviewWrites.Apply(World, writes);
                    if (!commands.IsEmpty) commands.Playback(EntityManager);
                }
            }
            finally { leases.Clear(); retainedSources.Clear(); consumedPass = false; roles.Clear(); endpointRoles.Clear(); curveSources.Clear(); indexed.Clear(); pathOwners.Clear(); cleanup.Clear(); writes.Clear(); includeActive = false; actorWork.Clear(); pendingSources.Clear(); dependents.Clear(); }
        }
        internal void RecordSnapshot()
            => Mod.Log.Info("Native paint projection: mode=" + (Mod.Settings?.CurbsidePreviews == true ? "Curbside" : "Vanilla (performance)")
                + " activeBindings=" + leases.Count + " retainedSources=" + retainedSources.Count + " pendingSources=" + pendingSources.Count
                + " ownerBindings=" + ownerLeases.Count + " replacementEntries=" + ReplacementPreviewCount
                + " replacementBuilds=" + ReplacementBuilds + " replacementCacheHits=" + ReplacementCacheHits
                + "; native changed actors and source callbacks only; vehicle/parking/track routing untouched.");

        public void PreSerialize(Context context)
        {
            // Native prefab serialization can enumerate references outside its
            // city-entity query. Release only our temporary asset/pose/flag claims.
            // Reacquire the active Temp set once after saving, not by polling it.
            ReleaseAndReset(); includeActive = Enabled;
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode) { reportedBatch = false; ReleaseAndReset(); base.OnGamePreload(purpose, mode); }
        protected override void OnDestroy() { if (tools != null) tools.EventToolChanged -= ToolChanged; ReleaseAndReset(); base.OnDestroy(); }
    }

    public partial class CrosswalkVisualSystem
    {
        internal bool TrySuppressedPreviewDefinition(Entity source, out Entity prefab)
        {
            prefab = Entity.Null;
            if (!EntityManager.Exists(source) || EntityManager.HasComponent<Deleted>(source)
                || !EntityManager.HasComponent<PrefabRef>(source)) return false;
            var current = EntityManager.GetComponentData<PrefabRef>(source).m_Prefab;
            if (_reverse.ContainsKey(current)) { prefab = current; return true; }
            if (!_sources.Contains(source)) return false;
            return _copies.TryGetValue(current, out prefab) && EntityManager.Exists(prefab);
        }
    }
}
