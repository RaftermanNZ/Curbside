using System;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    public struct MGRPendingVisualRegistration : IComponentData { }

    internal static class GeneratedVisualLifecycle
    {
        internal static void RetireSaveBatch(World world,NativeArray<Entity> entities)
        {
            var em=world.EntityManager;
            // Finish both tree-access scopes before structural changes. The
            // entity array is an owned snapshot, not an ECS buffer/chunk view.
            var objects=world.GetExistingSystemManaged<Game.Objects.SearchSystem>();
            if(objects!=null)
            {
                var tree=objects.GetStaticSearchTree(false,out var jobs);jobs.Complete();
                foreach(var entity in entities)if(em.Exists(entity)&&em.HasComponent<Game.Objects.Transform>(entity))tree.TryRemove(entity);
            }
            var lanes=world.GetExistingSystemManaged<Game.Net.SearchSystem>();
            if(lanes!=null)
            {
                var tree=lanes.GetLaneSearchTree(false,out var jobs);jobs.Complete();
                foreach(var entity in entities)if(em.Exists(entity)&&em.HasComponent<Game.Net.Lane>(entity))tree.TryRemove(entity);
            }
            using(var deleted=new NativeList<Entity>(entities.Length,Allocator.Temp))
            {
                foreach(var entity in entities){world.GetExistingSystemManaged<GeneratedPaintOwnerCleanupSystem>()?.Untrack(entity);if(em.Exists(entity)&&!em.HasComponent<Deleted>(entity))deleted.Add(entity);}
                if(deleted.Length>0)em.AddComponent(deleted.AsArray(),ComponentType.ReadWrite<Deleted>());
            }
        }

        internal static ComponentType[] Tags() => new[] { ComponentType.ReadOnly<MGRCurbMarking>(), ComponentType.ReadOnly<MGRIntersectionPaint>(), ComponentType.ReadOnly<MGRVisualPowerlineRuntime>(), ComponentType.ReadOnly<MGRSignalWire>(), ComponentType.ReadOnly<MGRSignalLuminaireRuntime>() };

        internal static void Track(World world, Entity entity)
        {
            if(world.EntityManager.HasComponent<Lane>(entity)&&world.EntityManager.HasComponent<Owner>(entity))world.GetOrCreateSystemManaged<GeneratedPaintOwnerCleanupSystem>().Track(entity,world.EntityManager.GetComponentData<Owner>(entity).m_Owner);
            if (!world.EntityManager.HasComponent<MGRPendingVisualRegistration>(entity)) world.EntityManager.AddComponent<MGRPendingVisualRegistration>(entity);
        }

        internal static void Unregister(World world, Entity entity)
        {
            var manager = world.EntityManager;
            if (!manager.Exists(entity)) return;
            if (manager.HasComponent<Game.Objects.Transform>(entity))
            {
                var search = world.GetExistingSystemManaged<Game.Objects.SearchSystem>();
                if (search != null) { var tree = search.GetStaticSearchTree(false, out var jobs); jobs.Complete(); tree.TryRemove(entity); }
            }
            if (manager.HasComponent<Lane>(entity))
            {
                var search = world.GetExistingSystemManaged<Game.Net.SearchSystem>();
                if (search != null) { var tree = search.GetLaneSearchTree(false, out var jobs); jobs.Complete(); tree.TryRemove(entity); }
            }
        }

        internal static void Retire(World world, Entity entity)
        {
            world.GetExistingSystemManaged<GeneratedPaintOwnerCleanupSystem>()?.Untrack(entity);
            if(world.EntityManager.Exists(entity))world.GetExistingSystemManaged<VisualLifecycleSystem>()?.Retire(entity);
        }

        internal static void RetireDefinition(World world, Entity entity, Entity original)
        {
            world.GetExistingSystemManaged<VisualLifecycleSystem>()?.RetireDefinition(entity, original);
        }

        internal static void RetireForSerialization(World world, Entity entity) { RetireNow(world,entity); }

        internal static void RetireNow(World world, Entity entity)
        {
            var manager = world.EntityManager;
            if (!manager.Exists(entity)) return;
            world.GetExistingSystemManaged<GeneratedPaintOwnerCleanupSystem>()?.Untrack(entity);
            Unregister(world, entity);
            manager.CompleteAllTrackedJobs();
            // A generated lane can have been picked up by a native reference
            // pass. Remove that membership too, before the child is destroyed.
            if (manager.HasComponent<Lane>(entity) && manager.HasComponent<Owner>(entity))
            {
                var owner = manager.GetComponentData<Owner>(entity).m_Owner;
                if (manager.Exists(owner) && manager.HasBuffer<Game.Net.SubLane>(owner))
                {
                    var lanes = manager.GetBuffer<Game.Net.SubLane>(owner);
                    for (int i = lanes.Length - 1; i >= 0; i--) if (lanes[i].m_SubLane == entity) lanes.RemoveAt(i);
                }
            }
            if (!manager.HasComponent<Deleted>(entity)) manager.AddComponent<Deleted>(entity);
        }
    }

    public partial class GeneratedVisualRegistrationSystem : GameSystemBase
    {
        private EntityQuery pending, spatial;
        protected override void OnCreate()
        {
            base.OnCreate();
            pending = GetEntityQuery(ComponentType.ReadOnly<MGRPendingVisualRegistration>(), ComponentType.Exclude<Deleted>());
            spatial = GetEntityQuery(ComponentType.ReadOnly<MGRPendingSpatialUpdate>());
        }

        protected override void OnUpdate()
        {
            if(Mod.Saving)return;
            World.GetOrCreateSystemManaged<VisualLifecycleSystem>().RepairDefinitionReferences();
            Dependency.Complete();
            using (var entities = pending.ToEntityArray(Allocator.Temp)) foreach (var entity in entities)
            {
                if (EntityManager.HasComponent<Owner>(entity))
                {
                    var owner = EntityManager.GetComponentData<Owner>(entity).m_Owner;
                    if (!EntityManager.Exists(owner) || EntityManager.HasComponent<Deleted>(owner) || !EntityManager.HasComponent<PrefabRef>(owner))
                    {
                        GeneratedVisualLifecycle.RetireNow(World, entity);
                        continue;
                    }
                }
                GeneratedVisualLifecycle.Unregister(World, entity);
                if (!EntityManager.HasComponent<Created>(entity)) EntityManager.AddComponent<Created>(entity);
                if (!EntityManager.HasComponent<Updated>(entity)) EntityManager.AddComponent<Updated>(entity);
                EntityManager.RemoveComponent<MGRPendingVisualRegistration>(entity);
            }
            using (var entities = spatial.ToEntityArray(Allocator.Temp)) foreach (var entity in entities)
            {
                if (!EntityManager.HasComponent<Deleted>(entity) && !EntityManager.HasComponent<Updated>(entity)) EntityManager.AddComponent<Updated>(entity);
                EntityManager.RemoveComponent<MGRPendingSpatialUpdate>(entity);
            }
        }
    }

    // Serialization can purge Deleted entities without a rendering frame. Include
    // visuals already deleted by a network edit, not just our active dictionaries.
    public partial class GeneratedVisualSaveSystem : GameSystemBase,Game.Serialization.IPreSerialize
    {
        private EntityQuery _visuals;
        private readonly System.Collections.Generic.HashSet<Entity> earlyRetired = new System.Collections.Generic.HashSet<Entity>();
        internal void PrepareBeforeSaveFrame()
        {
            Dependency.Complete();
            earlyRetired.Clear();
            using(var entities=_visuals.ToEntityArray(Allocator.Temp))
            {
                GeneratedVisualLifecycle.RetireSaveBatch(World,entities);
                foreach(var entity in entities)earlyRetired.Add(entity);
            }
            Mod.Log.Info("Save visual retirement queued before native GPU-frame wait: count="+earlyRetired.Count);
        }
        internal void EndSaveWindow(){earlyRetired.Clear();}
        internal bool RetiredEarly(Entity entity)=>earlyRetired.Contains(entity);
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose,GameMode mode){earlyRetired.Clear();base.OnGamePreload(purpose,mode);}
        internal void AuditRetirement()
        {
            var renderer=World.GetExistingSystemManaged<Game.Rendering.PreCullingSystem>();
            if(renderer==null||earlyRetired.Count==0)return;
            var records=renderer.GetCullingData(true,out var readers);readers.Complete();int pending=0;
            for(int i=0;i<records.Length;i++)if(earlyRetired.Contains(records[i].m_Entity)&&(records[i].m_Flags&Game.Rendering.PreCullingFlags.Deleted)==0)pending++;
            Mod.Log.Info("Save native retirement audit: tracked="+earlyRetired.Count+" culling records without Deleted="+pending+"; native culling data unchanged.");
            if(pending!=0)Mod.Log.Warn("Save retirement did not fully drain before serialization; lifecycle safety remains unverified.");
        }
        protected override void OnCreate()
        {
            base.OnCreate();
            _visuals=GetEntityQuery(new EntityQueryDesc {Any=new[]{ComponentType.ReadOnly<MGRCurbMarking>(),ComponentType.ReadOnly<MGRIntersectionPaint>(),ComponentType.ReadOnly<MGRVisualPowerlineRuntime>(),ComponentType.ReadOnly<MGRSignalWire>(),ComponentType.ReadOnly<MGRSignalLuminaireRuntime>(),ComponentType.ReadOnly<MGRTemporaryPreviewPaint>()}});
        }
        protected override void OnUpdate(){}
        public void PreSerialize(Colossal.Serialization.Entities.Context context)
        {
            Dependency.Complete();
            using(var entities=_visuals.ToEntityArray(Allocator.Temp))GeneratedVisualLifecycle.RetireSaveBatch(World,entities);
        }
    }

    // Native road regeneration can retire our entities too. Remove their
    // search entries; normal rendering deletion remains game-owned.
    public partial class GeneratedVisualRetirementSystem : GameSystemBase
    {
        private EntityQuery _deleted;
        protected override void OnCreate()
        {
            base.OnCreate();
            _deleted=GetEntityQuery(new EntityQueryDesc {All=new[]{ComponentType.ReadOnly<Deleted>()},Any=new[]{ComponentType.ReadOnly<MGRCurbMarking>(),ComponentType.ReadOnly<MGRIntersectionPaint>(),ComponentType.ReadOnly<MGRVisualPowerlineRuntime>(),ComponentType.ReadOnly<MGRSignalWire>(),ComponentType.ReadOnly<MGRSignalLuminaireRuntime>(),ComponentType.ReadOnly<MGRTemporaryPreviewPaint>()}});
            RequireForUpdate(_deleted);
        }
        protected override void OnUpdate()
        {
            if(Mod.Saving)return;Dependency.Complete();
            using(var entities=_deleted.ToEntityArray(Allocator.Temp))foreach(var entity in entities)GeneratedVisualLifecycle.Retire(World,entity);
        }
    }

}
