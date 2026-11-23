using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Serialization;
using Game.Tools;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using SubLane = Game.Net.SubLane;
using CarLane = Game.Net.CarLane;
using ParkingLane = Game.Net.ParkingLane;
using TrackLane = Game.Net.TrackLane;

namespace MedianGrassReplacement.Systems
{
    // Runtime ownership only. No committed prefab, geometry or saved choice.
    public struct MGRTemporaryPaintHidden : IComponentData { }

    internal struct TemporaryPaintVisibilityChange
    {
        internal Entity Actor;
        internal bool Suppress;
    }

    internal static class TemporaryPaintVisibilityPolicy
    {
        internal static bool Active(TempFlags flags, bool owned, bool ownerHidden)
            => (flags & (TempFlags.Delete | TempFlags.Cancel)) == 0
                && ((flags & TempFlags.Hidden) == 0 || owned && !ownerHidden);

        internal static Temp Apply(Temp current, bool suppress, bool ownerHidden)
        {
            // Preserve every other native flag and the latest original link.
            if (suppress) current.m_Flags |= TempFlags.Hidden;
            // Native validation owns Hidden on a cancelled preview as well.
            else if (!ownerHidden && (current.m_Flags & TempFlags.Cancel) == 0) current.m_Flags &= ~TempFlags.Hidden;
            return current;
        }
    }

    [BurstCompile]
    internal struct ApplyTemporaryPaintVisibilityJob : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<TemporaryPaintVisibilityChange> Changes;
        public ComponentLookup<Temp> Temps;
        [ReadOnly] public ComponentLookup<Owner> Owners;
        [ReadOnly] public ComponentLookup<Deleted> Deleted;
        [ReadOnly] public ComponentLookup<MGRTemporaryPaintHidden> Masks;

