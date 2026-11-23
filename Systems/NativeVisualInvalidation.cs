using System;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Unity.Entities;
using Transform = Game.Objects.Transform;

namespace MedianGrassReplacement.Systems
{
    public struct MGRLightReset : IComponentData { }
    public struct MGRPendingSpatialUpdate : IComponentData { }

    internal static class NativeVisualInvalidation
    {
        // Immutable, bounded subsets: one archetype transition for each visual.
        private static readonly ComponentTypeSet[] RefreshSets = CreateRefreshSets();
        private static ComponentTypeSet[] CreateRefreshSets()
        {
            var sets = new ComponentTypeSet[16];
            for (int mask = 1; mask < sets.Length; mask++)
            {
                var types = new System.Collections.Generic.List<ComponentType>(4);
                if ((mask & 1) != 0) types.Add(ComponentType.ReadWrite<Updated>());
                if ((mask & 2) != 0) types.Add(ComponentType.ReadWrite<BatchesUpdated>());
                if ((mask & 4) != 0) types.Add(ComponentType.ReadWrite<EffectsUpdated>());
                if ((mask & 8) != 0) types.Add(ComponentType.ReadWrite<MGRPendingSpatialUpdate>());
                sets[mask] = new ComponentTypeSet(types.ToArray());
            }
            return sets;
        }

        internal static void Mark(EntityManager manager,Entity entity) => ApplyMark(manager,entity);

        internal static void QueuePreviewRefresh(EntityManager manager, EntityCommandBuffer commands, Entity entity)
        {
            int missing = 0;
            if (!manager.HasComponent<Updated>(entity)) missing |= 1;
            if (!manager.HasComponent<BatchesUpdated>(entity)) missing |= 2;
            if (!manager.HasComponent<EffectsUpdated>(entity)) missing |= 4;
            if (missing != 0) commands.AddComponent(entity, RefreshSets[missing]);
        }

        internal static void CompleteReaders(World world)
        {
            var culling = world.GetExistingSystemManaged<PreCullingSystem>();
            if (culling != null) { culling.GetCullingData(false, out var jobs); jobs.Complete(); }
            var effects = world.GetExistingSystemManaged<Game.Effects.EffectControlSystem>();
            if (effects != null) { effects.GetEnabledData(false, out var jobs); jobs.Complete(); }
            world.EntityManager.CompleteAllTrackedJobs();
        }

        internal static void SetPrefab(World world, Entity entity, Entity prefab)
        {
            world.GetExistingSystemManaged<VisualLifecycleSystem>()?.SetPrefab(entity, prefab);
        }

        internal static void SetTransform(World world, Entity entity, Transform transform)
        {
            world.GetExistingSystemManaged<VisualLifecycleSystem>()?.SetTransform(entity, transform);
        }

        internal static void Mark(World world, Entity entity)
        {
            world.GetExistingSystemManaged<VisualLifecycleSystem>()?.Mark(entity);
        }

        internal static void RestoreRelative(World world, Entity entity, Game.Objects.Relative relative)
        {
            world.GetExistingSystemManaged<VisualLifecycleSystem>()?.RestoreRelative(entity, relative);
        }

        internal static void ApplyRelative(World world, Entity entity, Game.Objects.Relative relative)
            => ApplyRelativeCore(world, entity, relative, true);

        // Only VisualLifecycleSystem.FlushPending may use this entry: its
        // completed-reader fence precedes this synchronous mutation batch.
        internal static bool ApplyRelativeInBatch(World world, Entity entity, Game.Objects.Relative relative)
            => ApplyRelativeCore(world, entity, relative, false);

        private static bool ApplyRelativeCore(World world, Entity entity, Game.Objects.Relative relative, bool standalone)
        {
            var manager = world.EntityManager;
            if (!manager.Exists(entity) || manager.HasComponent<Deleted>(entity) || manager.HasComponent<Game.Objects.Relative>(entity)) return false;
            if (standalone) CompleteReaders(world);
            manager.AddComponentData(entity, relative);
            if (standalone) ApplyMark(manager, entity);
            return true;
        }

        internal static void ApplyPrefab(World world, Entity entity, Entity prefab)
            => ApplyPrefabCore(world, entity, prefab, true);

        // Only VisualLifecycleSystem.FlushPending may use this entry: its
        // completed-reader fence precedes this synchronous mutation batch.
        internal static bool ApplyPrefabInBatch(World world, Entity entity, Entity prefab)
            => ApplyPrefabCore(world, entity, prefab, false);

