using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;using Game.Common;using Game.Prefabs;using Game.Serialization;using Game.Tools;
using Unity.Collections;using Unity.Entities;using Unity.Mathematics;
using Transform=Game.Objects.Transform;
namespace MedianGrassReplacement.Systems
{
    // Reuse already initialized signal visuals on native road previews. No
    // prefab creation, controller edits, road edits or new render entities.
    public partial class SignalPreviewSystem:GameSystemBase,IPreSerialize
    {
        private struct Saved {internal Entity Original,Applied;internal Transform Native,Visual;}
        private EntityQuery previews;private readonly Dictionary<Entity,Saved> saved=new Dictionary<Entity,Saved>();
        private readonly List<Entity> stale=new List<Entity>();
        protected override void OnCreate()
        {
            base.OnCreate();previews=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Temp>(),ComponentType.ReadOnly<Transform>(),ComponentType.ReadOnly<PrefabRef>()},Any=new[]{ComponentType.ReadOnly<Game.Objects.TrafficLight>(),ComponentType.ReadOnly<Game.Objects.StreetLight>()},None=new[]{ComponentType.ReadOnly<Deleted>()}});
        }
        protected override void OnUpdate()
        {
            if(Mod.Saving)return;
            var tool=World.GetOrCreateSystemManaged<ToolSystem>().activeTool;
            if(!(tool is NetToolSystem)){Restore();return;}
            if(previews.IsEmptyIgnoreFilter&&saved.Count==0)return;
            Dependency.Complete();var signals=World.GetOrCreateSystemManaged<FarSideLightSystem>();stale.Clear();
            foreach(var item in saved)if(!EntityManager.Exists(item.Key)||!EntityManager.HasComponent<Temp>(item.Key)||EntityManager.HasComponent<Deleted>(item.Key))stale.Add(item.Key);
            foreach(var entity in stale)saved.Remove(entity);
            using(var items=previews.ToEntityArray(Allocator.Temp))foreach(var entity in items)
            {
                var temp=EntityManager.GetComponentData<Temp>(entity);
                if((temp.m_Flags&(TempFlags.Delete|TempFlags.Cancel|TempFlags.Hidden))!=0)continue;
                var current=EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;var pose=EntityManager.GetComponentData<Transform>(entity);
                if(saved.TryGetValue(entity,out var prior)&&current==prior.Applied&&pose.Equals(prior.Visual))continue;
                Entity source=temp.m_Original;
                if(!signals.TryPreviewVisual(source,out _,out _,out _))source=FindSource(entity,current,pose,signals);
                if(!signals.TryPreviewVisual(source,out var visual,out var native,out var applied))continue;
                // Respect a preview which already inherited the custom visual.
                if(current==visual)continue;
                var placed=pose;placed.m_Position+=applied.m_Position-native.m_Position;
                placed.m_Rotation=math.mul(math.mul(applied.m_Rotation,math.inverse(native.m_Rotation)),pose.m_Rotation);
                if(!math.all(math.isfinite(placed.m_Position)))continue;
                saved[entity]=new Saved{Original=current,Applied=visual,Native=pose,Visual=placed};
                EntityManager.SetComponentData(entity,new PrefabRef(visual));EntityManager.SetComponentData(entity,placed);
                if(!EntityManager.HasComponent<BatchesUpdated>(entity))EntityManager.AddComponent<BatchesUpdated>(entity);
                if(!EntityManager.HasComponent<EffectsUpdated>(entity))EntityManager.AddComponent<EffectsUpdated>(entity);
            }
        }
        internal void RecordSnapshot()
        {
            Dependency.Complete();Mod.Log.Info("Signal road preview snapshot: temporary lights="+previews.CalculateEntityCount()+" custom visual references="+saved.Count);
        }
        private Entity FindSource(Entity preview,Entity prefab,Transform pose,FarSideLightSystem signals)
        {
            if(!EntityManager.HasComponent<Owner>(preview))return Entity.Null;
            var owner=EntityManager.GetComponentData<Owner>(preview).m_Owner;
            if(!EntityManager.Exists(owner))return Entity.Null;
            if(EntityManager.HasComponent<Temp>(owner))owner=EntityManager.GetComponentData<Temp>(owner).m_Original;
            if(!EntityManager.Exists(owner)||!EntityManager.HasComponent<MGRGardenCitySignals>(owner)||!EntityManager.HasBuffer<Game.Objects.SubObject>(owner))return Entity.Null;
            float best=144;Entity source=Entity.Null;var nativePrefab=GardenCityLightPolicy.NativeSource(prefab);
            // Rebuilt preview children may not retain their own original ID.
            // Match only this node's known replacements and native prefab role.
            foreach(var item in EntityManager.GetBuffer<Game.Objects.SubObject>(owner,true))
            {
                if(!signals.TryPreviewVisual(item.m_SubObject,out var visual,out var original,out _)||GardenCityLightPolicy.NativeSource(visual)!=nativePrefab)continue;
                float distance=math.distancesq(pose.m_Position.xz,original.m_Position.xz);
                if(distance<best){best=distance;source=item.m_SubObject;}
            }
            return source;
        }
        internal void Restore()
        {
            if(saved.Count==0)return;Dependency.Complete();
            foreach(var item in saved)
            {
                var entity=item.Key;var value=item.Value;
                if(!EntityManager.Exists(entity)||!EntityManager.HasComponent<Temp>(entity)||!EntityManager.HasComponent<PrefabRef>(entity))continue;
                if(EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab!=value.Applied)continue;
                EntityManager.SetComponentData(entity,new PrefabRef(value.Original));
                if(EntityManager.HasComponent<Transform>(entity)&&EntityManager.GetComponentData<Transform>(entity).Equals(value.Visual))EntityManager.SetComponentData(entity,value.Native);
                if(!EntityManager.HasComponent<BatchesUpdated>(entity))EntityManager.AddComponent<BatchesUpdated>(entity);
                if(!EntityManager.HasComponent<EffectsUpdated>(entity))EntityManager.AddComponent<EffectsUpdated>(entity);
            }
            saved.Clear();
        }
        public void PreSerialize(Context context){Restore();}
        protected override void OnGamePreload(Purpose purpose,GameMode mode){Restore();base.OnGamePreload(purpose,mode);}
        protected override void OnDestroy(){Restore();base.OnDestroy();}
    }
    public partial class SignalPreviewRestoreSystem:GameSystemBase
    {
        protected override void OnUpdate(){World.GetOrCreateSystemManaged<SignalPreviewSystem>().Restore();}
    }
}
