using System;

using System.Collections.Generic;

using Game;

using Game.Prefabs;

using Unity.Collections;

using Unity.Entities;

namespace MedianGrassReplacement.Systems

{

 // Private visual definitions reference built-in meshes. No road/lane host is edited.

 public partial class YellowPaintDefinitionSystem : GameSystemBase

 {

  private PrefabSystem _prefabs;private EntityQuery _geometry;

  private readonly Dictionary<Entity,NetLaneGeometryPrefab> _variants=new Dictionary<Entity,NetLaneGeometryPrefab>();

  private readonly Dictionary<Entity,NetLaneGeometryPrefab> _whiteVariants=new Dictionary<Entity,NetLaneGeometryPrefab>();
  private int _count=-1,_scans,_sourceCount,_meshCount;private bool _waiting;
  internal string DiscoveryStatus=>"scans="+_scans+" geometry="+_count+" sources="+_sourceCount+" meshes="+_meshCount;

  private NetLaneGeometryPrefab _solid,_doubleYellow,_white,_doubleWhite,_dashedWhite;internal Entity Solid=>Ready(_solid);
  private Entity _nativeSolid,_nativeDoubleYellow,_nativeWhite,_nativeDoubleWhite,_nativeDashed;
  internal Entity Divider{get{var own=Ready(_dashedWhite);return own!=Entity.Null?own:ReadyEntity(_nativeDashed);}}
  internal Entity Explicit(byte style,bool yellow)
  {
   var own=Ready(yellow?(style==2?_doubleYellow:_solid):(style==2?_doubleWhite:_white));
   return own!=Entity.Null?own:ReadyEntity(yellow?(style==2?_nativeDoubleYellow:_nativeSolid):(style==2?_nativeDoubleWhite:_nativeWhite));
  }
  private Entity ReadyEntity(Entity entity)=>entity!=Entity.Null&&EntityManager.HasComponent<NetLaneGeometryData>(entity)&&EntityManager.HasComponent<NetLaneArchetypeData>(entity)&&EntityManager.GetComponentData<NetLaneArchetypeData>(entity).m_LaneArchetype.Valid?entity:Entity.Null;

  private Entity Ready(NetLaneGeometryPrefab prefab)=>prefab!=null&&_prefabs.TryGetEntity(prefab,out var entity)&&EntityManager.HasComponent<NetLaneGeometryData>(entity)&&EntityManager.HasComponent<NetLaneArchetypeData>(entity)&&EntityManager.GetComponentData<NetLaneArchetypeData>(entity).m_LaneArchetype.Valid?entity:Entity.Null;

  internal Entity WhiteVariant(Entity source)
  {
   if(source!=Entity.Null&&source==Ready(_solid))return Ready(_white);
   if(source!=Entity.Null&&source==Ready(_doubleYellow))return Ready(_doubleWhite);
   return _whiteVariants.TryGetValue(source,out var prefab)?Ready(prefab):Entity.Null;
  }
  internal Entity Variant(Entity source)=>_variants.TryGetValue(source,out var prefab)?Ready(prefab):Entity.Null;

