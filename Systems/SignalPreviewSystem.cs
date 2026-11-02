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
        private EntityQuery previews,hiddenSignals,hiddenRuntime;
        private float nextEvidence;private string lastPlacementEvidence="No placement-time visibility sample captured.";private readonly Dictionary<Entity,Saved> saved=new Dictionary<Entity,Saved>();
        private readonly List<Entity> stale=new List<Entity>();
        private readonly List<Entity> ownerWork=new List<Entity>(16);
        protected override void OnCreate()
        {
            base.OnCreate();
            hiddenSignals=GetEntityQuery(ComponentType.ReadOnly<Hidden>(),ComponentType.ReadOnly<Game.Objects.TrafficLight>(),ComponentType.ReadOnly<Owner>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Temp>());
            hiddenRuntime=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Hidden>()},Any=new[]{ComponentType.ReadOnly<MGRCurbMarking>(),ComponentType.ReadOnly<MGRIntersectionPaint>(),ComponentType.ReadOnly<MGRSignalWire>(),ComponentType.ReadOnly<MGRSignalLuminaireRuntime>()},None=new[]{ComponentType.ReadOnly<Deleted>(),ComponentType.ReadOnly<Temp>()}});
            previews=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Temp>(),ComponentType.ReadOnly<Transform>(),ComponentType.ReadOnly<PrefabRef>()},None=new[]{ComponentType.ReadOnly<Deleted>()}});
        }
        protected override void OnUpdate()
        {
            long started=ModDiagnostics.RuntimeCosts.Begin();
            try{UpdatePerformanceCore();}finally{ModDiagnostics.RuntimeCosts.End("SignalPreviewSystem",started);}
        }
        private void UpdatePerformanceCore()
        {
            if(Mod.Saving)return;
            CapturePlacementEvidence();
            // Object/building/netlane tools can also regenerate network previews.
            // Eligibility comes from exact Temp provenance and our tracked signal,
            // not the UI tool class. ApplyTool still restores before native commit.
            if(previews.IsEmptyIgnoreFilter&&saved.Count==0)return;
            Dependency.Complete();var signals=World.GetOrCreateSystemManaged<FarSideLightSystem>();stale.Clear();
            foreach(var item in saved)if(!EntityManager.Exists(item.Key)||!EntityManager.HasComponent<Temp>(item.Key)||EntityManager.HasComponent<Deleted>(item.Key))stale.Add(item.Key);
            foreach(var entity in stale)saved.Remove(entity);
            using(var items=previews.ToEntityArray(Allocator.Temp))foreach(var entity in items)
            {
                var temp=EntityManager.GetComponentData<Temp>(entity);
                if((temp.m_Flags&(TempFlags.Delete|TempFlags.Cancel|TempFlags.Hidden))!=0)continue;
                var current=EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                // Runtime light-role components can arrive after preview creation.
                // Recognize the already initialized definition instead, before any
                // owner search; unrelated temporary buildings/props are skipped.
                if(!EntityManager.HasComponent<TrafficLightData>(current)&&!EntityManager.HasComponent<StreetLightData>(current))continue;
                var pose=EntityManager.GetComponentData<Transform>(entity);
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
        private void CapturePlacementEvidence()
        {
            // Diagnostics-only, retained in memory so moving to the snapshot button
            // cannot erase the placement state. No screenshot or automatic disk writes.
            if(!ModDiagnostics.TraceBuffer.Enabled||UnityEngine.Time.realtimeSinceStartup<nextEvidence)return;
            nextEvidence=UnityEngine.Time.realtimeSinceStartup+.5f;
            var tool=World.GetOrCreateSystemManaged<ToolSystem>().activeTool;
            if(tool==null||tool is DefaultToolSystem)return;
            int controlledHidden=0;
            using(var items=hiddenSignals.ToEntityArray(Allocator.Temp))foreach(var entity in items)
                if(EntityManager.HasComponent<MGRGardenCitySignals>(EntityManager.GetComponentData<Owner>(entity).m_Owner))controlledHidden++;
            int temporaryCount=previews.CalculateEntityCount(),hiddenCount=hiddenRuntime.CalculateEntityCount();
            if(temporaryCount==0&&controlledHidden==0&&hiddenCount==0)return;
            lastPlacementEvidence="at="+UnityEngine.Time.realtimeSinceStartup+" tool="+tool.toolID+" temporaryObjects="+temporaryCount+" hiddenControlledSignals="+controlledHidden+" hiddenGeneratedVisuals="+hiddenCount+" previewBindings="+saved.Count;
        }
        internal void RecordSnapshot()
        {
            Dependency.Complete();Mod.Log.Info("Signal road preview snapshot: temporary objects="+previews.CalculateEntityCount()+" custom visual references="+saved.Count);Mod.Log.Info("Last placement visibility sample: "+lastPlacementEvidence);
        }
        private Entity FindSource(Entity preview,Entity prefab,Transform pose,FarSideLightSystem signals)
        {
            ownerWork.Clear();ownerWork.Add(preview);
            float best=144;Entity source=Entity.Null;var nativePrefab=GardenCityLightPolicy.NativeSource(prefab);
            // Nested object/netlane previews can own another temporary object,
            // rather than the junction directly. Follow exact native provenance
            // with a fixed budget and cycle detection; never scan nearby nodes.
            for(int at=0;at<ownerWork.Count&&at<16;at++)
            {
                var owner=ownerWork[at];
                if(!EntityManager.Exists(owner)||EntityManager.HasComponent<Deleted>(owner))continue;
                if(owner!=preview&&signals.TryPreviewVisual(owner,out var linkedVisual,out _,out _)
                    &&GardenCityLightPolicy.NativeSource(linkedVisual)==nativePrefab)return owner;
                if(EntityManager.HasComponent<Temp>(owner))AddOwner(EntityManager.GetComponentData<Temp>(owner).m_Original);
                if(EntityManager.HasComponent<Owner>(owner))AddOwner(EntityManager.GetComponentData<Owner>(owner).m_Owner);
                if(!EntityManager.HasComponent<MGRGardenCitySignals>(owner)||!EntityManager.HasBuffer<Game.Objects.SubObject>(owner))continue;
                // Rebuilt preview children may not retain their own original ID.
                // Match only an exactly linked node's known native prefab role.
                foreach(var item in EntityManager.GetBuffer<Game.Objects.SubObject>(owner,true))
                {
                    if(!signals.TryPreviewVisual(item.m_SubObject,out var visual,out var original,out _)||GardenCityLightPolicy.NativeSource(visual)!=nativePrefab)continue;
                    float distance=math.distancesq(pose.m_Position.xz,original.m_Position.xz);
                    if(distance<best){best=distance;source=item.m_SubObject;}
                }
            }
            return source;
        }
        private void AddOwner(Entity owner)
        {if(owner!=Entity.Null&&ownerWork.Count<16&&!ownerWork.Contains(owner))ownerWork.Add(owner);}
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
        protected override void OnGamePreload(Purpose purpose,GameMode mode){Restore();lastPlacementEvidence="No placement-time visibility sample captured.";nextEvidence=0;base.OnGamePreload(purpose,mode);}
        protected override void OnDestroy(){Restore();base.OnDestroy();}
    }
    public partial class SignalPreviewRestoreSystem:GameSystemBase
    {
        protected override void OnUpdate()
        {
            World.GetExistingSystemManaged<PaintPreviewVisibilitySystem>()?.Restore();
            World.GetOrCreateSystemManaged<SignalPreviewSystem>().Restore();
        }
    }
}
