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
            var change = GetChange(entity);
            change.PrefabChanged = true;
            change.Prefab = prefab;
        }

        internal void SetTransform(Entity entity, Transform transform)
        {
            if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<Transform>(entity)) return;
            if (BeforeCulling || Mod.Saving) { NativeVisualInvalidation.ApplyTransform(World, entity, transform); return; }
            var change = GetChange(entity);
            if (!change.PoseChanged) change.OriginalPose = EntityManager.GetComponentData<Transform>(entity);
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
            if (!EntityManager.Exists(entity)) return;
            definitions[entity] = Frame + (BeforeCulling ? 0 : 1);
            definitionSources[entity] = original;
        }

        internal void RepairDefinitionReferences()
        {
            if (definitions.Count == 0) return;
            NativeVisualInvalidation.CompleteReaders(World);
            using (var entities = references.ToEntityArray(Allocator.Temp)) foreach (var entity in entities)
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                if (definitionSources.TryGetValue(prefab, out var original) && EntityManager.Exists(original))
                {
                    if (EntityManager.HasComponent<Deleted>(entity)) continue;
                    NativeVisualInvalidation.ApplyPrefab(World, entity, original);
                }
            }
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
            foreach (var pair in definitionSources)
                if (!EntityManager.Exists(pair.Value))
                    throw new InvalidOperationException("Missing original visual definition before serialization: " + pair.Value);

            using (var entities = references.ToEntityArray(Allocator.Temp))
            {
                foreach (var entity in entities)
                {
                    var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                    if (!definitionSources.TryGetValue(prefab, out var original)) continue;
                    EntityManager.SetComponentData(entity, new PrefabRef(original));
                    if (!EntityManager.HasComponent<Deleted>(entity))
                        NativeVisualInvalidation.ApplyMark(EntityManager, entity);
                }
            }
            foreach (var entity in definitions.Keys)
                if (EntityManager.Exists(entity)) EntityManager.DestroyEntity(entity);
            definitions.Clear();
            definitionSources.Clear();
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
                if (change.PrefabChanged)
                    NativeVisualInvalidation.ApplyPrefab(World, entity, change.Prefab);
                if (change.PoseChanged && EntityManager.HasComponent<Transform>(entity) && EntityManager.GetComponentData<Transform>(entity).Equals(change.OriginalPose))
                    NativeVisualInvalidation.ApplyTransform(World, entity, change.Pose);
                if (change.RestoreRelative) NativeVisualInvalidation.ApplyRelative(World, entity, change.Relative);
                if (change.Marked) NativeVisualInvalidation.ApplyMark(EntityManager, entity);
            }
            changes.Clear();
        }

        internal void FinishFrame()
        {
            if (definitions.Count == 0) return;
            NativeVisualInvalidation.CompleteReaders(World);
            var used = new HashSet<Entity>();
            using (var values = references.ToComponentDataArray<PrefabRef>(Allocator.Temp)) foreach (var value in values) used.Add(value.m_Prefab);
            foreach (var entity in new List<Entity>(definitions.Keys))
            {
                if (definitions[entity] > Frame || used.Contains(entity)) continue;
                if (EntityManager.Exists(entity)) EntityManager.DestroyEntity(entity);
                definitions.Remove(entity);
                definitionSources.Remove(entity);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            changes.Clear(); retire.Clear(); BeforeCulling = false;
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
