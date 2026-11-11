using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Transform = Game.Objects.Transform;

namespace MedianGrassReplacement.Systems
{
    public struct MGRPrivateVisualPrefab : IComponentData { }

    // These queues belong to one world. Check versions and expected values so
    // an old request cannot overwrite a replacement made by another system.
    public partial class VisualLifecycleSystem : GameSystemBase
    {
        private sealed class Change
        {
            internal bool PrefabChanged, PoseChanged, HadOwner, HadPrefab, Marked, RestoreRelative;
            internal Entity OriginalPrefab, Prefab, Owner;
            internal Transform OriginalPose, Pose;
            internal Game.Objects.Relative Relative;
        }

        private readonly Dictionary<Entity, Change> changes = new Dictionary<Entity, Change>();
        private readonly HashSet<Entity> retire = new HashSet<Entity>();
        private readonly Dictionary<Entity, long> definitions = new Dictionary<Entity, long>();
        private readonly Dictionary<Entity, Entity> definitionSources = new Dictionary<Entity, Entity>();
        private EntityQuery references;
        internal bool BeforeCulling { get; private set; }
        internal long Frame { get; private set; }
        internal int PendingCount => changes.Count + retire.Count + definitions.Count;
        internal bool ShutdownRequested { get; set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            references = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<PrefabRef>() }, Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab });
        }

        private Change GetChange(Entity entity)
        {
            if (!changes.TryGetValue(entity, out var change))
            {
                change = new Change { HadOwner = EntityManager.HasComponent<Owner>(entity), HadPrefab = EntityManager.HasComponent<PrefabRef>(entity) };
                if (change.HadOwner) change.Owner = EntityManager.GetComponentData<Owner>(entity).m_Owner;
                if (change.HadPrefab) change.OriginalPrefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                changes.Add(entity, change);
            }
            return change;
        }

        internal void SetPrefab(Entity entity, Entity prefab)
        {
            if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<PrefabRef>(entity)) return;
            if (BeforeCulling || Mod.Saving) { NativeVisualInvalidation.ApplyPrefab(World, entity, prefab); return; }
            if (EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab == prefab)
            {
                if (changes.TryGetValue(entity, out var pending))
                {
                    pending.PrefabChanged = false;
                    if (!pending.PoseChanged && !pending.Marked && !pending.RestoreRelative)
                        changes.Remove(entity);
                }
                return;
            }
            var change = GetChange(entity);
            change.PrefabChanged = true;
            change.Prefab = prefab;
        }

        internal void SetTransform(Entity entity, Transform transform)
        {
            if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<Transform>(entity)) return;
            if (BeforeCulling || Mod.Saving) { NativeVisualInvalidation.ApplyTransform(World, entity, transform); return; }
            var current = EntityManager.GetComponentData<Transform>(entity);
            if (current.Equals(transform))
            {
                // A request to return to the current pose cancels an older
                // queued pose, while retaining independent prefab/mark work.
                if (changes.TryGetValue(entity, out var pending))
                {
                    pending.PoseChanged = false;
                    if (!pending.PrefabChanged && !pending.Marked && !pending.RestoreRelative)
                        changes.Remove(entity);
                }
                return;
            }
            var change = GetChange(entity);
            if (!change.PoseChanged) change.OriginalPose = current;
            change.PoseChanged = true;
            change.Pose = transform;
        }

        internal void Retire(Entity entity)
        {
            if (BeforeCulling || Mod.Saving) GeneratedVisualLifecycle.RetireNow(World, entity);
            else retire.Add(entity);
        }

        internal void Mark(Entity entity)
        {
            if (!EntityManager.Exists(entity)) return;
            if (BeforeCulling || Mod.Saving) NativeVisualInvalidation.ApplyMark(EntityManager, entity);
            else GetChange(entity).Marked = true;
        }

        internal void RestoreRelative(Entity entity, Game.Objects.Relative relative)
        {
            if (!EntityManager.Exists(entity)) return;
            if (BeforeCulling || Mod.Saving) { NativeVisualInvalidation.ApplyRelative(World, entity, relative); return; }
            var change = GetChange(entity);
            change.RestoreRelative = true;
            change.Relative = relative;
        }

        internal void RetireDefinition(Entity entity, Entity original)
        {
            if (entity == Entity.Null) return;
            TrackDefinition(entity, original, "retirement-only");
            definitions[entity] = Frame + (BeforeCulling ? 0 : 1);
            definitionSources[entity] = original;
            if (!definitionOrigins.HasDependents(entity)) readyDefinitions.Add(entity);
            definitionRepairPending = true;
        }

        internal void RepairDefinitionReferences()
        {
            if (definitions.Count == 0 || !definitionRepairPending && readyDefinitions.Count == 0) return;
            NativeVisualInvalidation.CompleteReaders(World);
            using (var entities = references.ToEntityArray(Allocator.Temp)) foreach (var entity in entities)
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                if (definitionSources.ContainsKey(prefab) && OriginalDefinition(prefab, out var original))
                {
                    if (EntityManager.HasComponent<Deleted>(entity)) continue;
                    NativeVisualInvalidation.ApplyPrefab(World, entity, original);
                }
            }
            definitionRepairPending = false;
        }

        internal void CloseMutations()
        {
            RepairDefinitionReferences();
            BeforeCulling = false;
        }

        // Test166 serializes without the supplied build's staged save frames.
        // Private copies can inherit LoadedIndex even with PrefabData disabled.
        // Restore every reference, including deleted actors, and remove those
        // copies before BeginPrefabSerialization builds its native index array.
        internal void PrepareForSerialization()
        {
            FlushPending();
            if (definitions.Count == 0) return;
            NativeVisualInvalidation.CompleteReaders(World);
            PrepareDefinitionReferences();

            foreach (var entity in definitions.Keys)
            {
                if (EntityManager.Exists(entity)) EntityManager.DestroyEntity(entity);
                definitionOrigins.ReleaseDestroyed(entity, DefinitionExists, DefinitionDependencyReleased);
            }
            definitionOrigins.SweepDestroyed(DefinitionExists, DefinitionDependencyReleased);
            definitions.Clear();
            definitionSources.Clear();
            readyDefinitions.Clear(); definitionRepairPending = false;
        }

        protected override void OnUpdate()
        {
            Frame++;
            BeforeCulling = true;
            FlushPending();
        }

        // Serialization/disposal cannot wait for the next MainLoop update.
        internal void FlushPending()
        {
            if (changes.Count != 0 || retire.Count != 0) NativeVisualInvalidation.CompleteReaders(World);
            foreach (var entity in retire) GeneratedVisualLifecycle.RetireNow(World, entity);
            retire.Clear();
            foreach (var pair in changes)
            {
                var entity = pair.Key;
                var change = pair.Value;
                if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity)) continue;
                if (EntityManager.HasComponent<Owner>(entity) != change.HadOwner || (change.HadOwner && EntityManager.GetComponentData<Owner>(entity).m_Owner != change.Owner)) continue;
                if (EntityManager.HasComponent<PrefabRef>(entity) != change.HadPrefab || (change.HadPrefab && EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab != change.OriginalPrefab)) continue;
                if (change.PrefabChanged && !EntityManager.Exists(change.Prefab)) continue;
                // CompleteReaders above owns this synchronous batch. The
                // helpers schedule no jobs; keep all validation/reset work and
                // mark each actor once after its final applied change.
                bool marked = change.Marked;
                try
                {
                    if (change.PrefabChanged)
                        marked |= NativeVisualInvalidation.ApplyPrefabInBatch(World, entity, change.Prefab);
                    if (change.PoseChanged && EntityManager.HasComponent<Transform>(entity) && EntityManager.GetComponentData<Transform>(entity).Equals(change.OriginalPose))
                        marked |= NativeVisualInvalidation.ApplyTransformInBatch(World, entity, change.Pose);
                    if (change.RestoreRelative) marked |= NativeVisualInvalidation.ApplyRelativeInBatch(World, entity, change.Relative);
                }
                finally
                {
                    // A later failed write cannot leave an applied prefab or
                    // pose without the required native initialization flags.
                    if (marked) NativeVisualInvalidation.ApplyMark(EntityManager, entity);
                }
            }
            changes.Clear();
        }

        internal void FinishFrame()
        {
            if (readyDefinitions.Count == 0) return;
            NativeVisualInvalidation.CompleteReaders(World);
            var used = new HashSet<Entity>();
            using (var values = references.ToComponentDataArray<PrefabRef>(Allocator.Temp)) foreach (var value in values) used.Add(value.m_Prefab);
            foreach (var entity in new List<Entity>(readyDefinitions))
            {
                if (definitions[entity] > Frame || used.Contains(entity) || definitionOrigins.HasDependents(entity)) continue;
                if (EntityManager.Exists(entity)) EntityManager.DestroyEntity(entity);
                definitions.Remove(entity); readyDefinitions.Remove(entity);
                definitionSources.Remove(entity);
                definitionOrigins.ReleaseDestroyed(entity, DefinitionExists, DefinitionDependencyReleased);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            changes.Clear(); retire.Clear(); BeforeCulling = false;
            definitions.Clear(); definitionSources.Clear(); definitionOrigins.Clear(); readyDefinitions.Clear(); definitionRepairPending = false;
            base.OnGamePreload(purpose, mode);
        }
    }

    public partial class VisualMutationBoundarySystem : GameSystemBase
    {
        protected override void OnUpdate() { World.GetOrCreateSystemManaged<VisualLifecycleSystem>().CloseMutations(); }
    }

    public partial class VisualLifecycleCompletionSystem : GameSystemBase
    {
        protected override void OnUpdate()
        {
            try
            {
                World.GetOrCreateSystemManaged<VisualLifecycleSystem>().FinishFrame();
            }
            catch (Exception exception)
            {
                Mod.Log.Error(exception);
            }
        }
    }
}
