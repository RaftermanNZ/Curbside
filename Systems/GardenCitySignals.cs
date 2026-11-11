using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Serialization.Entities;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using SubLane=Game.Net.SubLane;
using PedestrianLane=Game.Net.PedestrianLane;
using ObjectTransform=Game.Objects.Transform;
namespace MedianGrassReplacement.Systems
{
 public struct MGRGardenCitySignals:IComponentData,IEmptySerializable{}
 public struct MGRSignalStyle:IComponentData,ISerializable
 {
  public byte Value;
  public void Serialize<T>(T writer)where T:IWriter{writer.Write(Value);}
  public void Deserialize<T>(T reader)where T:IReader{reader.Read(out Value);Value=global::GardenCitySignals.FloridaCatalogPolicy.NormalizeStyle(Value);}
 }
 internal static partial class GardenCityLightPolicy
 {
  internal const string AssetName="MGR Garden City Traffic Lights",WireAsset="MGR Span Wire Traffic Lights",MastAsset="MGR Rigid Mast Traffic Lights";
  internal const string MetalWireAsset="MGR Metal Span Wire Traffic Lights";
  internal const string FloridaAsset="MGR Florida Horizontal Traffic Lights";
  internal static readonly float[] Reaches={3,4.5f,6,7.5f,9,10.5f,12,13.5f,15,16.5f,18,19.5f,21,22.5f,24,25.5f,27,28.5f,30};
  private static PrefabSystem owner;
  private static readonly Dictionary<string,StaticObjectPrefab> modules=new Dictionary<string,StaticObjectPrefab>();
  private static readonly Dictionary<string,Entity> variants=new Dictionary<string,Entity>();
  private static readonly Dictionary<string,StaticObjectPrefab> pending=new Dictionary<string,StaticObjectPrefab>();
  private static readonly Dictionary<string,Entity> pendingSources=new Dictionary<string,Entity>();
  private static readonly Dictionary<Entity,Entity> nativeSources=new Dictionary<Entity,Entity>();
  internal static Entity NativeSource(Entity prefab)
  {
   for(int i=0;i<16&&nativeSources.TryGetValue(prefab,out var source)&&source!=prefab;i++)prefab=source;
   return prefab;
  }
  private static readonly HashSet<Entity> pendingNodes=new HashSet<Entity>();
  private static readonly HashSet<string> notices=new HashSet<string>();
  internal static bool TraceEnabled=>ModDiagnostics.TraceBuffer.Enabled&&notices.Count<128;
  internal static void Trace(Entity node,string stage)
  {
   if(!TraceEnabled)return;
   string message=node+" "+stage;if(notices.Add(message))Mod.Log.Info("Signal placement: "+message);
  }
  internal static bool IsAsset(string name)=>name==AssetName||name==WireAsset||name==MastAsset||name==MetalWireAsset||name==FloridaAsset;
  internal static string CacheStatus=>"signalVariants="+variants.Count+" pending="+pending.Count+" modules="+modules.Count+" capacity=2048";
  internal static bool PackPresent()
  {
   return AppDomain.CurrentDomain.GetData("Curbside.GardenCitySignals.ActiveModels.v1") is bool present&&present;
  }
  internal static byte Style(string name)=>(byte)(name==FloridaAsset?4:name==MetalWireAsset?3:name==WireAsset?1:name==MastAsset?2:0);
  internal static bool IsWireStyle(byte style)=>style==1||style==3;
  internal static bool IsRigidStyle(byte style)=>style==2||style==4;
  internal static bool IsMetalWireStyle(byte style)=>style==3;
  internal static bool IsTimberWireStyle(byte style)=>style==1;
  internal static bool WireAssemblyReady(byte style,bool cableReady)=>!IsWireStyle(style)||cableReady;
  // Match the millimetre tolerance in lane fitting. Rotating a junction must
  // not select a longer mesh (or another bank) from sub-millimetre roundoff.
  internal static int Size(float reach){if(!math.isfinite(reach)||reach<=0)return -1;for(int i=0;i<Reaches.Length;i++)if(Reaches[i]+.001f>=reach)return i;return -1;}
  private static IEnumerable<string> Names(){foreach(string name in global::GardenCitySignals.SignalModuleCatalog.Names)yield return name.Substring(3);}
  private static void UseWorld(PrefabSystem p){if(owner!=p){owner=p;modules.Clear();variants.Clear();pending.Clear();pendingSources.Clear();nativeSources.Clear();pendingNodes.Clear();notices.Clear();}}
  internal static bool Ready(PrefabSystem p,EntityManager em)
  {
   UseWorld(p);
   if(modules.Count>=global::GardenCitySignals.SignalModuleCatalog.Names.Length)return true;
   var loaded=new Dictionary<string,StaticObjectPrefab>();
   foreach(var name in Names())
   {
    if(!global::GardenCitySignals.NativeSignalCatalog.TryGet(p,"CSG Module CSG"+name,out var model))
    {
     if(Environment.GetEnvironmentVariable("CURBSIDE_DEVELOPER_SIGNAL_IMPORT")!="1"||!p.TryGetPrefab(new PrefabID("StaticObjectPrefab","CSG Module CSG"+name),out var legacy)||!(legacy is StaticObjectPrefab authored))return false;
     model=authored;
    }
    if(model.m_Meshes==null||model.m_Meshes.Length!=1||!p.TryGetEntity(model,out var entity)||!em.Exists(entity))return false;
    loaded.Add(name,model);
   }
   foreach(var item in loaded)modules[item.Key]=item.Value;return true;
  }
  // Test24's pedestrian rear mount reaches 0.352 m, selecting the legacy 0.44 m offset.
  // Added bracket bounds must not move the optical face away from that anchor.
  internal static float PedestrianMountOffset(float depth)=>depth>.33f?.44f:.28f;
  internal static bool ReadyStyle(PrefabSystem p,EntityManager em,string asset)=>Style(asset)==4?FloridaReady(p,em):Ready(p,em);
  internal static bool FloridaReady(PrefabSystem p,EntityManager em)
  {
   if(!Ready(p,em))return false;
   foreach(var name in global::GardenCitySignals.FloridaModuleCatalog.Names)
   {
    if(!global::GardenCitySignals.FloridaNativeCatalog.TryGet(p,"CSF Module "+name,out var model)||model.m_Meshes==null||model.m_Meshes.Length!=1||model.m_Meshes[0].m_Mesh==null||!p.TryGetEntity(model,out var entity)||!em.Exists(entity))return false;
   }
   foreach(var name in global::GardenCitySignals.FloridaModuleCatalog.Names){global::GardenCitySignals.FloridaNativeCatalog.TryGet(p,"CSF Module "+name,out var model);modules[name]=model;}
   return true;
  }
  private static float PedestrianOffset=>.44f;
  private static ObjectMeshInfo Mesh(string name,float3 position,quaternion rotation)=>new ObjectMeshInfo{m_Mesh=modules[name].m_Meshes[0].m_Mesh,m_Position=position,m_Rotation=rotation};
  internal static int Nearest(ObjectTransform source,IList<FarSideLightPolicy.Approach> approaches)
   =>NearestForPhase(source,approaches,0);
  internal static int NearestForPhase(ObjectTransform source,IList<FarSideLightPolicy.Approach> approaches,ushort mask)
  {
   int selected=-1;float best=144;
   for(int i=0;i<approaches.Count;i++){var a=approaches[i];if(mask!=0&&(a.Incoming==null||a.LaneMasks==null||a.LaneMasks.Count!=a.Incoming.Count||!SignalSupportGroupPolicy.FullyMapped(mask,a.LaneMasks)))continue;float d=math.min(math.distancesq(source.m_Position.xz,a.Left.xz),math.distancesq(source.m_Position.xz,a.Right.xz));if(d<best){best=d;selected=i;}}
   return selected;
  }
  internal static bool TryLayout(IList<float3> lanes,float2 outward,float3 pole,out float3 inward,out float[] distances)
  {
   inward=default;distances=null;if(lanes==null||lanes.Count<1||lanes.Count>16||!math.all(math.isfinite(outward)))return false;
   float2 lateral=new float2(-outward.y,outward.x);float mean=0;foreach(var lane in lanes)mean+=math.dot(lane.xz-pole.xz,lateral);mean/=lanes.Count;if(math.abs(mean)<.2f)return false;
   lateral*=math.sign(mean);inward=new float3(lateral.x,0,lateral.y);distances=new float[lanes.Count];
   // Millimetre tolerance prevents rotated copies of an otherwise identical
   // junction from accepting different banks at the exact clearance boundary.
   for(int i=0;i<lanes.Count;i++){float d=math.dot(lanes[i].xz-pole.xz,lateral);if(!math.isfinite(d)||d<.349f||d>29.501f)return false;distances[i]=math.clamp(d,.35f,29.5f);}
   Array.Sort(distances);for(int i=1;i<distances.Length;i++)if(distances[i]-distances[i-1]<1.2f)return false;return true;
  }
  // Shared art eligibility for planning and actual appearance. A rejected
  // source must retain its native visual until its own movement is replaced.
  internal static bool HeadClearance(FarSideLightPolicy.Approach road,float3 pole,float3 inward,byte style)
  {
   if(!global::GardenCitySignals.SignalPackPolicy.UseRigidSignLayout||style==2||style==4)return true;
   if(road.Incoming==null||road.Incoming.Count==0)return false;
   foreach(var lane in road.Incoming)
    if(!global::GardenCitySignals.SignalMountClearance.AllowHead(style,true,math.dot(lane.xz-pole.xz,inward.xz)))return false;
   return true;
  }
  internal static bool AppearanceCandidate(FarSideLightPolicy.Approach road,float3 pole,byte style)
   =>style<=4&&TryLayout(road.Incoming,road.Outward,pole,out var inward,out var heads)&&HeadClearance(road,pole,inward,style);
  internal static FarSideLightPolicy.Approach PhaseApproach(FarSideLightPolicy.Approach source,ushort mask)
  {
   if(mask==0)return source;
   var points=new List<float3>();var masks=new List<ushort>();
   if(source.Incoming!=null&&source.LaneMasks!=null&&source.LaneMasks.Count==source.Incoming.Count&&SignalSupportGroupPolicy.FullyMapped(mask,source.LaneMasks))
    for(int i=0;i<source.Incoming.Count;i++)if((source.LaneMasks[i]&mask)!=0){points.Add(source.Incoming[i]);masks.Add(source.LaneMasks[i]);}
   // Missing or partly mapped phases retain native rendering. A nearby matching
   // bit is not enough evidence to render only part of a controller's lane bank.
   source.Incoming=points;source.LaneMasks=masks;return source;
  }
  internal static bool TryShoulderBounds(IList<float2> lanes,float width,out float left,out float right)
  {
   left=float.MaxValue;right=float.MinValue;if(lanes==null||lanes.Count==0||!math.isfinite(width)||width<1)return false;
   foreach(var lane in lanes){if(!math.all(math.isfinite(lane))||lane.y<=0)return false;left=math.min(left,lane.x-lane.y*.5f);right=math.max(right,lane.x+lane.y*.5f);}
   if(left< -width*.5f-.1f||right>width*.5f+.1f)return false;
   left=math.max(-width*.5f,left-.75f);right=math.min(width*.5f,right+.75f);return right-left>1;
  }
  internal static bool CrossingMatches(ushort mask,ushort required)=>mask!=0&&(required==0||(mask&required)!=0);
  internal struct CrossingFace {internal float3 Facing;internal ushort Mask;internal float Distance;}
  internal static bool DistinctFace(float3 a,float3 b)=>math.dot(a,b)<.8f;
  internal struct CrossingPath { internal float3 Start,End; internal ushort Mask; }
  internal static List<CrossingPath> ReadCrossingPaths(EntityManager em,Entity node)
  {
   var paths=new List<CrossingPath>();if(!em.HasBuffer<SubLane>(node))return paths;
   foreach(var item in em.GetBuffer<SubLane>(node,true))
   {
    var lane=item.m_SubLane;if(!em.Exists(lane)||!em.HasComponent<PedestrianLane>(lane)||!em.HasComponent<LaneSignal>(lane)||!em.HasComponent<Curve>(lane)||(em.GetComponentData<PedestrianLane>(lane).m_Flags&PedestrianLaneFlags.Crosswalk)==0)continue;
    var mask=em.GetComponentData<LaneSignal>(lane).m_GroupMask;if(mask==0)continue;
    var curve=em.GetComponentData<Curve>(lane).m_Bezier;paths.Add(new CrossingPath{Start=curve.a,End=curve.d,Mask=mask});
   }
   return paths;
  }
  internal static List<CrossingFace> CrossingFaces(EntityManager em,Entity node,float3 pole)
   =>ChooseCrossingFaces(ReadCrossingPaths(em,node),pole);
  internal static List<CrossingFace> ChooseCrossingFaces(IList<CrossingPath> paths,float3 pole)
  {
   var choices=new List<CrossingFace>();var result=new List<CrossingFace>();
   foreach(var path in paths)
   {
    bool first=math.distancesq(pole.xz,path.Start.xz)<math.distancesq(pole.xz,path.End.xz);var end=first?path.Start:path.End;var other=first?path.End:path.Start;float distance=math.distancesq(pole.xz,end.xz);
    var facing=math.normalizesafe(new float3(other.x-end.x,0,other.z-end.z));if(distance>576||math.lengthsq(facing)<.5f)continue;
    choices.Add(new CrossingFace{Facing=facing,Mask=path.Mask,Distance=distance});
   }
   choices.Sort((x,y)=>x.Distance.CompareTo(y.Distance));
   foreach(var face in choices){bool distinct=true;foreach(var accepted in result)if(!DistinctFace(face.Facing,accepted.Facing))distinct=false;if(distinct)result.Add(face);if(result.Count==2)break;}
   return result;
  }

