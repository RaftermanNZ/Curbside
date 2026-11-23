using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Game.Serialization;
using Unity.Collections;
using Unity.Entities;
using SubObject = Game.Objects.SubObject;
using ObjectTransform = Game.Objects.Transform;

namespace MedianGrassReplacement.Systems
{
    // Project committed presentation onto native temporary actors. Native
    // topology and controller masks remain under game ownership. A proved visual
    // source may be linked temporarily for native interpolation, then restored
    // before output/save; common appearance never supplies physical identity.
    [DisableAutoCreation]
    public partial class NativeSignalProjectionSystem : GameSystemBase, IPreSerialize
    {
        internal static bool Supported => typeof(Temp).Module.ModuleVersionId
            == new Guid("46198354-d4f9-4d23-bf32-7e3c085d8b92");
        private struct Lease
        {
            internal Entity Source, Owner, Original;
            internal TempFlags AppliedFlags;
            internal bool OwnsHidden, OwnsPrefab, OwnsPose, OwnsOriginal;
            internal Entity NativePrefab, AppliedPrefab;
            internal ObjectTransform NativePose, AppliedPose;
        }
        private EntityQuery changes, active;
        private bool includeActive, consumedPass;
        private uint lastPass;
        private ModificationBarrier4B nativeBoundary;
        private readonly Dictionary<Entity, TemporaryPreviewWrite> visualWrites = new Dictionary<Entity, TemporaryPreviewWrite>();
        private readonly HashSet<Entity> pendingSources = new HashSet<Entity>();
        private readonly HashSet<Entity> actorWork = new HashSet<Entity>();
        private ToolSystem tools;
        private FarSideLightSystem signals;
        private readonly NativeProjectionRoleIndex<NativeSignalRole, Entity> roles = new NativeProjectionRoleIndex<NativeSignalRole, Entity>();
        private readonly NativeProjectionRoleIndex<NativeSignalPoseRole, Entity> poses = new NativeProjectionRoleIndex<NativeSignalPoseRole, Entity>();
        private struct CommonAppearance { internal Entity Source, Prefab; internal bool Valid, Suppressed; }
        private readonly Dictionary<NativeSignalRole, CommonAppearance> common = new Dictionary<NativeSignalRole, CommonAppearance>();
        private readonly NativeProjectionLeaseIndex<Entity, Entity> ownerLeases = new NativeProjectionLeaseIndex<Entity, Entity>();
        private readonly HashSet<Entity> pendingOwners = new HashSet<Entity>();
        private readonly NativeProjectionLeaseIndex<Entity, Lease> leases = new NativeProjectionLeaseIndex<Entity, Lease>();
        private readonly HashSet<Entity> indexedOwners = new HashSet<Entity>();
        private readonly List<Entity> sourceWork = new List<Entity>();
        private readonly List<Entity> staleWork = new List<Entity>();
        private int nextPrune = 256;
        private bool reportedBatch;
        private int recognizedBatch, blockedBatch;

        protected override void OnCreate()
        {
            base.OnCreate();
            tools = World.GetOrCreateSystemManaged<ToolSystem>();
            signals = World.GetOrCreateSystemManaged<FarSideLightSystem>();
            nativeBoundary = World.GetOrCreateSystemManaged<ModificationBarrier4B>();
            changes = GetEntityQuery(Description(true, ComponentType.ReadOnly<TrafficLight>()),
                Description(true, ComponentType.ReadOnly<Game.Objects.StreetLight>()), Description(true, ComponentType.ReadOnly<ObjectTransform>()));
            active = GetEntityQuery(Description(false, ComponentType.ReadOnly<TrafficLight>()),
                Description(false, ComponentType.ReadOnly<Game.Objects.StreetLight>()), Description(false, ComponentType.ReadOnly<ObjectTransform>()));
            tools.EventToolChanged += ToolChanged;
            Enabled = Supported && Mod.Settings != null && Mod.Settings.CurbsidePreviews && NativeSignalProjectionPolicy.AllowsTool(tools.activeTool?.toolID);
            Mod.Log.Info("Native preview gate: supported=" + Supported + " requested=" + (Mod.Settings?.CurbsidePreviews == true)
                + " tool=" + tools.activeTool?.toolID + "; road/build tools only; bulldozer excluded.");
        }

