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
        internal static void Mark(EntityManager manager,Entity entity) => ApplyMark(manager,entity);

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
        {
            var manager = world.EntityManager;
            if (!manager.Exists(entity) || manager.HasComponent<Deleted>(entity) || manager.HasComponent<Game.Objects.Relative>(entity)) return;
            CompleteReaders(world);
            manager.AddComponentData(entity, relative);
            ApplyMark(manager, entity);
        }

        internal static void ApplyPrefab(World world, Entity entity, Entity prefab)
        {
            var manager = world.EntityManager;
            if (!manager.Exists(entity) || manager.HasComponent<Deleted>(entity) || !manager.HasComponent<PrefabRef>(entity)) return;
            if (!manager.Exists(prefab)) throw new InvalidOperationException("Visual prefab no longer exists: " + prefab);
            if (manager.GetComponentData<PrefabRef>(entity).m_Prefab == prefab) return;
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
            CompleteReaders(world);
            manager.SetComponentData(entity, new PrefabRef(prefab));
            if (manager.HasBuffer<Emissive>(entity) && !manager.HasComponent<MGRLightReset>(entity)) manager.AddComponent<MGRLightReset>(entity);
            ApplyMark(manager, entity);
        }

        internal static void ApplyTransform(World world, Entity entity, Transform transform)
        {
            var manager = world.EntityManager;
            if (!manager.Exists(entity) || manager.HasComponent<Deleted>(entity) || !manager.HasComponent<Transform>(entity)) return;
            CompleteReaders(world);
            manager.SetComponentData(entity, transform);
            ApplyMark(manager, entity);
        }

        internal static void ApplyMark(EntityManager manager, Entity entity)
        {
            if (!manager.Exists(entity) || manager.HasComponent<Deleted>(entity)) return;
            if (!manager.HasComponent<Updated>(entity)) manager.AddComponent<Updated>(entity);
            if (!manager.HasComponent<BatchesUpdated>(entity)) manager.AddComponent<BatchesUpdated>(entity);
            if (!manager.HasComponent<EffectsUpdated>(entity)) manager.AddComponent<EffectsUpdated>(entity);
            // Late marking producers keep this until an actual search pass.
            if (!manager.HasComponent<Game.Tools.Temp>(entity) && !manager.HasComponent<MGRPendingSpatialUpdate>(entity)) manager.AddComponent<MGRPendingSpatialUpdate>(entity);
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