        public void Execute()
        {
            foreach (var change in Changes)
            {
                if (!Temps.TryGetComponent(change.Actor, out var current)) continue;
                if (change.Suppress && (Deleted.HasComponent(change.Actor)
                    || !Masks.HasComponent(change.Actor)
                    || (current.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) != 0)) continue;
                bool ownerHidden = Owners.TryGetComponent(change.Actor, out var owner)
                    && Temps.TryGetComponent(owner.m_Owner, out var parent)
                    && (parent.m_Flags & TempFlags.Hidden) != 0;
                var next = TemporaryPaintVisibilityPolicy.Apply(current, change.Suppress, ownerHidden);
                if (next.m_Flags != current.m_Flags) Temps[change.Actor] = next;
            }
        }
    }

    public partial class TemporaryPaintVisibilitySystem : GameSystemBase, IPreSerialize
    {
        private EntityQuery marked, escaped;
        private readonly Dictionary<Entity, bool> requests = new Dictionary<Entity, bool>();

        protected override void OnCreate()
        {
            base.OnCreate();
            marked = GetEntityQuery(ComponentType.ReadOnly<MGRTemporaryPaintHidden>());
            escaped = GetEntityQuery(ComponentType.ReadOnly<MGRTemporaryPaintHidden>(), ComponentType.Exclude<Temp>());
            // Requests can precede the first marker, so RequireForUpdate(marked)
            // would drop first acquisition. Empty callbacks only check counts.
        }

        private bool OwnerHidden(Entity actor)
        {
            var manager = EntityManager;
            if (!manager.HasComponent<Owner>(actor)) return false;
            var owner = manager.GetComponentData<Owner>(actor).m_Owner;
            return manager.Exists(owner) && manager.HasComponent<Temp>(owner)
                && (manager.GetComponentData<Temp>(owner).m_Flags & TempFlags.Hidden) != 0;
        }

        internal bool IsActive(Entity actor, TempFlags flags)
        {
            // Most actors and all native visible refreshes need no owner read.
            if ((flags & TempFlags.Hidden) == 0) return PreviewVisualPolicy.Active(flags);
            return TemporaryPaintVisibilityPolicy.Active(flags,
                EntityManager.HasComponent<MGRTemporaryPaintHidden>(actor), OwnerHidden(actor));
        }

        internal bool TrySuppress(Entity actor)
        { return false; }

        internal bool Owns(Entity actor)
            => requests.TryGetValue(actor, out bool pending) ? pending
                : EntityManager.Exists(actor) && EntityManager.HasComponent<MGRTemporaryPaintHidden>(actor);

        internal void Release(Entity actor) {  }

        protected override void OnUpdate()
        { // Native-preview-only isolation test.
         }

        internal void RestoreAll()
        {
            // Apply/clear/save is a real lifetime boundary, not a hover refresh.
            Dependency.Complete();
            requests.Clear();
            if (marked.IsEmptyIgnoreFilter) return;
            var manager = EntityManager;
            using (var actors = marked.ToEntityArray(Allocator.Temp))
            using (var commands = new EntityCommandBuffer(Allocator.Temp))
            {
                foreach (var actor in actors)
                {
                    if (manager.HasComponent<Temp>(actor))
                    {
                        var current = manager.GetComponentData<Temp>(actor);
                        var next = TemporaryPaintVisibilityPolicy.Apply(current, false, OwnerHidden(actor));
                        if (next.m_Flags != current.m_Flags)
                        {
                            commands.SetComponent(actor, next);
                            if (!manager.HasComponent<Deleted>(actor) && !manager.HasComponent<BatchesUpdated>(actor))
                                commands.AddComponent<BatchesUpdated>(actor);
                        }
                    }
                    commands.RemoveComponent<MGRTemporaryPaintHidden>(actor);
                }
                NativeVisualInvalidation.CompleteReaders(World);
                commands.Playback(manager);
            }
        }

        public void PreSerialize(Context context) { RestoreAll(); }
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        { RestoreAll(); base.OnGamePreload(purpose, mode); }
        protected override void OnDestroy() { RestoreAll(); base.OnDestroy(); }
    }

    // Apply already uses SignalPreviewRestoreSystem before ToolApplySystem.
    // Clear is a separate native phase; retire our actors before it as well.
    public partial class PaintPreviewClearBoundarySystem : GameSystemBase
    {
        protected override void OnUpdate()
        { // Native-preview-only isolation test.
         }
    }

    public partial class PreviewPaintInheritanceSystem
    {
        private TemporaryPaintVisibilitySystem visibility;

        private void HideNativePreview(Entity lane, Entity current, Entity native)
        {
            aliases[lane] = native;
            if (visibility.TrySuppress(lane))
            {
                entryOwners.Remove(lane);
                TemporaryPreviewWrites.QueueLanePrefab(writes, lane, current, native);
            }
            else
            {
                if (entries.TryGetValue(lane, out var entry)) entryOwners.Track(lane, entry.Owner, entry.SourceOwner);
                TemporaryPreviewWrites.QueueLanePrefab(writes, lane, current, HiddenDefinition(native));
            }
        }

        private void FlushPreviewWrites()
        { TemporaryPreviewWrites.Apply(World, writes); }
    }

    public partial class CrosswalkAppearancePreviewSystem
    {
        private TemporaryPaintVisibilitySystem visibility;

        private void HideNativePreview(Entity lane, Entity current, Entity native)
        {
            aliases[lane] = native;
            if (visibility.TrySuppress(lane))
            {
                aliasOwners.Remove(lane);
                TemporaryPreviewWrites.QueueLanePrefab(writes, lane, current, native);
            }
            else
            {
                if (bindings.TryGetValue(lane, out var binding))
                    aliasOwners.Track(lane, EntityManager.GetComponentData<Owner>(lane).m_Owner, binding.Road, binding.Node);
                TemporaryPreviewWrites.QueueLanePrefab(writes, lane, current, HiddenDefinition(native));
            }
        }

        private void FlushPreviewWrites()
        { TemporaryPreviewWrites.Apply(World, writes); }
    }
}