        private static EntityQueryDesc Description(bool events, ComponentType role) => new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>(), role },
            Any = events ? new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() } : Array.Empty<ComponentType>()
        };

        // Native prefab roles exist before all runtime light components do.
        private bool SignalActor(Entity actor)
        {
            if (!Live(actor) || !EntityManager.HasComponent<PrefabRef>(actor)) return false;
            if (EntityManager.HasComponent<TrafficLight>(actor) || EntityManager.HasComponent<Game.Objects.StreetLight>(actor)) return true;
            var prefab = EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab;
            return EntityManager.HasComponent<TrafficLightData>(prefab) || EntityManager.HasComponent<StreetLightData>(prefab);
        }

        private bool Allowed() => Supported && Mod.Settings?.CurbsidePreviews == true
            && NativeSignalProjectionPolicy.AllowsTool(tools.activeTool?.toolID);

        private void ToolChanged(ToolBaseSystem tool)
        {
            reportedBatch = false;
            // Native ToolClear/Apply owns actors. This discards managed leases;
            // it does not replace bulldozer targeting, highlighting or deletion.
            ReleaseAndReset();
            Enabled = Supported && Mod.Settings != null && Mod.Settings.CurbsidePreviews && NativeSignalProjectionPolicy.AllowsTool(tool?.toolID);
        }

        internal void ModeChanged()
        {
            reportedBatch = false;
            ReleaseAndReset();
            Enabled = Supported && Mod.Settings != null && Mod.Settings.CurbsidePreviews && NativeSignalProjectionPolicy.AllowsTool(tools.activeTool?.toolID);
            includeActive = Enabled;
        }

        internal void ResetOperation()
        {
            leases.Clear(); ownerLeases.Clear(); pendingOwners.Clear(); common.Clear(); roles.Clear(); poses.Clear(); signalPositions.Clear(); signalCandidates.Clear(); indexedOwners.Clear(); sourceWork.Clear();
            staleWork.Clear(); pendingSources.Clear(); actorWork.Clear(); visualWrites.Clear(); includeActive = false; consumedPass = false; nextPrune = 256;
            refresh.Clear(); awaitRefresh = false;
        }

        internal void ReleaseAndReset()
        {
            if (leases.Count == 0) { ResetOperation(); return; }
            try
            {
                leases.CopyActors(staleWork);
                using (var invalidation = new EntityCommandBuffer(Allocator.Temp))
                {
                    var commands = invalidation;
                    foreach (var actor in staleWork) Release(actor, ref commands);
                    int appliedVisuals = TemporaryPreviewWrites.Apply(World, visualWrites);
                    if (ModDiagnostics.TraceBuffer.Enabled) metrics.AppearanceWrites += appliedVisuals;
                    if (!commands.IsEmpty) commands.Playback(EntityManager);
                }
            }
            finally { ResetOperation(); }
        }

        internal int ActiveLeases => leases.Count;

        internal void RunPending() { if (pendingSources.Count != 0 || pendingOwners.Count != 0 || refresh.Count != 0) RunIfWork(); }

        internal void RunIfWork()
        {
            if (!Enabled || !Allowed() || Mod.Saving) return;
            uint pass = nativeBoundary.LastSystemVersion;
            // Never reapply between our release and native Clear/Apply playback.
            if (awaitRefresh && pass == refreshPass) return;
            awaitRefresh = false;
            if (consumedPass && lastPass == pass && !includeActive && pendingSources.Count == 0 && pendingOwners.Count == 0 && refresh.Count == 0) return;
            if (!includeActive && pendingSources.Count == 0 && pendingOwners.Count == 0 && refresh.Count == 0 && changes.IsEmptyIgnoreFilter) return;
            ProjectBatch(); lastPass = pass; consumedPass = true;
        }

        public void PreSerialize(Context context)
        {
            // Native prefab serialization can enumerate references outside its
            // city-entity query. Release only our temporary asset/pose/flag claims.
            // Reacquire the active Temp set once after saving, not by polling it.
            ReleaseAndReset(); includeActive = Enabled;
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        { reportedBatch = false; ResetOperation(); base.OnGamePreload(purpose, mode); }
        protected override void OnDestroy()
        {
            if (tools != null) tools.EventToolChanged -= ToolChanged;
            ResetOperation();
            base.OnDestroy();
        }

        private bool Live(Entity entity) => EntityManager.Exists(entity) && !EntityManager.HasComponent<Deleted>(entity);
        private bool TryParent(Entity actor, out Entity parent, out Entity original)
        {
            parent = original = Entity.Null;
            if (!EntityManager.HasComponent<Owner>(actor)) return false;
            parent = EntityManager.GetComponentData<Owner>(actor).m_Owner;
            if (!Live(parent)) return false;
            original = EntityManager.HasComponent<Temp>(parent)
                ? EntityManager.GetComponentData<Temp>(parent).m_Original : parent;
            return Live(original) && !EntityManager.HasComponent<Temp>(original);
        }
        private bool TryRole(Entity actor, Entity originalOwner, bool committed, out NativeSignalRole role)
        {
            role = default;
            if (!SignalActor(actor)
                || !EntityManager.HasComponent<PrefabRef>(actor) || !EntityManager.HasComponent<Owner>(actor)) return false;
            if (committed && (EntityManager.HasComponent<Temp>(actor)
                || EntityManager.GetComponentData<Owner>(actor).m_Owner != originalOwner)) return false;
            bool secondary = EntityManager.HasComponent<Secondary>(actor);
            int slot = secondary ? -1 : EntityManager.HasComponent<Aligned>(actor)
                ? EntityManager.GetComponentData<Aligned>(actor).m_SubObjectIndex : -1;
            // Direct native originals and unique roles remain valid without Aligned.
            // Native generated network signals do not promise this optional component.
            ushort vehicle, pedestrian;
            if (committed)
            { if (!signals.TryPreviewMasks(actor, out vehicle, out pedestrian)) return false; }
            else { var light = EntityManager.HasComponent<TrafficLight>(actor) ? EntityManager.GetComponentData<TrafficLight>(actor) : default; vehicle = light.m_GroupMask0; pedestrian = light.m_GroupMask1; }
            role = new NativeSignalRole(originalOwner,
                GardenCityLightPolicy.NativeSource(EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab),
                slot, vehicle, pedestrian, secondary);
            return true;
        }

        private Entity Resolve(Entity actor, Temp temp, Entity originalOwner, out bool commonOnly)
        {
            commonOnly = false;
            Entity nativeOriginal = Entity.Null;
            if (Live(temp.m_Original))
            {
                // Native physical identity is stronger than phase masks/Aligned:
                // those can legitimately change while the native tool previews
                // an edited controller. Do not write either of those values.
                var directSource = temp.m_Original;
                bool match = !EntityManager.HasComponent<Temp>(directSource)
                    && EntityManager.HasComponent<Owner>(directSource)
                    && EntityManager.GetComponentData<Owner>(directSource).m_Owner == originalOwner
                    && EntityManager.HasComponent<PrefabRef>(directSource)
                    && SignalActor(directSource)
                    && EntityManager.HasComponent<Secondary>(directSource) == EntityManager.HasComponent<Secondary>(actor)
                    && GardenCityLightPolicy.NativeSource(EntityManager.GetComponentData<PrefabRef>(directSource).m_Prefab)
                        == GardenCityLightPolicy.NativeSource(EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab);
                if (!match) { if (measureBatch) metrics.Unresolved++; return Entity.Null; }
                // Recover a customized secondary against its recorded
                // native pose within this owner, rejecting ties. A shared appearance alone
                // must never override an existing native physical identity.
                if (!EntityManager.HasComponent<Secondary>(actor)
                    || !EntityManager.HasComponent<ObjectTransform>(actor)
                    || signals.IsSuppressedForNativeProjection(directSource)
                    || signals.TryPreviewVisual(directSource, out _, out _, out _))
                { if (measureBatch) metrics.DirectLinks++; return directSource; }
                nativeOriginal = directSource;
            }
            if (!TryRole(actor, originalOwner, false, out var role)) return Entity.Null;
            if (indexedOwners.Add(originalOwner) && EntityManager.HasBuffer<SubObject>(originalOwner))
            {
                if (measureBatch) metrics.OwnersIndexed++;
                // No structural changes or borrowed buffer escapes this scope.
                foreach (var child in EntityManager.GetBuffer<SubObject>(originalOwner, true))
                {
                    if (measureBatch) metrics.DependenciesRead++;
                    var candidate = child.m_SubObject;
                    if (!TryRole(candidate, originalOwner, true, out var key)) continue;
                    roles.Add(key, candidate);
                    bool hidden = signals.IsSuppressedForNativeProjection(candidate);
                    bool visual = signals.TryPreviewVisual(candidate, out var target, out var recordedNative, out _);
                    bool valid = hidden || visual;
                    if (common.TryGetValue(key, out var group))
                    {
                        group.Valid &= valid && group.Suppressed == hidden && (hidden || group.Prefab == target);
                        common[key] = group;
                    }
                    else common[key] = new CommonAppearance { Source = candidate, Prefab = target, Suppressed = hidden, Valid = valid };
                    if (EntityManager.HasComponent<ObjectTransform>(candidate))
                    {
                        var nativePose = visual ? recordedNative : EntityManager.GetComponentData<ObjectTransform>(candidate);
                        poses.Add(new NativeSignalPoseRole(key, nativePose), candidate);
                        signalPositions.Add(key, nativePose.m_Position, new NativeSignalCandidate { Source = candidate, Pose = nativePose });
                    }
                }
            }
            bool unique = roles.TryUnique(role, out var source);
            if (!unique && EntityManager.HasComponent<ObjectTransform>(actor))
            {
                var nativePose = EntityManager.GetComponentData<ObjectTransform>(actor);
                if (leases.TryGet(actor, out var lease) && lease.Original == temp.m_Original
                    && lease.Owner == EntityManager.GetComponentData<Owner>(actor).m_Owner
                    && lease.OwnsPose && nativePose.Equals(lease.AppliedPose)) nativePose = lease.NativePose;
                unique = poses.TryUnique(new NativeSignalPoseRole(role, nativePose), out source);
            }
            if (!unique && EntityManager.HasComponent<ObjectTransform>(actor))
            {
                var proposed = EntityManager.GetComponentData<ObjectTransform>(actor);
                if (leases.TryGet(actor, out var prior) && prior.Original == temp.m_Original
                    && prior.Owner == EntityManager.GetComponentData<Owner>(actor).m_Owner
                    && prior.OwnsPose && proposed.Equals(prior.AppliedPose)) proposed = prior.NativePose;
                // Resolve physical placement before appearance consensus. Native
                // regeneration can perturb a pose without changing which pole it
                // represents; model-only consensus would put it back near-side.
                unique = TrySpatialSource(role, proposed, out source, out _);
            }
            if (!unique && common.TryGetValue(role, out var shared) && shared.Valid)
            {
                // Every authoritative member agrees on appearance. Preserve that
                // model on native proposed geometry without inventing physical
                // identity or assigning one member's position to another.
                source = shared.Source; unique = true; commonOnly = true;
            }
            if (nativeOriginal != Entity.Null)
            {
                var proposed = EntityManager.GetComponentData<ObjectTransform>(actor);
                if (leases.TryGet(actor, out var previous) && previous.Original == temp.m_Original
                    && previous.Owner == EntityManager.GetComponentData<Owner>(actor).m_Owner
                    && previous.OwnsPose && proposed.Equals(previous.AppliedPose)) proposed = previous.NativePose;
                bool recover = TrySpatialSource(role, proposed, out source, out float recoveredScore)
                    && source != nativeOriginal && signals.TryPreviewVisual(source, out _, out _, out _)
                    && EntityManager.HasComponent<ObjectTransform>(nativeOriginal)
                    && SignalPreviewIdentityPolicy.Better(recoveredScore, Unity.Mathematics.math.distancesq(
                        EntityManager.GetComponentData<ObjectTransform>(nativeOriginal).m_Position, proposed.m_Position));
                if (!recover) { if (measureBatch) metrics.DirectLinks++; return nativeOriginal; }
                unique = true; commonOnly = false;
            }
            if (measureBatch) { if (unique) metrics.FallbackLinks++; else metrics.Unresolved++; }
            return unique ? source : Entity.Null;
        }

        private bool ParentHidden(Entity parent) => EntityManager.HasComponent<Temp>(parent)
            && (EntityManager.GetComponentData<Temp>(parent).m_Flags & TempFlags.Hidden) != 0;

        private void Release(Entity actor, ref EntityCommandBuffer invalidation)
        {
            if (!leases.TryGet(actor, out var lease)) return;
            leases.Remove(actor);
            ownerLeases.Remove(actor);
            if (!Live(actor) || !EntityManager.HasComponent<Temp>(actor) || !EntityManager.HasComponent<Owner>(actor)) return;
            var current = EntityManager.GetComponentData<Temp>(actor);
            var owner = EntityManager.GetComponentData<Owner>(actor).m_Owner;
            if (lease.OwnsOriginal && current.m_Original == lease.Source) current.m_Original = lease.Original;
            if (owner != lease.Owner || current.m_Original != lease.Original) return;
            QueueAppearanceRelease(actor, lease);
            if (!lease.OwnsHidden || !NativeSignalProjectionPolicy.CanRelease(current, owner, lease.Original,
                lease.Owner, lease.AppliedFlags, ParentHidden(owner))) return;
            EntityManager.SetComponentData(actor, NativeSignalProjectionPolicy.Release(current));
            if (measureBatch) metrics.Releases++;
            if (!EntityManager.HasComponent<BatchesUpdated>(actor)) invalidation.AddComponent<BatchesUpdated>(actor);
        }

        private void QueueAppearanceRelease(Entity actor, Lease lease)
        {
            if (!EntityManager.HasComponent<PrefabRef>(actor)) return;
            var prefab = EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab;
            bool restorePrefab = lease.OwnsPrefab && prefab == lease.AppliedPrefab;
            bool restorePose = lease.OwnsPose && EntityManager.HasComponent<ObjectTransform>(actor)
                && EntityManager.GetComponentData<ObjectTransform>(actor).Equals(lease.AppliedPose);
            bool restoreOriginal = lease.OwnsOriginal && EntityManager.HasComponent<Temp>(actor)
                && EntityManager.GetComponentData<Temp>(actor).m_Original == lease.Source;
            if (restorePrefab || restorePose || restoreOriginal)
                visualWrites[actor] = new TemporaryPreviewWrite { Prefab = restorePrefab ? lease.NativePrefab : prefab,
                    HasPose = restorePose, Pose = lease.NativePose, HasOriginal = restoreOriginal, Original = lease.Original };
        }

        private void Project(Entity actor, ref EntityCommandBuffer invalidation)
        {
            if (measureBatch) metrics.ActorsVisited++;
            if (!Live(actor) || !EntityManager.HasComponent<Temp>(actor)) { leases.Remove(actor); ownerLeases.Remove(actor); return; }
            if (!SignalActor(actor)) { Release(actor, ref invalidation); return; }
            recognizedBatch++;
            var temp = EntityManager.GetComponentData<Temp>(actor);
            if (!TryParent(actor, out var parent, out var originalOwner)) { Release(actor, ref invalidation); return; }
            if ((temp.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) != 0) { Release(actor, ref invalidation); return; }
            var actualOriginal = temp.m_Original;
            if (leases.TryGet(actor, out var linked) && linked.Owner == parent && linked.OwnsOriginal
                && actualOriginal == linked.Source) temp.m_Original = linked.Original;
            var source = Resolve(actor, temp, originalOwner, out bool commonOnly);
            if (source == Entity.Null)
            {
                Release(actor, ref invalidation);
                ownerLeases.Set(actor, originalOwner, originalOwner);
                return;
            }
            bool bound = leases.TryGet(actor, out var old) && old.Owner == parent && old.Original == temp.m_Original;
            bool parentHidden = ParentHidden(parent);
            bool suppressed = signals.IsSuppressedForNativeProjection(source);
            bool ownHidden = bound && old.OwnsHidden && temp.m_Flags == old.AppliedFlags;
            if (!suppressed && ownHidden && NativeSignalProjectionPolicy.CanRelease(temp, parent, old.Original, old.Owner, old.AppliedFlags, parentHidden))
            {
                temp = NativeSignalProjectionPolicy.Release(temp); EntityManager.SetComponentData(actor, temp);
                ownHidden = false; if (measureBatch) metrics.Releases++;
                if (!EntityManager.HasComponent<BatchesUpdated>(actor)) invalidation.AddComponent<BatchesUpdated>(actor);
            }
            if (suppressed && !ownHidden && NativeSignalProjectionPolicy.Eligible(temp.m_Flags) && !parentHidden)
            {
                temp = NativeSignalProjectionPolicy.Suppress(temp); EntityManager.SetComponentData(actor, temp);
                ownHidden = true; if (measureBatch) metrics.Writes++;
                if (!EntityManager.HasComponent<BatchesUpdated>(actor)) invalidation.AddComponent<BatchesUpdated>(actor);
            }
            var currentPrefab = EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab;
            bool hasPose = EntityManager.HasComponent<ObjectTransform>(actor);
            var pose = hasPose ? EntityManager.GetComponentData<ObjectTransform>(actor) : default;
            var lease = new Lease { Source = source, Owner = parent, Original = temp.m_Original,
                AppliedFlags = temp.m_Flags, OwnsHidden = ownHidden,
                NativePrefab = bound && old.OwnsPrefab && currentPrefab == old.AppliedPrefab ? old.NativePrefab : currentPrefab,
                NativePose = bound && old.OwnsPose && pose.Equals(old.AppliedPose) ? old.NativePose : pose };
            Entity target = Entity.Null; ObjectTransform applied = default;
            bool appearance = !suppressed && !parentHidden && (temp.m_Flags & TempFlags.Hidden) == 0
                && signals.TryPreviewVisual(source, out target, out _, out applied);
            if (commonOnly) applied = lease.NativePose;
            bool ready = appearance && TemporaryPreviewWrites.Ready(EntityManager, actor, target);
            if (appearance && !ready) blockedBatch++;
            if (ready)
            {
                lease.AppliedPrefab = target; lease.AppliedPose = applied;
                lease.OwnsPrefab = lease.NativePrefab != target;
                lease.OwnsPose = hasPose && !lease.NativePose.Equals(applied);
                // ObjectInterpolateSystem/RelativeObjectSystem can consume the
                // original instead of this actor's pose. A one-time exact source
                // handoff lets native rendering follow the committed placement.
                var desiredOriginal = commonOnly ? temp.m_Original : source;
                lease.OwnsOriginal = desiredOriginal != temp.m_Original;
                if (currentPrefab != target || hasPose && !pose.Equals(applied) || actualOriginal != desiredOriginal)
                    visualWrites[actor] = new TemporaryPreviewWrite { Prefab = target, HasPose = hasPose, Pose = applied,
                        HasOriginal = actualOriginal != desiredOriginal, Original = desiredOriginal };
            }
            else if (bound) QueueAppearanceRelease(actor, old);
            leases.Set(actor, source, lease);
            ownerLeases.Set(actor, originalOwner, originalOwner);
        }

        // O(1) expected enqueue at actual committed mutations. Coalesce before
        // resolving dependents; no native Updated tag or cursor poll is generated.
        internal static void SourceChanged(World world, Entity source)
        {
            var system = world?.GetExistingSystemManaged<NativeSignalProjectionSystem>();
            if (system == null || !system.Enabled || !system.Allowed() || Mod.Saving || system.ownerLeases.Count == 0) return;
            bool added = system.pendingSources.Add(source);
            if (system.EntityManager.HasComponent<Owner>(source))
                system.pendingOwners.Add(system.EntityManager.GetComponentData<Owner>(source).m_Owner);
            if (ModDiagnostics.TraceBuffer.Enabled)
            { system.metrics.SourceEvents++; if (!added) system.metrics.SourceCoalesced++; }
        }

        // Invoked by the existing lifecycle owner; never nest ECS Update(),
        // which would change its caller's change-version context.
        protected override void OnUpdate() { }
        private void ProjectBatch()
        {
            if (Mod.Saving) return;
            measureBatch = ModDiagnostics.TraceBuffer.Enabled;
            long started = measureBatch ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            if (measureBatch) metrics.Batches++;
            roles.Clear(); poses.Clear(); signalPositions.Clear(); signalCandidates.Clear(); common.Clear(); indexedOwners.Clear();
            recognizedBatch = blockedBatch = 0;
            try
            {
                using (var invalidation = new EntityCommandBuffer(Allocator.Temp))
                {
                    var commands = invalidation;
                    actorWork.Clear();
                    refresh.Drain(EntityManager, actorWork);
                    bool nativeEvents = includeActive || !consumedPass || lastPass != nativeBoundary.LastSystemVersion;
                    if (nativeEvents)
                        using (var actors = (includeActive ? active : changes).ToEntityArray(Allocator.Temp))
                        {
                            if (measureBatch) metrics.NativeActors += actors.Length;
                            foreach (var actor in actors) actorWork.Add(actor);
                        }
                    foreach (var source in pendingSources)
                    {
                        leases.CopyDependents(source, sourceWork);
                        foreach (var actor in sourceWork) actorWork.Add(actor);
                    }
                    foreach (var owner in pendingOwners)
                    {
                        ownerLeases.CopyDependents(owner, sourceWork);
                        foreach (var actor in sourceWork) actorWork.Add(actor);
                    }
                    foreach (var actor in actorWork) Project(actor, ref commands);
                    int appliedVisuals = TemporaryPreviewWrites.Apply(World, visualWrites);
                    if (!reportedBatch && recognizedBatch != 0)
                    {
                        Mod.Log.Info("Native signal preview first batch: tool=" + tools.activeTool?.toolID + " signals=" + recognizedBatch
                            + " matched=" + leases.Count + " appearanceWrites=" + appliedVisuals + " resourceBlocked=" + blockedBatch);
                        reportedBatch = true;
                    }
                    if (ModDiagnostics.TraceBuffer.Enabled) metrics.AppearanceWrites += appliedVisuals;
                    if (!commands.IsEmpty) commands.Playback(EntityManager);
                }
                // Foreign immediate destruction can bypass native Deleted tags.
                // Prune only under geometric insertion pressure, not on a timer
                // or every update. Successful native deletions remove directly.
                if (ownerLeases.Count >= nextPrune)
                {
                    ownerLeases.CopyActors(staleWork);
                    foreach (var actor in staleWork)
                        if (!Live(actor) || !EntityManager.HasComponent<Temp>(actor))
                        { leases.Remove(actor); ownerLeases.Remove(actor); }
                    staleWork.Clear();
                    nextPrune = ownerLeases.Count > (int.MaxValue - 256) / 2
                        ? int.MaxValue : Math.Max(256, ownerLeases.Count * 2 + 256);
                }
            }
            finally
            {
                roles.Clear(); poses.Clear(); signalPositions.Clear(); signalCandidates.Clear(); common.Clear(); indexedOwners.Clear(); actorWork.Clear(); sourceWork.Clear(); pendingSources.Clear(); pendingOwners.Clear();
                if (measureBatch)
                {
                    long ticks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
                    metrics.ElapsedTicks += ticks; if (ticks > metrics.PeakTicks) metrics.PeakTicks = ticks;
                }
                measureBatch = false; includeActive = false; visualWrites.Clear();
            }
        }
    }

    // Runs only in native apply/clear phases. Discard bookkeeping; native Temp
    // removal/deletion owns actors and the render-only bit cannot persist.
    public partial class NativeProjectionBoundarySystem : GameSystemBase
    {
        protected override void OnUpdate()
            {
            World.GetExistingSystemManaged<NativeApproachStopPreviewSystem>()?.Restore();
            World.GetExistingSystemManaged<NativeSignalProjectionSystem>()?.BeforeNativeOutput();
            World.GetExistingSystemManaged<NativePaintProjectionSystem>()?.BeforeNativeOutput();
            World.GetExistingSystemManaged<NativeCrosswalkDragSystem>()?.BeforeNativeOutput();
        }
    }
}
