using System.Collections.Generic;
using Game;using Game.Prefabs;using Unity.Entities;using Unity.Collections;
namespace MedianGrassReplacement.Systems
{
 public partial class SignalSaveFenceSystem:GameSystemBase
 {
  protected override void OnUpdate()
  {
   Mod.Log.Info("Save boundary 1: completing entity and native effect readers before visual restoration.");
   SignalSaveAnimationGuard.Begin(World);
   EntityManager.CompleteAllTrackedJobs();
   var effects=World.GetExistingSystemManaged<Game.Effects.EffectControlSystem>();
   if(effects!=null){effects.GetEnabledData(false,out var jobs);jobs.Complete();}
   Mod.Log.Info("Save boundary 1 complete: native readers joined; no native buffers modified.");
  }
 }
 public partial class SignalSaveIndexAuditSystem:GameSystemBase
 {
  private EntityQuery loaded;
  protected override void OnCreate(){base.OnCreate();loaded=GetEntityQuery(ComponentType.ReadOnly<LoadedIndex>());}
  protected override void OnUpdate()
  {
   EntityManager.CompleteAllTrackedJobs();int count=loaded.CalculateEntityCountWithoutFiltering(),invalid=0,duplicates=0;var seen=new HashSet<int>();
   using(var entities=loaded.ToEntityArray(Allocator.Temp))foreach(var entity in entities)
   {
    if(!EntityManager.HasComponent<PrefabData>(entity)){invalid++;continue;}
    int index=EntityManager.GetComponentData<PrefabData>(entity).m_Index;int slot=index<0?count+index:index;
    if(slot<0||slot>=count){invalid++;if(invalid<=4)Mod.Log.Warn("Save prefab index out of range: entity="+entity+" index="+index+" count="+count);}
    else if(!seen.Add(slot))duplicates++;
   }
   Mod.Log.Info("Save boundary 2: native prefab index audit count="+count+" invalid="+invalid+" duplicateSlots="+duplicates+". Native prefab data unchanged.");
  }
 }
 public partial class SignalSaveNativeCompletionSystem:GameSystemBase
 {
  protected override void OnUpdate(){EntityManager.CompleteAllTrackedJobs();Mod.Log.Info("Save boundary 3: native BeginPrefabSerializationSystem completed.");}
 }

 // Register this coordinator once. Each native UpdateBefore/After call is a registration,
 // not an extra constraint on an existing registration.
 public partial class SignalSaveCoordinatorSystem:GameSystemBase
 {
  protected override void OnUpdate()
  {
   Mod.Log.Info("Save coordinator: preparation started.");
   World.GetOrCreateSystemManaged<SignalSaveFenceSystem>().Update();
   if(SaveDiagnostics.DeepEnabled)World.GetOrCreateSystemManaged<GeneratedVisualSaveSystem>().AuditRetirement();
   Prepare<DecalVisibilitySystem>();
   Prepare<SignalWireSystem>();
   Prepare<FarSideLightSystem>();
   Prepare<VisualPowerlineSystem>();
   Prepare<SignalPreviewSystem>();
   Prepare<StreetLightingSystem>();
   Prepare<CurbLineEmissionSystem>();
   Prepare<IntersectionPaintSystem>();
   Prepare<JunctionMarkingSystem>();
   Prepare<FixedParkingSystem>();
   Prepare<ApproachStopSystem>();
   Prepare<GeneratedVisualSaveSystem>();
   Prepare<SignalAssetSaveSystem>();
   if(SaveDiagnostics.DeepEnabled)World.GetOrCreateSystemManaged<SignalSaveIndexAuditSystem>().Update();
   Mod.Log.Info("Save coordinator: preparation completed.");
  }
  private void Prepare<T>() where T:ComponentSystemBase,Game.Serialization.IPreSerialize
  {
   Mod.Log.Info("Save preparation enter: "+typeof(T).Name);
   World.GetOrCreateSystemManaged<T>().PreSerialize(World.GetOrCreateSystemManaged<Game.Serialization.SaveGameSystem>().context);
   Mod.Log.Info("Save preparation complete: "+typeof(T).Name);
  }
 }
 public partial class SignalSaveSerializerEntrySystem:GameSystemBase
 {
  protected override void OnUpdate(){Mod.Log.Info("Save boundary 4: entering native entity serialization.");}
 }
 public partial class SignalSaveSerializerExitSystem:GameSystemBase
 {
  protected override void OnUpdate(){Mod.Log.Info("Save boundary 5: native entity serialization returned; asynchronous writing may still be pending.");}
 }
 public partial class SignalSaveWriterScheduledSystem:GameSystemBase
 {
  protected override void OnUpdate()
  {
   if(!SaveDiagnostics.JoinWriter){Mod.Log.Info("Save boundary 6: native asynchronous writer owns completion; experimental extra-wait removal enabled.");return;}
   Mod.Log.Info("Save boundary 6: joining native compression/file-write jobs; this is not full save completion.");
   // Same public handle completed by SaveGameSystem on its following update.
   // Save-only fence: no replacement writer, background polling or buffer access.
   var writer=World.GetExistingSystemManaged<Game.Serialization.WriteSystem>();
   if(writer==null){Mod.Log.Warn("Save boundary 7 unavailable: native WriteSystem missing.");return;}
   writer.writeDependency.Complete();
   Mod.Log.Info("Save boundary 7: native compression/file-write jobs completed. Preview, metadata and package work may still follow.");
  }
 }
}
