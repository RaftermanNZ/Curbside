using System;
using System.Linq;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
namespace GardenCitySignals
{
 internal static class FloridaModuleCatalog
 {
  internal static readonly string[] Names={"CSFHorizontalHead","CSFLowerHead","CSFPedHead","CSFPole"};
  internal static bool Complete(string[] names)=>names!=null&&names.Length==4&&names.Distinct(StringComparer.Ordinal).Count()==4&&Names.All(n=>names.Contains(n,StringComparer.Ordinal));
  internal static bool Visible(bool originalReady,bool floridaReady)=>originalReady&&floridaReady;
 }
 internal static class FloridaNativeCatalog
 {
  private static PrefabSystem owner;
  private static readonly Dictionary<string,StaticObjectPrefab> models=new Dictionary<string,StaticObjectPrefab>();
  internal static void Register(PrefabSystem system,IDictionary<string,StaticObjectPrefab> source){owner=system;models.Clear();foreach(var p in source)models.Add(p.Key,p.Value);}
  internal static bool TryGet(PrefabSystem system,string name,out StaticObjectPrefab model){model=null;return system==owner&&models.TryGetValue(name,out model)&&model!=null;}
  internal static void Reset(PrefabSystem system=null){if(system!=null&&owner!=system)return;owner=null;models.Clear();}
 }
 public partial class FloridaPackSystem:GameSystemBase
 {
  private PrefabSystem prefabs;private EntityQuery candidates,styledNodes;private bool ready;private double start=-1,next;
  private static readonly HashSet<string> expected=new HashSet<string>(FloridaModuleCatalog.Names.Select(n=>"CSF Module "+n),StringComparer.Ordinal);
  internal static string DiscoveryStatus{get;private set;}="Waiting for Florida native modules";
  protected override void OnCreate(){base.OnCreate();prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();candidates=GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),ComponentType.ReadOnly<StaticObjectData>(),ComponentType.Exclude<Game.Common.Deleted>());styledNodes=GetEntityQuery(ComponentType.ReadOnly<MedianGrassReplacement.Systems.MGRSignalStyle>(),ComponentType.ReadOnly<Game.Net.TrafficLights>(),ComponentType.Exclude<Game.Common.Deleted>());}
  private static string Owner(StaticObjectPrefab model){try{return model?.asset?.GetMeta().platformID;}catch{return null;}}
  protected override void OnUpdate()
  {
   if(ready||!(AppDomain.CurrentDomain.GetData("Curbside.Active.v1") is bool active&&active))return;
   double now=Mod.LoadTimer.Elapsed.TotalSeconds;if(now<next)return;next=now+1;
   bool local=Mod.PreparedPack&&SignalPackPolicy.IncludesFlorida;
   // An exact old local manifest is positive evidence that this local pack has four styles.
   if(Mod.PreparedPack&&!SignalPackPolicy.IncludesFlorida){Enabled=false;DiscoveryStatus="Local original pack has no Florida modules";return;}
   var found=new Dictionary<string,StaticObjectPrefab>();bool ambiguous=false;
   using(var items=candidates.ToEntityArray(Allocator.Temp))foreach(var entity in items)
   {
    if(!prefabs.TryGetPrefab<StaticObjectPrefab>(entity,out var model)||model==null||model.isBuiltin||!expected.Contains(model.name))continue;
    if(!local&&string.IsNullOrWhiteSpace(Owner(model)))continue;
    if(found.TryGetValue(model.name,out var previous)&&previous!=model){ambiguous=true;continue;}found[model.name]=model;
   }
   bool original=NativeSignalCatalog.TryGet(prefabs,"CSG Module CSGSignPlate",out var sign)&&NativeSignalCatalog.TryGet(prefabs,"CSG Module CSGRigidLuminaire",out _);
   string originalOwner=original?Owner(sign):null;
   bool complete=FloridaCatalogPolicy.Accept(found.Count,ambiguous,original,local,originalOwner,found.Values.Select(Owner).ToArray());
   if(complete)foreach(var model in found.Values)
   {
    if(model.m_Meshes==null||model.m_Meshes.Length!=1||model.m_Meshes[0].m_Mesh==null||!prefabs.TryGetEntity(model,out var entity)||!EntityManager.Exists(entity)||(!local&&(string.IsNullOrWhiteSpace(originalOwner)||Owner(model)!=originalOwner))){complete=false;break;}
   }
   if(complete)
   {
    Entity fallback=Entity.Null;foreach(var name in new[]{"NA_TrafficLightCarCrosswalk01","NA_TrafficLightCar01","EU_TrafficLightCar01"})if(prefabs.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab),name),out var model)&&model is StaticObjectPrefab native&&native.isBuiltin&&prefabs.TryGetEntity(native,out fallback)&&EntityManager.Exists(fallback))break;
    if(fallback==Entity.Null)return;
    bool layoutChanged=!local&&!SignalPackPolicy.UseLowerRigidSignLayout;
    FloridaNativeCatalog.Register(prefabs,found);if(!local)SignalPackPolicy.AdoptPublishedFloridaLayout();
    foreach(var model in found.Values)if(prefabs.TryGetEntity(model,out var entity))MedianGrassReplacement.Systems.SignalAssetSaveSystem.Register(entity,fallback);
    // A published five-style pack also proves the revised original sign layout.
    // Refresh affected junctions once if that proof arrived after original adoption.
    using(var items=styledNodes.ToEntityArray(Allocator.Temp))foreach(var entity in items)if(layoutChanged||EntityManager.GetComponentData<MedianGrassReplacement.Systems.MGRSignalStyle>(entity).Value==4)MedianGrassReplacement.Systems.FarSideLightSystem.Queue(entity);
    ready=true;Enabled=false;DiscoveryStatus="Ready: 4 Florida modules and shared original modules";Mod.Log.Info(DiscoveryStatus+". Fifth style enabled; startup scan stopped.");return;
   }
   DiscoveryStatus="Florida modules "+found.Count+"/4; originalReady="+original+"; ambiguous="+ambiguous;
   if(GameManager.instance==null||GameManager.instance.state<GameManager.State.WorldReady)return;
   if(start<0)start=now;
   if(!FloridaCatalogPolicy.Expired(now,start,local||found.Count>0))return;
   Enabled=false;DiscoveryStatus+="; optional fifth style hidden; startup scan stopped";Mod.Log.Info(DiscoveryStatus);
  }
  protected override void OnGameLoadingComplete(Purpose purpose,GameMode mode){base.OnGameLoadingComplete(purpose,mode);if(ready)return;Enabled=true;start=-1;next=0;}
  protected override void OnDestroy(){FloridaNativeCatalog.Reset(prefabs);base.OnDestroy();}
 }
}
