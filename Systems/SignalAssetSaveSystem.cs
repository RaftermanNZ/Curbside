using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Serialization;
using Unity.Collections;
using Unity.Entities;

namespace MedianGrassReplacement.Systems
{
    // A last serialization boundary for directly placed render donors as well
    // as derived signal prefabs. Saves contain native prefab references only.
    public partial class SignalAssetSaveSystem : GameSystemBase, IPreSerialize
    {
        private static readonly Dictionary<Entity,Entity> native=new Dictionary<Entity,Entity>();
        private readonly Dictionary<Entity,KeyValuePair<Entity,Entity>> restored=new Dictionary<Entity,KeyValuePair<Entity,Entity>>();
        private EntityQuery actors;
        internal static void Register(Entity custom,Entity fallback)
        {
            if(custom!=Entity.Null&&fallback!=Entity.Null&&custom!=fallback)native[custom]=fallback;
        }
        protected override void OnCreate()
        {
            base.OnCreate();native.Clear();
            actors=GetEntityQuery(ComponentType.ReadWrite<PrefabRef>(),ComponentType.ReadOnly<Game.Objects.Transform>(),ComponentType.Exclude<Deleted>());
        }
        public void PreSerialize(Context context)
        {
            if(native.Count==0)return; // No optional assets: no city-wide actor scan.
            Dependency.Complete();
            using(var items=actors.ToEntityArray(Allocator.Temp))foreach(var actor in items)
            {
                var custom=EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab;
                if(!native.TryGetValue(custom,out var fallback)||!EntityManager.Exists(fallback))continue;
                restored[actor]=new KeyValuePair<Entity,Entity>(custom,fallback);
                EntityManager.SetComponentData(actor,new PrefabRef{m_Prefab=fallback});
                NativeVisualInvalidation.Mark(EntityManager,actor);
            }
            Mod.Log.Info("Signal save compatibility: converted "+restored.Count+" remaining custom asset references to native fallbacks before prefab serialization.");
        }
        internal void RevertToVanilla()
        {
            PreSerialize(default(Context));restored.Clear();native.Clear();
        }
        protected override void OnUpdate()
        {
            if(Mod.Saving||restored.Count==0)return;
            Dependency.Complete();
            foreach(var item in restored)
                if(EntityManager.Exists(item.Key)&&!EntityManager.HasComponent<Deleted>(item.Key)&&EntityManager.HasComponent<PrefabRef>(item.Key)&&EntityManager.Exists(item.Value.Key)&&!EntityManager.HasComponent<Deleted>(item.Value.Key)&&EntityManager.GetComponentData<PrefabRef>(item.Key).m_Prefab==item.Value.Value)
                {
                    EntityManager.SetComponentData(item.Key,new PrefabRef{m_Prefab=item.Value.Key});
                    if(!EntityManager.HasComponent<Updated>(item.Key))EntityManager.AddComponent<Updated>(item.Key);
                    if(!EntityManager.HasComponent<Game.Common.BatchesUpdated>(item.Key))EntityManager.AddComponent<Game.Common.BatchesUpdated>(item.Key);
                    if(!EntityManager.HasComponent<Game.Common.EffectsUpdated>(item.Key))EntityManager.AddComponent<Game.Common.EffectsUpdated>(item.Key);
                }
            restored.Clear();
        }
    }
}
