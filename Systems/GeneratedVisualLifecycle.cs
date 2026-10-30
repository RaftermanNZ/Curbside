using Game;

using Game.Common;

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
                foreach(var entity in entities)if(em.Exists(entity)&&!em.HasComponent<Deleted>(entity))deleted.Add(entity);
                if(deleted.Length>0)em.AddComponent(deleted.AsArray(),ComponentType.ReadWrite<Deleted>());
            }
        }

        internal static void Track(World world,Entity entity)

        {

            if(!world.EntityManager.HasComponent<MGRPendingVisualRegistration>(entity)) world.EntityManager.AddComponent<MGRPendingVisualRegistration>(entity);

        }

        internal static void Unregister(World world,Entity entity)

        {

            // Public native writer APIs: complete readers/writers before removal.

            // This also makes late-frame retirement safe before entity cleanup.

            var em=world.EntityManager;

            if(em.HasComponent<Game.Objects.Transform>(entity))

            {

                var system=world.GetExistingSystemManaged<Game.Objects.SearchSystem>();

                if(system!=null) {var tree=system.GetStaticSearchTree(false,out var jobs);jobs.Complete();tree.TryRemove(entity);}

            }

            if(em.HasComponent<Game.Net.Lane>(entity))

            {

                var system=world.GetExistingSystemManaged<Game.Net.SearchSystem>();

                if(system!=null) {var tree=system.GetLaneSearchTree(false,out var jobs);jobs.Complete();tree.TryRemove(entity);}

            }

        }

        internal static void Retire(World world,Entity entity)

        {

            var em=world.EntityManager;if(!em.Exists(entity)) return;

            // Use native Deleted retirement during saves as well as live edits.
            // Never rewrite the renderer's private culling records or timers.
            if(!em.HasComponent<Deleted>(entity)&&ModDiagnostics.TraceBuffer.Enabled&&(em.HasComponent<MGRSignalWire>(entity)||em.HasComponent<MGRSignalLuminaireRuntime>(entity)))Mod.Log.Info("Signal runtime retirement: entity="+entity+" wire="+em.HasComponent<MGRSignalWire>(entity)+" saving="+Mod.Saving);
            bool alreadyRetired=Mod.Saving&&em.HasComponent<Deleted>(entity)&&(world.GetExistingSystemManaged<GeneratedVisualSaveSystem>()?.RetiredEarly(entity)??false);
            if(!alreadyRetired)Unregister(world,entity);if(!em.HasComponent<Deleted>(entity))em.AddComponent<Deleted>(entity);

        }

        internal static void RetireForSerialization(World world,Entity entity)
        {
            if(!world.EntityManager.Exists(entity)) return;
            // Do not depend on the save/load event having fired before this
            // callback. Include visuals already marked Deleted by road edits.
            var em=world.EntityManager;
            Retire(world,entity);
        }


    }

    public partial class GeneratedVisualRegistrationSystem : GameSystemBase

    {

        private EntityQuery _pending;

        protected override void OnCreate()

        {

            base.OnCreate();_pending=GetEntityQuery(ComponentType.ReadOnly<MGRPendingVisualRegistration>(),ComponentType.Exclude<Deleted>());RequireForUpdate(_pending);

        }

        protected override void OnUpdate()

        {

            if(Mod.Saving) return;Dependency.Complete();

            using(var entities=_pending.ToEntityArray(Allocator.Temp)) foreach(var entity in entities)

            {

                // A generated marking can be created after native search jobs.

                // Retain insertion intent until the next actual search pass.

                GeneratedVisualLifecycle.Unregister(World,entity);

                if(!EntityManager.HasComponent<Created>(entity)) EntityManager.AddComponent<Created>(entity);

                if(!EntityManager.HasComponent<Updated>(entity)) EntityManager.AddComponent<Updated>(entity);

                EntityManager.RemoveComponent<MGRPendingVisualRegistration>(entity);

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
            _visuals=GetEntityQuery(new EntityQueryDesc {Any=new[]{ComponentType.ReadOnly<MGRCurbMarking>(),ComponentType.ReadOnly<MGRIntersectionPaint>(),ComponentType.ReadOnly<MGRVisualPowerlineRuntime>(),ComponentType.ReadOnly<MGRSignalWire>(),ComponentType.ReadOnly<MGRSignalLuminaireRuntime>()}});
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
            _deleted=GetEntityQuery(new EntityQueryDesc {All=new[]{ComponentType.ReadOnly<Deleted>()},Any=new[]{ComponentType.ReadOnly<MGRCurbMarking>(),ComponentType.ReadOnly<MGRIntersectionPaint>(),ComponentType.ReadOnly<MGRVisualPowerlineRuntime>(),ComponentType.ReadOnly<MGRSignalWire>(),ComponentType.ReadOnly<MGRSignalLuminaireRuntime>()}});
            RequireForUpdate(_deleted);
        }
        protected override void OnUpdate()
        {
            if(Mod.Saving)return;Dependency.Complete();
            using(var entities=_deleted.ToEntityArray(Allocator.Temp))foreach(var entity in entities)GeneratedVisualLifecycle.Retire(World,entity);
        }
    }

}