  protected override void OnCreate(){base.OnCreate();_prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();_geometry=GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),ComponentType.ReadOnly<NetLaneGeometryData>(),ComponentType.ReadOnly<NetLaneArchetypeData>());}
  protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose,GameMode mode){base.OnGamePreload(purpose,mode);_variants.Clear();_whiteVariants.Clear();_count=-1;_waiting=true;}
  protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose,GameMode mode){base.OnGameLoadingComplete(purpose,mode);_count=-1;_waiting=true;Enabled=true;}

  private NetLaneGeometryPrefab Clone(NetLaneGeometryPrefab source,NetLaneMeshInfo[] meshes,string name)

  {if(_prefabs.TryGetPrefab(new PrefabID("NetLaneGeometryPrefab",name),out var registered)&&registered is NetLaneGeometryPrefab existing)return existing;var copy=(NetLaneGeometryPrefab)source.Clone(name);copy.m_Meshes=meshes;copy.Remove<ObsoleteIdentifiers>();copy.components?.RemoveAll(c=>c is SecondaryLane);_prefabs.AddPrefab(copy);_waiting=true;return copy;}

  private NetLaneGeometryPrefab BuildWhite(NetLaneGeometryPrefab source,Dictionary<string,RenderPrefab> meshes)
  {
   bool changed=false;var result=new NetLaneMeshInfo[source.m_Meshes.Length];
   for(int i=0;i<result.Length;i++)
   {
    var original=source.m_Meshes[i];if(original?.m_Mesh==null)return null;
    string name=original.m_Mesh.name.Replace("Yellow","White");var mesh=original.m_Mesh;
    if(name!=mesh.name){if(!meshes.TryGetValue(name,out mesh))return null;changed=true;}
    result[i]=new NetLaneMeshInfo{m_Mesh=mesh,m_RequireSafe=original.m_RequireSafe,m_RequireClear=original.m_RequireClear,m_RequireEditor=original.m_RequireEditor,m_RequireLevelCrossing=original.m_RequireLevelCrossing,m_RequireTrackCrossing=original.m_RequireTrackCrossing,m_RequireLeftHandTraffic=original.m_RequireLeftHandTraffic,m_RequireRightHandTraffic=original.m_RequireRightHandTraffic};
   }
   return changed?Clone(source,result,"MGR White Paint "+source.name):null;
  }

  private IEnumerable<NetLaneGeometryPrefab> PendingDefinitions()
  {
   yield return _solid;yield return _doubleYellow;yield return _white;yield return _doubleWhite;
   yield return _dashedWhite;
   foreach(var prefab in _variants.Values)yield return prefab;
   foreach(var prefab in _whiteVariants.Values)yield return prefab;
  }

  protected override void OnUpdate()

  {

   if(Mod.Saving)return;

   if(_waiting)
   {
    bool ready=true,changed=false;
    foreach(var prefab in PendingDefinitions())
    {
     if(prefab==null)continue;
     var entity=Ready(prefab);if(entity==Entity.Null){ready=false;continue;}
     if(!EntityManager.HasComponent<MGRPrivatePaintDefinition>(entity)){EntityManager.AddComponent<MGRPrivatePaintDefinition>(entity);changed=true;}
    }
    _waiting=!ready;
    if(changed)
    {
     CurbLineEmissionSystem.RequestRoadRefresh();IntersectionPaintSystem.RequestRefresh();
     if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("paint.ready","singleYellow="+(Explicit(1,true)!=Entity.Null)+" doubleYellow="+(Explicit(2,true)!=Entity.Null)+" singleWhite="+(Explicit(1,false)!=Entity.Null)+" doubleWhite="+(Explicit(2,false)!=Entity.Null));
    }
   }

   int count=_geometry.CalculateEntityCount();if(count==_count)return;_count=count;_scans++;

   var sources=new Dictionary<Entity,NetLaneGeometryPrefab>();var meshes=new Dictionary<string,RenderPrefab>(StringComparer.Ordinal);

   using(var entities=_geometry.ToEntityArray(Allocator.Temp))foreach(var entity in entities)

   {

    if(!_prefabs.TryGetPrefab<NetLaneGeometryPrefab>(entity,out var prefab)||prefab==null||prefab.m_Meshes==null||prefab.name.StartsWith("MGR ",StringComparison.Ordinal))continue;
    // Asset origin is not a paint capability. Runtime-loaded game definitions may
    // not report isBuiltin. Never clone a definition that also routes traffic.
    if(!EntityManager.HasComponent<NetLaneData>(entity)||(EntityManager.GetComponentData<NetLaneData>(entity).m_Flags&(LaneFlags.Road|LaneFlags.Parking|LaneFlags.Pedestrian|LaneFlags.Track|LaneFlags.Master))!=0)continue;

    sources[entity]=prefab;foreach(var info in prefab.m_Meshes)if(info?.m_Mesh!=null)meshes[info.m_Mesh.name]=info.m_Mesh;
    // Native, unconditional paint is usable immediately while private clones
    // register. Never substitute a single stripe for a requested double stripe.
    if(NodeLinePolicy.IsLineGeometry(prefab)&&prefab.m_Meshes.Length==1)
    {
     var info=prefab.m_Meshes[0];
     if(info?.m_Mesh!=null&&!info.m_RequireSafe&&!info.m_RequireClear&&!info.m_RequireEditor&&!info.m_RequireLevelCrossing&&!info.m_RequireTrackCrossing&&!info.m_RequireLeftHandTraffic&&!info.m_RequireRightHandTraffic)
     {
      switch(info.m_Mesh.name)
      {
       case "Yellow Solid Line Mesh":_nativeSolid=entity;break;
       case "Yellow Double Solid Line Mesh":_nativeDoubleYellow=entity;break;
       case "White Solid Line Mesh":_nativeWhite=entity;break;
       case "White Double Solid Line Mesh":_nativeDoubleWhite=entity;break;
       case "White Dashed Line Mesh":_nativeDashed=entity;break;
      }
     }
    }

   }

   _sourceCount=sources.Count;_meshCount=meshes.Count;
   foreach(var pair in sources)

   {

    var source=pair.Value;

    if(!NodeLinePolicy.IsLineGeometry(source))continue;

    if(_solid==null&&meshes.TryGetValue("Yellow Solid Line Mesh",out var solid))_solid=Clone(source,new[]{new NetLaneMeshInfo{m_Mesh=solid}},"MGR Native Yellow Median Line");
    if(_doubleYellow==null&&meshes.TryGetValue("Yellow Double Solid Line Mesh",out var yellowPair))_doubleYellow=Clone(source,new[]{new NetLaneMeshInfo{m_Mesh=yellowPair}},"MGR Explicit Yellow Double Line");
    if(_doubleWhite==null&&meshes.TryGetValue("White Double Solid Line Mesh",out var whitePair))_doubleWhite=Clone(source,new[]{new NetLaneMeshInfo{m_Mesh=whitePair}},"MGR Explicit White Double Line");
    if(_white==null&&meshes.TryGetValue("White Solid Line Mesh",out var whiteSingle))_white=Clone(source,new[]{new NetLaneMeshInfo{m_Mesh=whiteSingle}},"MGR Explicit White Single Line");
    if(_dashedWhite==null&&meshes.TryGetValue("White Dashed Line Mesh",out var dashed))_dashedWhite=Clone(source,new[]{new NetLaneMeshInfo{m_Mesh=dashed}},"MGR Explicit White Lane Divider");

    bool changed=false,complete=true;var replacement=new NetLaneMeshInfo[source.m_Meshes.Length];

    for(int i=0;i<replacement.Length;i++)

    {

     var original=source.m_Meshes[i];if(original?.m_Mesh==null){complete=false;break;}

     var name=NorthAmericanLinePolicy.YellowMeshKey(original.m_Mesh.name);var mesh=original.m_Mesh;

     if(name!=mesh.name){if(!meshes.TryGetValue(name,out mesh)){complete=false;break;}changed=true;}

     replacement[i]=new NetLaneMeshInfo{m_Mesh=mesh,m_RequireSafe=original.m_RequireSafe,m_RequireClear=original.m_RequireClear,m_RequireEditor=original.m_RequireEditor,m_RequireLevelCrossing=original.m_RequireLevelCrossing,m_RequireTrackCrossing=original.m_RequireTrackCrossing,m_RequireLeftHandTraffic=original.m_RequireLeftHandTraffic,m_RequireRightHandTraffic=original.m_RequireRightHandTraffic};

    }

    if(complete&&changed)_variants[pair.Key]=Clone(source,replacement,"MGR Yellow Paint "+source.name);

    var whiteVariant=BuildWhite(source,meshes);if(whiteVariant!=null)_whiteVariants[pair.Key]=whiteVariant;


   }

  }

 }

}