        private static bool ApplyPrefabCore(World world, Entity entity, Entity prefab, bool standalone)
        {
            var manager = world.EntityManager;
            if (!manager.Exists(entity) || manager.HasComponent<Deleted>(entity) || !manager.HasComponent<PrefabRef>(entity)) return false;
            if (!manager.Exists(prefab)) throw new InvalidOperationException("Visual prefab no longer exists: " + prefab);
            if (manager.GetComponentData<PrefabRef>(entity).m_Prefab == prefab) return false;
            if (manager.HasComponent<Game.Objects.Transform>(entity))
            {
                if (!manager.HasComponent<ObjectData>(prefab) || !manager.HasComponent<ObjectGeometryData>(prefab) || !manager.HasBuffer<SubMesh>(prefab))
                    throw new InvalidOperationException("Visual object prefab is not initialized: " + prefab);
                var meshes = manager.GetBuffer<SubMesh>(prefab, true);
                foreach (var mesh in meshes)
                {
                    if (!manager.Exists(mesh.m_SubMesh)) throw new InvalidOperationException("Visual mesh no longer exists: " + mesh.m_SubMesh);
                    if (manager.HasBuffer<ProceduralLight>(mesh.m_SubMesh) && !manager.HasBuffer<Emissive>(entity))
                        throw new InvalidOperationException("Visual replacement requires an emissive actor: " + entity);
                }
                if (manager.HasBuffer<Game.Prefabs.Effect>(prefab) && manager.GetBuffer<Game.Prefabs.Effect>(prefab, true).Length != 0 && !manager.HasBuffer<Game.Effects.EnabledEffect>(entity))
                    throw new InvalidOperationException("Visual replacement requires an effect-enabled actor: " + entity);
            }
            if (manager.HasBuffer<Emissive>(entity) != manager.HasBuffer<LightState>(entity))
                throw new InvalidOperationException("Incomplete native light buffers on " + entity);
            if (standalone) CompleteReaders(world);
            manager.SetComponentData(entity, new PrefabRef(prefab));
            if (manager.HasBuffer<Emissive>(entity) && !manager.HasComponent<MGRLightReset>(entity)) manager.AddComponent<MGRLightReset>(entity);
            if (standalone) ApplyMark(manager, entity);
            return true;
        }

        internal static void ApplyTransform(World world, Entity entity, Transform transform)
            => ApplyTransformCore(world, entity, transform, true);

        // Only VisualLifecycleSystem.FlushPending may use this entry: its
        // completed-reader fence precedes this synchronous mutation batch.
        internal static bool ApplyTransformInBatch(World world, Entity entity, Transform transform)
            => ApplyTransformCore(world, entity, transform, false);

        private static bool ApplyTransformCore(World world, Entity entity, Transform transform, bool standalone)
        {
            var manager = world.EntityManager;
            if (!manager.Exists(entity) || manager.HasComponent<Deleted>(entity) || !manager.HasComponent<Transform>(entity)) return false;
            if (manager.GetComponentData<Transform>(entity).Equals(transform)) return false;
            if (standalone) CompleteReaders(world);
            manager.SetComponentData(entity, transform);
            if (standalone) ApplyMark(manager, entity);
            return true;
        }

        internal static void ApplyMark(EntityManager manager, Entity entity)
        {
            if (!manager.Exists(entity) || manager.HasComponent<Deleted>(entity)) return;
            int missing = 0;
            if (!manager.HasComponent<Updated>(entity)) missing |= 1;
            if (!manager.HasComponent<BatchesUpdated>(entity)) missing |= 2;
            if (!manager.HasComponent<EffectsUpdated>(entity)) missing |= 4;
            // Late marking producers keep this until an actual search pass.
            if (!manager.HasComponent<Game.Tools.Temp>(entity) && !manager.HasComponent<MGRPendingSpatialUpdate>(entity)) missing |= 8;
            // Even an empty AddComponent set enters structural-change handling.
            // Preserve the no-write path when every required flag is present.
            if (missing != 0) manager.AddComponent(entity, RefreshSets[missing]);
        }
    }

    public partial class SignalLightResetSystem : Game.GameSystemBase
    {
        private EntityQuery pending;
        protected override void OnCreate()
        {
            base.OnCreate();
            pending = GetEntityQuery(ComponentType.ReadOnly<MGRLightReset>());
            RequireForUpdate(pending);
        }

        protected override void OnUpdate()
        {
            NativeVisualInvalidation.CompleteReaders(World);
            var emissiveSystem = World.GetOrCreateSystemManaged<ProceduralEmissiveSystem>();
            emissiveSystem.GetHeapAllocator(out var info, out var removes, out var currentTime, out var jobs);
            jobs.Complete();
            using (var entities = pending.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                foreach (var entity in entities)
                {
                    if (!EntityManager.HasComponent<Deleted>(entity) && EntityManager.HasBuffer<Emissive>(entity) && EntityManager.HasBuffer<LightState>(entity))
                    {
                        var emissives = EntityManager.GetBuffer<Emissive>(entity);
                        for (int i = 0; i < emissives.Length; i++)
                        {
                            var allocation = emissives[i].m_BufferAllocation;
                            if (!allocation.Empty) removes.Enqueue(new ProceduralEmissiveSystem.AllocationRemove { m_Allocation = allocation, m_RemoveTime = currentTime });
                        }
                        // Same removal protocol as InitializeLightsSystem.Remove.
                        // Native initialization owns the replacement allocations.
                        emissives.Clear();
                        EntityManager.GetBuffer<LightState>(entity).Clear();
                    }
                    EntityManager.RemoveComponent<MGRLightReset>(entity);
                }
            }
        }
    }
}