  internal static int WireRingNeighbor(IList<float3> supports,int origin)
  {
   if(supports==null||supports.Count<2||origin<0||origin>=supports.Count)return -1;
   float3 centre=float3.zero;foreach(var p in supports)centre+=p;centre/=supports.Count;
   var from=supports[origin]-centre;float angle=math.atan2(from.z,from.x),best=7;int next=-1;
   for(int i=0;i<supports.Count;i++)if(i!=origin){var delta=supports[i]-centre;float turn=math.atan2(delta.z,delta.x)-angle;if(turn<=0)turn+=2*math.PI;if(turn<best){best=turn;next=i;}}
   return next;
  }
  internal static int WireNeighbor(IList<float3> supports,int origin,float3 inward)
  {
   int chosen=-1;float score=float.MinValue;if(supports==null||origin<0||origin>=supports.Count)return -1;
   int next=WireRingNeighbor(supports,origin);
   for(int i=0;i<supports.Count;i++)if(i!=origin&&(i==next||WireRingNeighbor(supports,i)==origin)){var delta=supports[i]-supports[origin];delta.y=0;float distance=math.length(delta);if(distance<2||distance>60)continue;float alignment=math.dot(delta/distance,inward);float value=alignment-distance*.0001f;if(alignment>.5f&&value>score){score=value;chosen=i;}}
   return chosen;
  }
  internal static int WireNeighborForLayout(IList<float3> supports,int origin,float3 inward,bool allowAngled)
  {
   if(!allowAngled)return WireNeighbor(supports,origin,inward);
   int chosen=-1,next=WireRingNeighbor(supports,origin);float best=.5f;
   for(int i=0;i<supports.Count;i++)if(i!=origin&&(i==next||WireRingNeighbor(supports,i)==origin))
   {
    var delta=supports[i]-supports[origin];float length=math.length(delta.xz);
    if(length<2||length>60)continue;float alignment=math.dot(math.normalizesafe(delta.xz),inward.xz);
    if(alignment>best){best=alignment;chosen=i;}
   }
   return chosen;
  }
  internal static bool CompleteWireCorners(List<float3> supports)
  {
   if(supports.Count!=3)return false;int elbow=-1;float best=.55f;
   for(int i=0;i<3;i++){var a=math.normalizesafe(supports[(i+1)%3].xz-supports[i].xz);var b=math.normalizesafe(supports[(i+2)%3].xz-supports[i].xz);float dot=math.abs(math.dot(a,b));if(dot<best){best=dot;elbow=i;}}
   if(elbow<0)return false;var missing=supports[(elbow+1)%3]+supports[(elbow+2)%3]-supports[elbow];
   missing.y=(supports[(elbow+1)%3].y+supports[(elbow+2)%3].y)*.5f;
   supports.Add(missing);return true;
  }
  internal static bool TryAlignedWireCorners(IList<float3> supports,IList<FarSideLightPolicy.Approach> roads,out List<float3> fitted)
  {
   fitted=null;if(supports==null||supports.Count!=4||roads==null||roads.Count<3||roads.Count>4)return false;
   var axis=math.normalizesafe(roads[0].Outward);if(math.lengthsq(axis)<.9f)return false;
   var across=new float2(-axis.y,axis.x);
   // A shared rectangular perimeter is valid only for orthogonal road banks.
   // Skew junctions need a separate constrained layout, not forced right angles.
   foreach(var road in roads){var direction=math.normalizesafe(road.Outward);if(math.lengthsq(direction)<.9f||math.max(math.abs(math.dot(direction,axis)),math.abs(math.dot(direction,across)))<.999f)return false;}
   float3 origin=float3.zero;foreach(var point in supports){if(!math.all(math.isfinite(point)))return false;origin+=point;}origin/=4;
   float2 min=new float2(float.MaxValue),max=new float2(float.MinValue);var projected=new List<float2>();
   foreach(var point in supports){var delta=point.xz-origin.xz;var local=new float2(math.dot(delta,axis),math.dot(delta,across));projected.Add(local);min=math.min(min,local);max=math.max(max,local);}
   if(math.cmin(max-min)<2||math.cmax(max-min)>60)return false;
   var centre=(min+max)*.5f;var occupied=new HashSet<int>();var result=new List<float3>();
   for(int i=0;i<4;i++)
   {
    bool highX=projected[i].x>centre.x,highY=projected[i].y>centre.y;int corner=(highX?1:0)|(highY?2:0);if(!occupied.Add(corner))return false;
    var local=new float2(highX?max.x:min.x,highY?max.y:min.y);
    var point=supports[i];point.xz=origin.xz+axis*local.x+across*local.y;
    // Expand to the outermost mount on each bank, rather than pulling a pole roadward.
    if(math.distancesq(point.xz,supports[i].xz)>64)return false;result.Add(point);
   }
   fitted=result;return true;
  }
  internal static bool ActiveWireSpan(IList<float3> supports,int from,int to,IList<FarSideLightPolicy.Approach> approaches)
  {
   float3 centre=float3.zero;foreach(var point in supports)centre+=point;centre/=supports.Count;
   var midpoint=(supports[from]+supports[to])*.5f;var radial=math.normalizesafe(midpoint.xz-centre.xz);var edge=math.normalizesafe(supports[to].xz-supports[from].xz);
   foreach(var road in approaches)if(math.dot(radial,road.Outward)>.5f&&math.abs(math.dot(edge,road.Outward))<.65f)return true;
   return false;
  }
  internal static bool HasRequiredSpan(IList<int2> spans,int from,int to)
  {
   if(spans==null)return false;foreach(var span in spans)if((span.x==from&&span.y==to)||(span.x==to&&span.y==from))return true;return false;
  }
  internal static List<float2> WirePieces(float span)
  {
   var pieces=new List<float2>();if(!math.isfinite(span)||span<2||span>60)return pieces;
   float next=0;
   while(span-next>.0001f)
   {
    int units=math.clamp((int)math.ceil((span-next)*4-.00001f),1,8);float length=units*.25f;
    // Existing quarter-metre modules are shared. Overlap the final piece
    // backwards so its end reaches the actual pole, without stretching a mesh.
    float start=math.min(next,span-length);pieces.Add(new float2(start,length));next=start+length;
   }
   return pieces;
  }
  internal static bool FindCrossing(EntityManager em,Entity node,float3 pole,out float3 facing,out ushort mask,ushort requiredMask=0)
  {
   facing=default;mask=0;if(!em.HasBuffer<SubLane>(node))return false;float nearest=144;
   foreach(var item in em.GetBuffer<SubLane>(node,true))
   {
    var lane=item.m_SubLane;if(!em.Exists(lane)||!em.HasComponent<PedestrianLane>(lane)||!em.HasComponent<LaneSignal>(lane)||!em.HasComponent<Curve>(lane)||(em.GetComponentData<PedestrianLane>(lane).m_Flags&PedestrianLaneFlags.Crosswalk)==0)continue;
    var signal=em.GetComponentData<LaneSignal>(lane);if(!CrossingMatches(signal.m_GroupMask,requiredMask))continue;
    var b=em.GetComponentData<Curve>(lane).m_Bezier;bool first=math.distancesq(pole.xz,b.a.xz)<math.distancesq(pole.xz,b.d.xz);var end=first?b.a:b.d;var other=first?b.d:b.a;float d=math.distancesq(pole.xz,end.xz);
    if(d<nearest){float2 dir=math.normalizesafe(other.xz-end.xz);if(math.lengthsq(dir)<.5f)continue;nearest=d;facing=new float3(dir.x,0,dir.y);mask=signal.m_GroupMask;}
   }
   return mask!=0;
  }
  internal static bool Renderable(TrafficLightType type,int meshCount,float3 size)
   =>FarSideLightPolicy.Vehicle(type)&&meshCount>0&&math.all(math.isfinite(size))&&math.all(size>=0)&&size.y>.1f&&math.cmax(size)>1f;
  internal static bool CrossingRenderable(TrafficLightType type,int count,float3 size)
   =>(type&(TrafficLightType.CrossingLeft|TrafficLightType.CrossingRight))!=0&&count>0&&math.all(math.isfinite(size))&&math.all(size>=0)&&size.y>.1f&&math.cmax(size)>.2f;
  private static bool Initialized(EntityManager em,Entity entity,bool vehicle=true)
  {
   return em.Exists(entity)&&em.HasComponent<TrafficLightData>(entity)&&em.HasBuffer<SubMesh>(entity)&&em.HasComponent<ObjectGeometryData>(entity)&&em.GetComponentData<ObjectGeometryData>(entity).m_Layers!=0&&
    (vehicle?Renderable(em.GetComponentData<TrafficLightData>(entity).m_Type,em.GetBuffer<SubMesh>(entity,true).Length,em.GetComponentData<ObjectGeometryData>(entity).m_Size):CrossingRenderable(em.GetComponentData<TrafficLightData>(entity).m_Type,em.GetBuffer<SubMesh>(entity,true).Length,em.GetComponentData<ObjectGeometryData>(entity).m_Size));
  }
  internal static void RegisterPending(PrefabSystem p)
  {
   if(Mod.Saving||owner!=p||pending.Count==0)return;
   // Bound native geometry initialization work after a large road edit.
   var batch=new List<string>();foreach(var key in pending.Keys){batch.Add(key);if(batch.Count==4)break;}
   foreach(var key in batch)
   {
    var item=new KeyValuePair<string,StaticObjectPrefab>(key,pending[key]);
    if(p.AddPrefab(item.Value)&&p.TryGetEntity(item.Value,out var entity)){variants.Add(item.Key,entity);if(pendingSources.TryGetValue(key,out var source))nativeSources[entity]=NativeSource(source);SignalAssetSaveSystem.Register(entity,NativeSource(source));Mod.Log.Info("Signal composite registered for native initialization: "+item.Value.name+" meshes="+item.Value.m_Meshes.Length);}
    else Mod.Log.Warn("Signal composite registration failed: "+item.Value.name);
    pending.Remove(key);pendingSources.Remove(key);
   }
   foreach(var node in pendingNodes)FarSideLightSystem.Queue(node);if(pending.Count==0)pendingNodes.Clear();
  }
  internal static Entity SelectSuppressed(PrefabSystem p,EntityManager em,Entity node,Entity fallback)
  {
   UseWorld(p);
   fallback=NativeSource(fallback);
   string id="suppressed|"+p.GetPrefab<StaticObjectPrefab>(fallback).GetPrefabID();
   if(variants.TryGetValue(id,out var ready))
   {
    if(!em.Exists(ready)||!em.HasComponent<TrafficLightData>(ready)||!em.HasBuffer<SubMesh>(ready)||!em.HasComponent<ObjectGeometryData>(ready))return fallback;
    var meshes=em.GetBuffer<SubMesh>(ready,true);if(!ValidControllerGeometry(meshes.Length,em.GetComponentData<ObjectGeometryData>(ready).m_Size))return fallback;
    foreach(var mesh in meshes)if((mesh.m_Flags&SubMeshFlags.OutlineOnly)==0)return fallback;
    return ready;
   }
   if(!pending.ContainsKey(id))
   {
    var source=p.GetPrefab<StaticObjectPrefab>(fallback);
    if(source.m_Meshes==null||source.m_Meshes.Length==0){if(TraceEnabled)Trace(node,"suppression skipped: native source has no geometry");return fallback;}
    foreach(var mesh in source.m_Meshes)if(mesh==null||mesh.m_Mesh==null)return fallback;
    var prefab=(StaticObjectPrefab)source.Clone("CSG Controller Only "+(variants.Count+pending.Count));
    // Retain valid native geometry, bounds and submesh slots. Outline-only meshes
    // are excluded from normal rendering by BatchInstanceSystem and remain valid
    // for native selection/deletion previews. Never mutate the shared source entries.
    var outlines=OutlineMeshes(source.m_Meshes);
    prefab.m_Meshes=outlines;prefab.Remove<EffectSource>();prefab.Remove<StandingObject>();prefab.Remove<SpawnableObject>();prefab.Remove<ObsoleteIdentifiers>();prefab.Remove<ObjectSubObjects>();prefab.Remove<SubObjectDefaultProbability>();
    if(prefab.TryGet<UIObject>(out var ui))ui.m_Group=null;pending.Add(id,prefab);pendingSources[id]=fallback;
   }
   pendingNodes.Add(node);return fallback;
  }
  internal static ObjectMeshInfo[] OutlineMeshes(ObjectMeshInfo[] source)
  {
   if(source==null||source.Length==0)throw new ArgumentException("Native signal geometry is required",nameof(source));
   var copies=new ObjectMeshInfo[source.Length];
   for(int i=0;i<source.Length;i++){var mesh=source[i];copies[i]=new ObjectMeshInfo{m_Mesh=mesh.m_Mesh,m_Position=mesh.m_Position,m_Rotation=mesh.m_Rotation,m_RequireState=Game.Objects.ObjectState.Outline};}
   return copies;
  }
  internal static bool ValidControllerGeometry(int count,float3 size)=>count>0&&math.all(math.isfinite(size))&&math.all(size>=0)&&size.y>.1f&&math.cmax(size)>.2f;
  internal static Entity SelectSuppressedLamp(PrefabSystem p,EntityManager em,Entity node,Entity fallback)
  {
   fallback=NativeSource(fallback);string id="dark-lamp|"+p.GetPrefab<StaticObjectPrefab>(fallback).GetPrefabID();
   if(variants.TryGetValue(id,out var ready))
   {
    if(!em.Exists(ready)||!em.HasComponent<StreetLightData>(ready)||!em.HasBuffer<SubMesh>(ready)||!em.HasComponent<ObjectGeometryData>(ready))return fallback;
    var meshes=em.GetBuffer<SubMesh>(ready,true);if(!ValidControllerGeometry(meshes.Length,em.GetComponentData<ObjectGeometryData>(ready).m_Size))return fallback;
    foreach(var mesh in meshes)if((mesh.m_Flags&SubMeshFlags.OutlineOnly)==0)return fallback;
    if(em.HasBuffer<Game.Prefabs.Effect>(ready)&&em.GetBuffer<Game.Prefabs.Effect>(ready,true).Length!=0)return fallback;
    return ready;
   }
   if(!pending.ContainsKey(id))
   {
    var source=p.GetPrefab<StaticObjectPrefab>(fallback);if(source.m_Meshes==null||source.m_Meshes.Length==0)return fallback;
    foreach(var mesh in source.m_Meshes)if(mesh?.m_Mesh==null)return fallback;
    var prefab=(StaticObjectPrefab)source.Clone("CSG Dark Junction Lamp "+(variants.Count+pending.Count));prefab.m_Meshes=OutlineMeshes(source.m_Meshes);
    prefab.Remove<EffectSource>();prefab.Remove<StandingObject>();prefab.Remove<SpawnableObject>();prefab.Remove<ObsoleteIdentifiers>();prefab.Remove<ObjectSubObjects>();prefab.Remove<SubObjectDefaultProbability>();
    if(prefab.TryGet<UIObject>(out var ui))ui.m_Group=null;pending.Add(id,prefab);pendingSources[id]=fallback;
   }
   pendingNodes.Add(node);return fallback;
  }
  // Stable farthest pair avoids adjacent fixtures and is independent of prop order.
  internal static int2 LuminairePair(IList<float3> points)
  {
   if(points==null||points.Count<2)return new int2(-1);int2 pair=new int2(0,1);float best=-1;
   for(int i=0;i<points.Count;i++)for(int j=i+1;j<points.Count;j++)
   {float d=math.distancesq(points[i].xz,points[j].xz);if(d>best){best=d;pair=new int2(i,j);}}
   return pair;
  }
  internal static Entity SelectLuminaire(PrefabSystem p,EntityManager em,Entity node,Entity fallback)
  {
   if(!Ready(p,em))return fallback;

   fallback=NativeSource(fallback);var source=p.GetPrefab<StaticObjectPrefab>(fallback);byte style=em.HasComponent<MGRSignalStyle>(node)?em.GetComponentData<MGRSignalStyle>(node).Value:(byte)0;if(style==4&&!FloridaReady(p,em))return fallback;string id="luminaire|"+style+"|"+source.GetPrefabID();
   if(variants.TryGetValue(id,out var ready))return em.Exists(ready)&&em.HasComponent<StreetLightData>(ready)&&em.HasComponent<ObjectGeometryData>(ready)&&em.GetComponentData<ObjectGeometryData>(ready).m_Layers!=0&&em.HasBuffer<Game.Prefabs.Effect>(ready)&&em.GetBuffer<Game.Prefabs.Effect>(ready,true).Length>0?ready:fallback;
   if(!pending.ContainsKey(id))
   {
    if(!source.TryGet<EffectSource>(out var native)||native.m_Effects==null)return fallback;
    var effects=new List<EffectSource.EffectSettings>();
    foreach(var effect in native.m_Effects)
    {
     if(effect.m_Effect==null||!p.TryGetEntity(effect.m_Effect,out var entity)||!em.HasComponent<LightEffectData>(entity))continue;
     effects.Add(new EffectSource.EffectSettings{m_Effect=effect.m_Effect,m_PositionOffset=new float3(3.18f,IsWireStyle(style)?7.916f:8.45f,0),m_Rotation=effect.m_Rotation,m_Scale=effect.m_Scale,m_Intensity=effect.m_Intensity,m_ParentMesh=-1,m_AnimationIndex=-1});
     Mod.Log.Info("Native fixture effect selected: source="+source.name+" effect="+effect.m_Effect.GetPrefabID()+" rotation="+effect.m_Rotation+" intensity="+effect.m_Intensity);
     break; // One native light effect per fixture; never duplicate a donor's multi-arm effects.
    }
    if(effects.Count==0)return fallback;
    var prefab=(StaticObjectPrefab)source.Clone("CSG Junction Luminaire "+(variants.Count+pending.Count));prefab.m_Meshes=new[]{Mesh(IsMetalWireStyle(style)?"MetalWireLuminaire":IsWireStyle(style)?"WireLuminaire":IsRigidStyle(style)?"RigidLuminaire":"GardenLuminaire",float3.zero,quaternion.identity)};
    prefab.GetComponent<EffectSource>().m_Effects=effects;prefab.Remove<StandingObject>();prefab.Remove<SpawnableObject>();prefab.Remove<ObsoleteIdentifiers>();prefab.Remove<ObjectSubObjects>();prefab.Remove<SubObjectDefaultProbability>();if(prefab.TryGet<UIObject>(out var ui))ui.m_Group=null;
    pending.Add(id,prefab);pendingSources[id]=fallback;Mod.Log.Info("Signal luminaire queued: style="+style+" source="+source.name);
   }
   pendingNodes.Add(node);return fallback;
  }
  internal static float ArmHeadHeight(byte style,float distance,float reach)=>IsRigidStyle(style)?6.58f:6.48f;
  internal static float3 StreetSignPosition(byte style)=>new float3(global::GardenCitySignals.SignalSignPlacement.Along(style==4?(byte)2:style,style==4||global::GardenCitySignals.SignalPackPolicy.UseRigidSignLayout),global::GardenCitySignals.SignalSignPlacement.Height(style==4?(byte)2:style,style==4||global::GardenCitySignals.SignalPackPolicy.UseRigidSignLayout),0);
  internal static quaternion DownwardLampRotation()=>quaternion.LookRotationSafe(new float3(0,-1,0),new float3(0,0,1));
  internal static quaternion InwardLampRotation(float3 support,float3 centre)
  {
   var toward=math.normalizesafe(new float3(centre.x-support.x,0,centre.z-support.z));
   return quaternion.LookRotationSafe(math.cross(toward,new float3(0,1,0)),new float3(0,1,0));
  }
  internal static Entity Select(PrefabSystem p,EntityManager em,Entity node,ObjectTransform original,ref ObjectTransform placed,IList<FarSideLightPolicy.Approach> approaches,Entity fallback,ushort originalMask,out ushort pedestrianMask,IList<float3> supports=null,int virtualSupport=-1,IList<int2> requiredSpans=null,int sourceApproach=-1,bool includePole=true,ushort vehicleMask=0,bool includeAssembly=true,bool sharedPhaseSupport=false)
  {
   if(!TryDescribe(p,em,node,original,ref placed,approaches,fallback,originalMask,out pedestrianMask,supports,virtualSupport,requiredSpans,sourceApproach,includePole,vehicleMask,includeAssembly,sharedPhaseSupport,out var meshes,out var rotation,out var style))return fallback;
   return Resolve(p,em,node,fallback,meshes,rotation,ref placed,true,style);
  }
  internal static bool TryDescribe(PrefabSystem p,EntityManager em,Entity node,ObjectTransform original,ref ObjectTransform placed,IList<FarSideLightPolicy.Approach> approaches,Entity fallback,ushort originalMask,out ushort pedestrianMask,IList<float3> supports,int virtualSupport,IList<int2> requiredSpans,int sourceApproach,bool includePole,ushort vehicleMask,bool includeAssembly,bool sharedPhaseSupport,out List<ObjectMeshInfo> meshes,out quaternion rotation,out byte style)
  {
   meshes=null;rotation=default;style=0;pedestrianMask=originalMask;if(!Ready(p,em)){if(TraceEnabled)Trace(node,"module lookup not ready");return false;}int selected=sourceApproach>=0?sourceApproach:Nearest(original,approaches);if(selected<0||selected>=approaches.Count){if(TraceEnabled)Trace(node,"no source curb within 12m");return false;}var a=PhaseApproach(approaches[selected],vehicleMask);
   byte requestedStyle=em.HasComponent<MGRSignalStyle>(node)?em.GetComponentData<MGRSignalStyle>(node).Value:(byte)0;
   float3 inward;float[] heads;
   bool layout=IsWireStyle(requestedStyle)&&supports is CornerSupportList cornerList&&cornerList.UsesCornerRule
    ?TryWireLayout(a,placed.m_Position,supports,requestedStyle,out inward,out heads,out _)
    :TryLayout(a.Incoming,a.Outward,placed.m_Position,out inward,out heads);
   if(!layout){if(TraceEnabled)Trace(node,"lane fit rejected: incoming="+(a.Incoming?.Count??0)+" source="+original.m_Position+" pole="+placed.m_Position+" outward="+a.Outward);return false;}
   style=em.HasComponent<MGRSignalStyle>(node)?em.GetComponentData<MGRSignalStyle>(node).Value:(byte)0;
   if(style==4&&!FloridaReady(p,em))return false;
   if(!(IsWireStyle(style)&&supports is CornerSupportList verifiedCorners&&verifiedCorners.UsesCornerRule)&&!HeadClearance(approaches[selected],placed.m_Position,inward,style))return false;
   rotation=quaternion.LookRotationSafe(math.cross(inward,new float3(0,1,0)),new float3(0,1,0));var inverse=math.inverse(rotation);
   var face=quaternion.LookRotationSafe(new float3(a.Outward.x,0,a.Outward.y),new float3(0,1,0));var headRotation=math.mul(inverse,face);float3 wireSlope=new float3(1,0,0);float armReach=0,wireSpan=0,wireProjection=1;meshes=new List<ObjectMeshInfo>();
   bool nativeWires=IsWireStyle(style)&&p.World.GetOrCreateSystemManaged<SignalWireSystem>().Ready;
   if(!WireAssemblyReady(style,nativeWires))return false;
   if(IsWireStyle(style))return ComposeWire(placed.m_Position,a.Outward,inward,heads,style,supports,virtualSupport,includeAssembly,nativeWires,
    includeAssembly?CrossingFaces(em,node,placed.m_Position):new List<CrossingFace>(),out meshes,out rotation,out pedestrianMask);

    float reach=heads[heads.Length-1];
    if(sharedPhaseSupport)
    {
     // One arm covers the complete approach; each controller retains only its
     // own lane heads. All phases use the same arm profile and head heights.
     if(!TryLayout(approaches[selected].Incoming,a.Outward,placed.m_Position,out _,out var bankHeads))return false;
     reach=bankHeads[bankHeads.Length-1];
    }
    int index=Size(reach+.35f);if(index<0)return false;armReach=Reaches[index];
    if(includeAssembly&&includePole)meshes.Add(Mesh(style==4?"CSFPole":IsRigidStyle(style)?"ModularRigidPole":"ModularGardenPole",float3.zero,quaternion.identity));
    if(includeAssembly)meshes.Add(Mesh((IsRigidStyle(style)?"ModularRigidArm":"ModularGardenArm")+(index+1).ToString("00"),float3.zero,quaternion.identity));
   // Florida uses the same shared placement eligibility and native lane/phase
   // mapping as the rigid style. Style selects authored fixtures, not road types.
   string head=style==4?"CSFHorizontalHead":IsWireStyle(style)?"HeadWire":IsRigidStyle(style)?"HeadYellow":"HeadBlack";
   foreach(float d in heads)
   {
    var headPosition=IsWireStyle(style)?wireSlope*d+new float3(0,5.94f,0):new float3(d,ArmHeadHeight(style,d,armReach),0)+math.rotate(headRotation,new float3(0,0,.12f));
    if(nativeWires)headPosition.y-=SignalWirePolicy.Drop(wireSpan,d/wireProjection);
    meshes.Add(Mesh(head,headPosition,headRotation));
   }
   if(includeAssembly)
   {
    meshes.Add(Mesh(style==4?"CSFLowerHead":IsWireStyle(style)?"LowerHeadWire":style==2?"LowerHeadYellow":"LowerHeadBlack",math.rotate(headRotation,new float3(0,0,.42f))+new float3(0,3.8f,0),headRotation));
    meshes.Add(Mesh("SignPlate",StreetSignPosition(style),quaternion.identity));
   }
   var crossings=includeAssembly?CrossingFaces(em,node,placed.m_Position):new List<CrossingFace>();
   if(crossings.Count>0)
   {
    var crossing=crossings[0];var pedRotation=math.mul(inverse,quaternion.LookRotationSafe(crossing.Facing,new float3(0,1,0)));var pedPosition=math.mul(inverse,crossing.Facing*PedestrianOffset)+new float3(0,2.55f,0);
    meshes.Add(Mesh(style==4?"CSFPedHead":IsWireStyle(style)?"PedHeadWire":"PedHead",pedPosition,pedRotation));pedestrianMask=crossing.Mask;
   }
   else pedestrianMask=0;
   return true;
  }
  // Shared by rendering and clearance planning: no entity reads or registration.
  internal static bool ComposeWire(float3 position,float2 outward,float3 inward,float[] heads,byte style,IList<float3> supports,int virtualSupport,bool includeAssembly,bool nativeWires,List<CrossingFace> crossings,out List<ObjectMeshInfo> meshes,out quaternion rotation,out ushort pedestrianMask)
  {
   pedestrianMask=0;meshes=new List<ObjectMeshInfo>();
   rotation=quaternion.LookRotationSafe(math.cross(inward,new float3(0,1,0)),new float3(0,1,0));var inverse=math.inverse(rotation);
   var face=quaternion.LookRotationSafe(new float3(outward.x,0,outward.y),new float3(0,1,0));var headRotation=math.mul(inverse,face);
   float3 wireSlope=new float3(1,0,0);float wireSpan=0,wireProjection=1,armReach=0;

    if(supports==null||supports.Count<2)return false;int origin=-1;float closest=float.MaxValue;
    for(int i=0;i<supports.Count;i++){float d=math.distancesq(supports[i],position);if(d<closest){closest=d;origin=i;}}
    int target=WireNeighborForLayout(supports,origin,inward,supports is CornerSupportList corners&&corners.UsesCornerRule);if(target<0)return false;
    var headDelta=math.mul(inverse,supports[target]-position);if(headDelta.x<heads[heads.Length-1]+.3f)return false;wireSlope=headDelta/headDelta.x;
    wireSpan=math.length(headDelta.xz);wireProjection=headDelta.x;
    // Use the same adjacent supports for heads and cables; never a diagonal.
    int cableTarget=WireRingNeighbor(supports,origin);if(cableTarget<0)return false;
    if(includeAssembly){if(IsMetalWireStyle(style))meshes.Add(Mesh("MetalWirePole",float3.zero,quaternion.identity));else{meshes.Add(Mesh("WirePole",float3.zero,quaternion.identity));meshes.Add(Mesh("WireTimber",float3.zero,quaternion.identity));}}

    // Corners without an incoming controller (T/one-way roads) remain physical
    // supports, owned once by the first assembly. No extra controller is created.
    if(includeAssembly&&virtualSupport>=0&&origin==0)
     for(int extra=virtualSupport;extra<supports.Count;extra++)
     {var offset=math.mul(inverse,supports[extra]-position);if(IsMetalWireStyle(style))meshes.Add(Mesh("MetalWirePole",offset,quaternion.identity));else{meshes.Add(Mesh("WirePole",offset,quaternion.identity));meshes.Add(Mesh("WireTimber",offset,quaternion.identity));}}


   // Florida uses the same shared placement eligibility and native lane/phase
   // mapping as the rigid style. Style selects authored fixtures, not road types.
   string head=style==4?"CSFHorizontalHead":IsWireStyle(style)?"HeadWire":IsRigidStyle(style)?"HeadYellow":"HeadBlack";
   foreach(float d in heads)
   {
    var headPosition=IsWireStyle(style)?wireSlope*d+new float3(0,5.94f,0):new float3(d,ArmHeadHeight(style,d,armReach),0)+math.rotate(headRotation,new float3(0,0,.12f));
    if(nativeWires)headPosition.y-=SignalWirePolicy.Drop(wireSpan,d/wireProjection);
    meshes.Add(Mesh(head,headPosition,headRotation));
   }
   if(includeAssembly)
   {
    meshes.Add(Mesh(style==4?"CSFLowerHead":IsWireStyle(style)?"LowerHeadWire":style==2?"LowerHeadYellow":"LowerHeadBlack",math.rotate(headRotation,new float3(0,0,.42f))+new float3(0,3.8f,0),headRotation));
    meshes.Add(Mesh("SignPlate",StreetSignPosition(style),quaternion.identity));
   }
   
   if(crossings.Count>0)
   {
    var crossing=crossings[0];var pedRotation=math.mul(inverse,quaternion.LookRotationSafe(crossing.Facing,new float3(0,1,0)));var pedPosition=math.mul(inverse,crossing.Facing*PedestrianOffset)+new float3(0,2.55f,0);
    meshes.Add(Mesh(style==4?"CSFPedHead":IsWireStyle(style)?"PedHeadWire":"PedHead",pedPosition,pedRotation));pedestrianMask=crossing.Mask;
   }
   else pedestrianMask=0;
   return true;
  }

