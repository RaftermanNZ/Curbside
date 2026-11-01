using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Colossal.AssetPipeline;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Colossal.PSI.Common;
using Colossal.Serialization.Entities;
using Game;
using Game.AssetPipeline;
using Game.Modding;
using Game.Prefabs;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace GardenCitySignals
{
 internal static class Mod
 {
  internal static readonly ILog Log=MedianGrassReplacement.Mod.Log;
  internal static bool Saving=>MedianGrassReplacement.Mod.Saving;
  internal static readonly System.Diagnostics.Stopwatch LoadTimer=System.Diagnostics.Stopwatch.StartNew();
  internal static string Root;
  internal static bool PreparedPack;
  internal static string Cache=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GardenCitySignals","CS2Native-v17-capuv");
  internal static void Initialize(UpdateSystem updates)
  {
   NativeSignalCatalog.Reset();
   Root=Path.Combine(Application.persistentDataPath,"ModsData","TheCurbsideMod","SignalAssets");
   PreparedPack=SignalPackPolicy.ReadLocalManifest(Path.Combine(Root,"Prepared","native-pack.json"));
   bool present=PreparedPack;
   AppDomain.CurrentDomain.SetData("Curbside.GardenCitySignals.ActiveModels.v1",present);
   Log.Info("Curbside signal discovery: known local pack="+present+"; published packs are resolved by native prefab ownership. Automatic FBX import is disabled.");
   // Native subscribed assets are loaded after code mods. Probe the native
   // catalog during boot, then stop completely if the optional pack is absent.
   updates.UpdateAt<PackSystem>(SystemUpdatePhase.PrefabUpdate);
  }
 }
 public readonly struct PackDatabase:IAssetDatabaseDescriptor<PackDatabase>
 {
  public string name=>"GardenCitySignals";
  public bool canWriteSettings=>false;
  public DlcId dlcId=>DlcId.Invalid;
  public IAssetFactory assetFactory=>DefaultAssetFactory.instance;
  public IDataSourceProvider dataSourceProvider=>new FileSystemDataSource(name,Mod.Cache,assetFactory,0L);
  public bool Equals(PackDatabase other)=>true;
  public override bool Equals(object other)=>other is PackDatabase;
  public override int GetHashCode()=>typeof(PackDatabase).GetHashCode();
 }
 public partial class PackSystem:GameSystemBase
 {
  private static readonly string[] Modules=SignalModuleCatalog.Names;
  internal static string[] SourceImportPaths()=>Modules.Select(name=>"ProjectFiles/"+name).ToArray();
  internal static bool HasSourceModels(string root)
  {
   if(string.IsNullOrWhiteSpace(root))return false;
   foreach(string module in Modules)
   {
    string folder=Path.Combine(root,"Art","ProjectFiles",module);
    if(!File.Exists(Path.Combine(folder,"settings.json")))return false;
    for(int lod=0;lod<3;lod++)if(!File.Exists(Path.Combine(folder,module+(lod==0?"":"_LOD"+lod)+".fbx")))return false;
   }
   foreach(string suffix in new[]{"BaseColor","MaskMap","Normal","Emissive0","EmissiveID0"})if(!File.Exists(Path.Combine(root,"Art","Textures","SignalStyles_"+suffix+".png")))return false;
   return true;
  }
  private PrefabSystem prefabs;
  private EntityQuery templates,streetTemplates,nativeModules;
  private readonly Dictionary<string,StaticObjectPrefab> discovered=new Dictionary<string,StaticObjectPrefab>();
  private static readonly HashSet<string> ExpectedNames=new HashSet<string>(Modules.Select(n=>"CSG Module "+n),StringComparer.Ordinal);
  private double nextCatalogScan;
  private bool ambiguousCatalog;
  private EffectPrefab fixtureSource;
  private bool started,stopping;
  private System.Threading.CancellationToken termination;
  private Task importTask;
  private bool CancelImport=>stopping||termination.IsCancellationRequested||GameManager.instance==null||GameManager.instance.state==GameManager.State.Quitting||GameManager.instance.state==GameManager.State.Terminated||!World.IsCreated;
  private bool ImportProgress(string title,string detail,float value)=>CancelImport;
  private double nextProbe;
  private double preparedProbeStart=-1;
  internal static string DiscoveryStatus { get; private set; }="Waiting for native signal catalog";
  // Recovery: cached prefab registration is not yet proven to restore rendering.
  private static bool CacheRestoreEnabled=>false;
  internal static bool ValidModuleHeight(string name,float height)
  {
   bool fullSupport=name.StartsWith("CSGGardenArm",StringComparison.Ordinal)||name.StartsWith("CSGRigidArm",StringComparison.Ordinal)||name=="CSGWirePole";
   return !float.IsNaN(height)&&!float.IsInfinity(height)&&height>0&&height<=10.1f&&(!fullSupport||height>=6.5f);
  }
  private readonly Dictionary<EmissiveProperties.Purpose,EmissiveProperties.LightProperties> calibration=new Dictionary<EmissiveProperties.Purpose,EmissiveProperties.LightProperties>();
  private readonly List<PrefabBase> created=new List<PrefabBase>();
  protected override void OnCreate(){base.OnCreate();DiscoveryStatus="Waiting for native signal catalog";prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();nativeModules=GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),ComponentType.ReadOnly<StaticObjectData>(),ComponentType.Exclude<Game.Common.Deleted>());templates=GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),ComponentType.ReadOnly<TrafficLightData>());streetTemplates=GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),ComponentType.ReadOnly<StreetLightData>());}
  protected override void OnUpdate()
  {
   if(started||stopping||!(AppDomain.CurrentDomain.GetData("Curbside.Active.v1") is bool active&&active))return;
   if(Mod.PreparedPack||Environment.GetEnvironmentVariable("CURBSIDE_DEVELOPER_SIGNAL_IMPORT")!="1"){AdoptPreparedPack();return;}
   if(!HasSourceModels(Mod.Root)){started=true;Enabled=false;return;}
   AppDomain.CurrentDomain.SetData("Curbside.GardenCitySignals.ActiveModels.v1",true);
   if(templates.IsEmptyIgnoreFilter)return;
   if(Mod.LoadTimer.Elapsed.TotalSeconds<nextProbe)return;
   nextProbe=Mod.LoadTimer.Elapsed.TotalSeconds+1;
   StaticObjectPrefab template=null;
   using(var items=templates.ToEntityArray(Allocator.Temp))foreach(var entity in items)
   {
    if(!prefabs.TryGetPrefab<StaticObjectPrefab>(entity,out var candidate)||candidate==null||!candidate.isBuiltin)continue;
    if(candidate.name.IndexOf("TrafficLight",StringComparison.Ordinal)<0||candidate.name.IndexOf("Levelcrossing",StringComparison.OrdinalIgnoreCase)>=0)continue;
    var light=candidate.GetComponent<TrafficLightObject>();if(light==null)continue;
    foreach(var mesh in candidate.m_Meshes??Array.Empty<ObjectMeshInfo>())if(mesh.m_Mesh!=null)Mod.Log.Info("Native signal geometry: "+candidate.name+" mesh="+mesh.m_Mesh.name+" bounds="+(mesh.m_Mesh is RenderPrefab nativeRender?nativeRender.bounds.ToString():"group")+" offset="+mesh.m_Position+" rotation="+mesh.m_Rotation);
    foreach(var item in NativeMappings(candidate))if(!calibration.ContainsKey(item.Key))calibration[item.Key]=item.Value;
    if(!light.m_VehicleLeft&&!light.m_VehicleRight)continue;
    Mod.Log.Info("Native signal template "+candidate.name+" vehicle="+light.m_VehicleLeft+","+light.m_VehicleRight+" crossings="+light.m_CrossingLeft+","+light.m_CrossingRight);
    if(candidate.name=="NA_TrafficLightCarCrosswalk01")template=candidate;else if(template==null&&light.m_VehicleLeft&&light.m_VehicleRight&&light.m_CrossingLeft&&light.m_CrossingRight)template=candidate;
   }
   if(template==null)return;
   using(var items=streetTemplates.ToEntityArray(Allocator.Temp))foreach(var entity in items)if(prefabs.TryGetPrefab<StaticObjectPrefab>(entity,out var lamp)&&lamp.isBuiltin)foreach(var item in NativeMappings(lamp))if(item.Key==EmissiveProperties.Purpose.EffectSource&&!calibration.ContainsKey(item.Key))calibration[item.Key]=item.Value;
   using(var lamps=streetTemplates.ToEntityArray(Allocator.Temp))foreach(var entity in lamps)
   {
    if(!prefabs.TryGetPrefab<StaticObjectPrefab>(entity,out var lamp)||!lamp.isBuiltin||!lamp.TryGet<EffectSource>(out var effects)||effects.m_Effects==null)continue;
    foreach(var effect in effects.m_Effects)if(effect.m_Effect!=null&&effect.m_Effect.TryGet<Game.Prefabs.Effects.LightEffect>(out var light)){fixtureSource=effect.m_Effect;break;}
    if(fixtureSource!=null)break;
   }
   if(fixtureSource==null)return;
   started=true;Enabled=false;
   termination=GameManager.instance.terminationToken;
   // Shutdown waits for tracked work in TaskManager.CompleteAndClear. An
   // async-void importer would escape that wait and keep scheduling Unity jobs.
   importTask=Colossal.TaskManager.instance.SharedTask("Curbside.SignalAssetImport",()=>Import(template));
  }
  protected override void OnDestroy(){stopping=true;NativeSignalCatalog.Reset(prefabs);base.OnDestroy();}
  private void ScanNativeCatalog()
  {
   if(Mod.LoadTimer.Elapsed.TotalSeconds<nextCatalogScan)return;
   nextCatalogScan=Mod.LoadTimer.Elapsed.TotalSeconds+1;
   discovered.Clear();ambiguousCatalog=false;
   // Query the public prefab entities. Published PrefabIDs include the pack
   // identity; a name-only lookup deliberately cannot address those IDs.
   using(var items=nativeModules.ToEntityArray(Allocator.Temp))foreach(var entity in items)
   {
    if(!prefabs.TryGetPrefab<StaticObjectPrefab>(entity,out var model)||model==null||!ExpectedNames.Contains(model.name))continue;
    if(!Mod.PreparedPack&&!Published(model))continue;
    if(discovered.TryGetValue(model.name,out var existing)&&existing!=model){ambiguousCatalog=true;continue;}
    discovered[model.name]=model;
   }
  }
  private void AdoptPreparedPack()
  {
   if(Mod.LoadTimer.Elapsed.TotalSeconds<nextProbe)return;
   nextProbe=Mod.LoadTimer.Elapsed.TotalSeconds+.25;
   ScanNativeCatalog();
   // The native database and boot loader own all asset instances. Do not
   // populate the runtime database, clone donors, mutate metadata or import FBX.
   StaticObjectPrefab fallback=null;Entity fallbackEntity=Entity.Null;
   foreach(string name in new[]{"NA_TrafficLightCarCrosswalk01","NA_TrafficLightCar01","EU_TrafficLightCar01"})
    if(prefabs.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab),name),out var item)&&item is StaticObjectPrefab native&&native.isBuiltin&&prefabs.TryGetEntity(native,out fallbackEntity)&&EntityManager.Exists(fallbackEntity)){fallback=native;break;}
   bool published=false;
   if(!Mod.PreparedPack&&discovered.Count>0)
   {published=true;AppDomain.CurrentDomain.SetData("Curbside.GardenCitySignals.ActiveModels.v1",true);}
   if(!Mod.PreparedPack&&!published)
   {
    if(GameManager.instance!=null&&GameManager.instance.state>=GameManager.State.WorldReady)
    {
     if(preparedProbeStart<0)preparedProbeStart=Mod.LoadTimer.Elapsed.TotalSeconds;
     DiscoveryStatus="No published signal modules detected; checking native catalog during bounded startup grace";
     if(Mod.LoadTimer.Elapsed.TotalSeconds-preparedProbeStart<30)return;
     started=true;Enabled=false;DiscoveryStatus="Optional signal pack absent after startup grace; custom buttons hidden";
     AppDomain.CurrentDomain.SetData("Curbside.GardenCitySignals.ActiveModels.v1",false);Mod.Log.Info(DiscoveryStatus+". No source import or loading buttons.");
    }
    return;
   }
   var adopted=new List<Entity>();
   if(fallback!=null)foreach(string name in Modules)
   {
    if(ambiguousCatalog||!discovered.TryGetValue("CSG Module "+name,out var model)||model.m_Meshes==null||model.m_Meshes.Length!=1||model.m_Meshes[0].m_Mesh==null||!prefabs.TryGetEntity(model,out var entity)||!EntityManager.Exists(entity))break;
    if(!Mod.PreparedPack&&!Published(model))break; // Published modules may share declared asset-pack dependencies.
    adopted.Add(entity);
   }
   if(adopted.Count==Modules.Length)
   {
    NativeSignalCatalog.Register(prefabs,discovered);
    foreach(var entity in adopted)MedianGrassReplacement.Systems.SignalAssetSaveSystem.Register(entity,fallbackEntity);
    started=true;Enabled=false;DiscoveryStatus="Ready: "+adopted.Count+" native signal modules";
    Mod.Log.Info("Prepared signal pack adopted from native startup: modules="+adopted.Count+" uptime="+UnityEngine.Time.realtimeSinceStartup+". No FBX import, runtime database population or manual geometry loading.");
    return;
   }
   DiscoveryStatus="Signal modules "+adopted.Count+"/"+Modules.Length+"; ambiguous="+ambiguousCatalog+"; missing="+string.Join(",",Modules.Where(n=>!discovered.ContainsKey("CSG Module "+n)).Take(8));
   // Native prefab boot may take longer with a large playset. A missing-pack
   // deadline must not run while the game is still registering its assets.
   if(GameManager.instance==null||GameManager.instance.state<GameManager.State.WorldReady)return;
   if(preparedProbeStart<0)preparedProbeStart=Mod.LoadTimer.Elapsed.TotalSeconds;
   if(Mod.LoadTimer.Elapsed.TotalSeconds-preparedProbeStart<30)return;
   started=true;Enabled=false;
   AppDomain.CurrentDomain.SetData("Curbside.GardenCitySignals.ActiveModels.v1",false);
   Mod.Log.Error(DiscoveryStatus);
   Mod.Log.Error("Prepared signal pack incomplete after native boot: "+adopted.Count+"/"+Modules.Length+" modules. Custom buttons disabled; Curbside remains available. Refusing duplicate source import. Check asset pack installation and native asset errors.");
  }
  private static bool Published(StaticObjectPrefab model)
  {
   // Metadata belongs to the native AssetDatabase. Do not create a second
   // database or retain asset stream/header views for subscribed content.
   try{return model!=null&&!model.isBuiltin&&model.asset!=null&&!string.IsNullOrWhiteSpace(model.asset.GetMeta().platformID);}
   catch(Exception){return false;}
  }
  private sealed class Factory:IPrefabFactory
  {
   private readonly Dictionary<EmissiveProperties.Purpose,EmissiveProperties.LightProperties> native;
   internal Factory(Dictionary<EmissiveProperties.Purpose,EmissiveProperties.LightProperties> mappings){native=mappings;}
   internal readonly List<RenderPrefab> Roots=new List<RenderPrefab>();
   internal readonly List<PrefabBase> All=new List<PrefabBase>();
   internal readonly Dictionary<string,List<PrefabBase>> Groups=new Dictionary<string,List<PrefabBase>>();
   public T CreatePrefab<T>(string sourcePath,string assetName,int lodLevel)where T:PrefabBase
   {
    var result=PrefabBase.Create<T>(assetName);
    if(result is RenderPrefab prepared&&!assetName.StartsWith("CSGWireTimber",StringComparison.Ordinal))
    {
     var lights=prepared.AddComponent<EmissiveProperties>();
     lights.m_MultiLights=new List<EmissiveProperties.MultiLightMapping>();
     for(int layer=1;layer<=5;layer++)lights.m_MultiLights.Add(new EmissiveProperties.MultiLightMapping{layerId=layer});
     lights.m_SingleLights=new List<EmissiveProperties.SingleLightMapping>();
     BindLights(prepared,native);
    }
    All.Add(result);if(lodLevel==0&&result is RenderPrefab render)Roots.Add(render);
    string path=sourcePath.Replace('\\','/');string module=Modules.FirstOrDefault(name=>path.Contains("/"+name+"/")||path.EndsWith("/"+name,StringComparison.Ordinal)||assetName.StartsWith(name+" ",StringComparison.Ordinal));
    if(module==null)throw new InvalidDataException("Unrecognized signal source "+sourcePath+" / "+assetName);
    if(!Groups.TryGetValue(module,out var group)){group=new List<PrefabBase>();Groups.Add(module,group);}group.Add(result);return result;
   }
  }
  private async Task WaitForSave(){while(Mod.Saving&&!CancelImport)await Task.Delay(50,termination);}
  private async Task Import(StaticObjectPrefab template)
  {
   var previousDatabase=AssetImportPipeline.targetDatabase;var previousSeed=AssetImportPipeline.importSeed;bool previousParallel=AssetImportPipeline.useParallelImport;
   try
   {
    string art=Path.Combine(Mod.Root,"Art").Replace(Path.DirectorySeparatorChar,'/');if(!Directory.Exists(art))throw new DirectoryNotFoundException("Signal Art directory missing: "+art);
    Directory.CreateDirectory(Mod.Cache);
    var database=AssetDatabase<PackDatabase>.instance;await AssetDatabase.global.RegisterDatabase(database);
    if(CacheRestoreEnabled&&File.Exists(Path.Combine(Mod.Cache,"complete-v13.txt")))
    {
      await database.PopulateFromDataSource(true,System.Threading.CancellationToken.None,Colossal.TaskManager.instance.progress.GetSubProgress());
      await database.PopulateFromDataSource(false,System.Threading.CancellationToken.None,Colossal.TaskManager.instance.progress.GetSubProgress());
      var cached=database.GetAssets<PrefabAsset>().Select(a=>a.Load()).OfType<PrefabBase>().ToList();
      if(cached.OfType<StaticObjectPrefab>().Count(p=>p.name.StartsWith("CSG Module ",StringComparison.Ordinal))!=Modules.Length)throw new InvalidDataException("Cached pack is incomplete; refusing partial registration.");
      var renders=cached.OfType<RenderPrefab>().ToDictionary(p=>p.GetPrefabID());
      foreach(var p in renders.Values)if(p.TryGet<LodProperties>(out var lod)&&lod.m_LodMeshes!=null)
       for(int i=0;i<lod.m_LodMeshes.Length;i++)if(lod.m_LodMeshes[i]!=null&&renders.TryGetValue(lod.m_LodMeshes[i].GetPrefabID(),out var canonical))lod.m_LodMeshes[i]=canonical;
      foreach(var p in cached.OfType<StaticObjectPrefab>())
      {
       ConfigureIdentity(p);
       foreach(var mesh in p.m_Meshes)if(mesh.m_Mesh!=null&&renders.TryGetValue(mesh.m_Mesh.GetPrefabID(),out var canonical))mesh.m_Mesh=canonical;
       SavePrefab(database,p);
      }
      foreach(var p in cached.OrderBy(p=>p is StaticObjectPrefab?1:0)){await WaitForSave();if(CancelImport)return;if(!prefabs.TryGetPrefab(p.GetPrefabID(),out _)&&!prefabs.AddPrefab(p))throw new InvalidOperationException("Failed cached registration "+p.name);created.Add(p);}
      Mod.Log.Info("READY cached Garden City pack; no FBX import or texture decoding requested. Startup seconds="+Mod.LoadTimer.Elapsed.TotalSeconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture));return;
    }
    AssetImportPipeline.targetDatabase=database;AssetImportPipeline.importSeed="GardenCitySignals-v12-capuv";AssetImportPipeline.useParallelImport=false;
    // Fixture effects are shared directly from native streetlight donors.
    SurfaceAsset[] sharedSurface=null;
    var nativeLights=calibration;
    Mod.Log.Info("Source import (tracked task, shutdown cancellation enabled): one batch for all "+Modules.Length+" modules; cache restoration remains disabled.");
    var factory=new Factory(nativeLights);var importWatch=System.Diagnostics.Stopwatch.StartNew();
    await AssetImportPipeline.ImportPath(art,SourceImportPaths(),ImportMode.All,false,ImportProgress,factory);
    if(CancelImport){Mod.Log.Info("Signal asset import canceled during shutdown; incomplete modules were not published as ready.");return;}
    foreach(string name in Modules)if(!factory.Groups.ContainsKey(name))throw new InvalidDataException("Batch import omitted "+name+"; registration stopped.");
    Mod.Log.Info("Source batch complete: modules="+factory.Groups.Count+" elapsed seconds="+importWatch.Elapsed.TotalSeconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture));
    foreach(string name in Modules)
    {
     if(CancelImport){Mod.Log.Info("Signal asset import canceled during shutdown; incomplete modules were not published as ready.");return;}
     if(!factory.Groups.TryGetValue(name,out var group))throw new InvalidDataException(name+": module missing from batch import.");
     var roots=group.OfType<RenderPrefab>().Where(p=>factory.Roots.Contains(p)).ToArray();
     if(roots.Length!=1)throw new InvalidDataException(name+": expected one imported root mesh, got "+roots.Length);
     RenderPrefab render=roots[0];
     Mod.Log.Info(name+": imported bounds min="+render.bounds.min+" max="+render.bounds.max+" vertices="+render.vertexCount);
     float height=render.bounds.max.y-render.bounds.min.y;
     if(!ValidModuleHeight(name,height))throw new InvalidDataException(name+": unexpected import scale; registration stopped.");
     if(!render.hasGeometryAsset||render.vertexCount<=0||render.meshCount!=1||render.surfaceAssets.Count()!=1)throw new InvalidDataException(name+": incomplete geometry/material data; registration stopped.");
     if(sharedSurface==null)sharedSurface=render.surfaceAssets.ToArray();
     // Pedestrian import bakes a different emission-layer layout; retain that surface.
     var moduleSurface=name=="CSGWireTimber"||name=="CSGPedHead"||name=="CSGLampArm"||name.EndsWith("Luminaire",StringComparison.Ordinal)?render.surfaceAssets.ToArray():sharedSurface;
     await WaitForSave();if(CancelImport)return;
     foreach(var p in group.OfType<RenderPrefab>())
     {
      // Imported FBXs already contain the matte timber UVs. Never read or rewrite native geometry here.
      p.surfaceAssets=moduleSurface;
      if(name=="CSGWireTimber")p.Remove<EmissiveProperties>();else BindLights(p,nativeLights);
      SavePrefab(database,p);
      if(!prefabs.AddPrefab(p))throw new InvalidOperationException("Failed to register "+p.name);
      created.Add(p);
     }
     var signal=(StaticObjectPrefab)template.Clone("CSG Module "+name);
     signal.m_Meshes=new[]{new ObjectMeshInfo{m_Mesh=render}};
     signal.Remove<SubObjectDefaultProbability>();signal.Remove<ObjectSubObjects>();
     ConfigureIdentity(signal);
     var signalFlags=signal.GetComponent<TrafficLightObject>();signalFlags.m_VehicleLeft=false;signalFlags.m_VehicleRight=false;signalFlags.m_CrossingLeft=false;signalFlags.m_CrossingRight=false;
     if(signal.TryGet<UIObject>(out var ui))ui.m_Group=null;
     // Reach is selected by Curbside per approach; native signal state components remain.
     SavePrefab(database,signal);
     if(!prefabs.AddPrefab(signal))throw new InvalidOperationException("Failed to register "+signal.name);
     created.Add(signal);
     MedianGrassReplacement.Systems.SignalAssetSaveSystem.Register(prefabs.GetEntity(signal),prefabs.GetEntity(template));
     Mod.Log.Info("READY "+signal.name+" vertices="+render.vertexCount+" indices="+render.indexCount+" sharedSurface="+sharedSurface[0].id);
    }
    File.WriteAllText(Path.Combine(Mod.Cache,"complete-v13.txt"),Modules.Length.ToString());
    Mod.Log.Info("READY all "+Modules.Length+" shared signal modules; startup seconds="+Mod.LoadTimer.Elapsed.TotalSeconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+". Live visual/state checks still required.");
   }
   catch(OperationCanceledException){Mod.Log.Info("Signal asset import canceled by game termination; incomplete modules were not published as ready.");}
   catch(Exception error){Mod.Log.Error(error,"Garden City import stopped. Incomplete configuration was not advertised as ready.");}
   finally{AssetImportPipeline.targetDatabase=previousDatabase;AssetImportPipeline.importSeed=previousSeed;AssetImportPipeline.useParallelImport=previousParallel;}
  }
  private static void ConfigureIdentity(StaticObjectPrefab signal)
  {
   // These are render donors, never alternatives in vanilla prop placeholders.
   signal.Remove<SpawnableObject>();
   signal.Remove<PlaceableObject>();
   signal.Remove<EffectSource>();
   // A cloned vanilla object's historical names must never alias vanilla signals.
   signal.Remove<ObsoleteIdentifiers>();
   signal.AddOrGetComponent<ObsoleteIdentifiers>().m_PrefabIdentifiers=new[]{new PrefabIdentifierInfo{m_Name=signal.name,m_Type=nameof(StaticObjectPrefab)}};
  }
  private static void SavePrefab(ILocalAssetDatabase database,PrefabBase prefab)
  {
   var path=AssetDataPath.Create("Prefabs",prefab.name);
   if(!database.Exists<PrefabAsset>(path,out var asset))asset=database.AddAsset(path,prefab);
   prefab.asset=asset;asset.Save(force:true);
  }
  private static Dictionary<EmissiveProperties.Purpose,EmissiveProperties.LightProperties> NativeMappings(StaticObjectPrefab template)
  {
   var map=new Dictionary<EmissiveProperties.Purpose,EmissiveProperties.LightProperties>();
   foreach(var mesh in template.m_Meshes??Array.Empty<ObjectMeshInfo>())
   {
    if(mesh.m_Mesh==null||!mesh.m_Mesh.TryGet<EmissiveProperties>(out var component))continue;
    if(component.m_MultiLights!=null)foreach(var item in component.m_MultiLights)map[item.purpose]=item;
    if(component.m_SingleLights!=null)foreach(var item in component.m_SingleLights)map[item.purpose]=item;
   }
   return map;
  }
  private static void BindLights(RenderPrefab render,Dictionary<EmissiveProperties.Purpose,EmissiveProperties.LightProperties> native)
  {
   var purposes=new[]{EmissiveProperties.Purpose.TrafficLight_Red,EmissiveProperties.Purpose.TrafficLight_Yellow,EmissiveProperties.Purpose.TrafficLight_Green,EmissiveProperties.Purpose.PedestrianLight_Stop,EmissiveProperties.Purpose.PedestrianLight_Walk};
   var props=render.GetComponent<EmissiveProperties>();
   if(props?.m_MultiLights==null||props.m_MultiLights.Count!=5)throw new InvalidDataException("Expected five declared light layers before registering "+render.name);
   var layers=new HashSet<int>();
   foreach(var light in props.m_MultiLights)
   {
    if(light.layerId<1||light.layerId>5||!layers.Add(light.layerId))throw new InvalidDataException("Unexpected or duplicate signal layer "+light.layerId);
    light.purpose=(render.name.StartsWith("CSGLampArm",StringComparison.Ordinal)||render.name.IndexOf("Luminaire",StringComparison.Ordinal)>=0)?EmissiveProperties.Purpose.EffectSource:purposes[light.layerId-1];light.previewState=false;light.animationIndex=-1;light.colorOff=Color.black;
    if(native.TryGetValue(light.purpose,out var source)){light.intensity=source.intensity;light.luminance=source.luminance;light.color=source.color;light.colorOff=source.colorOff;light.responseTime=source.responseTime;}
    else throw new InvalidDataException("Native signal template has no calibrated mapping for "+light.purpose);
   }
   props.m_SingleLights=new List<EmissiveProperties.SingleLightMapping>();
  }
 }
}