  internal static Entity SelectPedestrian(PrefabSystem p,EntityManager em,Entity node,Entity fallback,ref ObjectTransform placed,CrossingFace face)
  {
   if(!Ready(p,em))return fallback;
   var rotation=quaternion.LookRotationSafe(face.Facing,new float3(0,1,0));
   byte style=em.HasComponent<MGRSignalStyle>(node)?em.GetComponentData<MGRSignalStyle>(node).Value:(byte)0;
   if(style==4&&!FloridaReady(p,em))return fallback;
   var meshes=new List<ObjectMeshInfo>{Mesh(style==4?"CSFPedHead":IsWireStyle(style)?"PedHeadWire":"PedHead",new float3(0,2.55f,PedestrianOffset),quaternion.identity)};
   return Resolve(p,em,node,fallback,meshes,rotation,ref placed,false,style);
  }
  private static Entity Resolve(PrefabSystem p,EntityManager em,Entity node,Entity fallback,List<ObjectMeshInfo> meshes,quaternion rotation,ref ObjectTransform placed,bool vehicle,byte style)
  {
   fallback=NativeSource(fallback);
   var key=new StringBuilder(p.GetPrefab<StaticObjectPrefab>(fallback).GetPrefabID().ToString()).Append('|').Append(style);
   foreach(var mesh in meshes){key.Append('|').Append(mesh.m_Mesh.name);foreach(float f in new[]{mesh.m_Position.x,mesh.m_Position.y,mesh.m_Position.z,mesh.m_Rotation.value.x,mesh.m_Rotation.value.y,mesh.m_Rotation.value.z,mesh.m_Rotation.value.w})key.Append(':').Append(math.round(f*1000).ToString(CultureInfo.InvariantCulture));}
   string id=key.ToString();if(variants.TryGetValue(id,out var ready)){if(!Initialized(em,ready,vehicle)){FarSideLightSystem.WaitForSignalDefinition(node);if(TraceEnabled)Trace(node,"composite awaiting initialized signal geometry: "+ready);return fallback;}placed.m_Rotation=rotation;return ready;}
   if(pending.ContainsKey(id)){FarSideLightSystem.WaitForSignalDefinition(node);pendingNodes.Add(node);return fallback;}
   if(variants.Count+pending.Count>=2048){if(notices.Add("variant-capacity"))Mod.Log.Warn("Signal composite capacity reached (2048). Retaining native signals for unsupported new configurations; existing prefabs are not freed. Write a snapshot for cache counts.");return fallback;}
   var prefab=(StaticObjectPrefab)p.GetPrefab<StaticObjectPrefab>(fallback).Clone("CSG Applied "+(variants.Count+pending.Count));prefab.m_Meshes=meshes.ToArray();prefab.Remove<EffectSource>();if(vehicle&&IsWireStyle(style))WireStandingGeometry.Configure(prefab,meshes);if(!vehicle){prefab.Remove<StandingObject>();var role=prefab.GetComponent<TrafficLightObject>();role.m_VehicleLeft=false;role.m_VehicleRight=false;role.m_CrossingLeft=false;role.m_CrossingRight=true;}prefab.Remove<SpawnableObject>();prefab.Remove<ObsoleteIdentifiers>();prefab.Remove<ObjectSubObjects>();prefab.Remove<SubObjectDefaultProbability>();if(prefab.TryGet<UIObject>(out var ui))ui.m_Group=null;
   // Register before the native prefab initialization pass, never during modification.
   pending.Add(id,prefab);pendingSources[id]=fallback;FarSideLightSystem.WaitForSignalDefinition(node);pendingNodes.Add(node);if(TraceEnabled)Trace(node,"composite queued: style="+style+" vehicle="+vehicle+" meshes="+meshes.Count);return fallback;
  }
 }
 public partial class SignalPrefabRegistrationSystem:Game.GameSystemBase
 {
  protected override void OnUpdate(){World.GetOrCreateSystemManaged<SignalWireSystem>().RegisterPrefab();GardenCityLightPolicy.RegisterPending(World.GetOrCreateSystemManaged<PrefabSystem>());}
 }
}
