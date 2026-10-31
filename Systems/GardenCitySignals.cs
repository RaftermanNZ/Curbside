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
  public void Deserialize<T>(T reader)where T:IReader{reader.Read(out Value);if(Value>2)Value=0;}
 }
 internal static class GardenCityLightPolicy
 {
  internal const string AssetName="MGR Garden City Traffic Lights",WireAsset="MGR Span Wire Traffic Lights",MastAsset="MGR Rigid Mast Traffic Lights";
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
  internal static bool IsAsset(string name)=>name==AssetName||name==WireAsset||name==MastAsset;
  internal static string CacheStatus=>"signalVariants="+variants.Count+" pending="+pending.Count+" modules="+modules.Count+" capacity=2048";
  internal static bool PackPresent()
  {
   return AppDomain.CurrentDomain.GetData("Curbside.GardenCitySignals.ActiveModels.v1") is bool present&&present;
  }
  internal static byte Style(string name)=>(byte)(name==WireAsset?1:name==MastAsset?2:0);
  // Match the millimetre tolerance in lane fitting. Rotating a junction must
  // not select a longer mesh (or another bank) from sub-millimetre roundoff.
  internal static int Size(float reach){if(!math.isfinite(reach)||reach<=0)return -1;for(int i=0;i<Reaches.Length;i++)if(Reaches[i]+.001f>=reach)return i;return -1;}
  private static IEnumerable<string> Names(){foreach(string name in global::GardenCitySignals.SignalModuleCatalog.Names)yield return name.Substring(3);}
  internal static bool Ready(PrefabSystem p,EntityManager em)
  {
   if(owner!=p){owner=p;modules.Clear();variants.Clear();pending.Clear();pendingSources.Clear();nativeSources.Clear();pendingNodes.Clear();notices.Clear();}
   if(modules.Count==global::GardenCitySignals.SignalModuleCatalog.Names.Length)return true;
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
  // The revised rear saddle is deeper than the legacy pins. Inspect only the
  // native prefab's published bounds; never load or modify geometry memory.
  internal static float PedestrianMountOffset(float depth)=>depth>.33f?.44f:.28f;
  private static float PedestrianOffset
  {
   get { if(!(modules["PedHead"].m_Meshes[0].m_Mesh is RenderPrefab render))return .28f;var bounds=render.bounds;return PedestrianMountOffset(math.max(math.abs(bounds.min.z),math.abs(bounds.max.z))); }
  }
  private static ObjectMeshInfo Mesh(string name,float3 position,quaternion rotation)=>new ObjectMeshInfo{m_Mesh=modules[name].m_Meshes[0].m_Mesh,m_Position=position,m_Rotation=rotation};
  internal static int Nearest(ObjectTransform source,IList<FarSideLightPolicy.Approach> approaches)
   =>NearestForPhase(source,approaches,0);
  internal static int NearestForPhase(ObjectTransform source,IList<FarSideLightPolicy.Approach> approaches,ushort mask)
  {
   int selected=-1;float best=144;
   for(int i=0;i<approaches.Count;i++){var a=approaches[i];if(mask!=0&&a.VehicleMask!=0&&(mask&a.VehicleMask)==0)continue;float d=math.min(math.distancesq(source.m_Position.xz,a.Left.xz),math.distancesq(source.m_Position.xz,a.Right.xz));if(d<best){best=d;selected=i;}}
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
  internal static FarSideLightPolicy.Approach PhaseApproach(FarSideLightPolicy.Approach source,ushort mask)
  {
   if(mask==0||source.LaneMasks==null||source.LaneMasks.Count!=source.Incoming.Count)return source;
   var points=new List<float3>();var masks=new List<ushort>();
   for(int i=0;i<source.Incoming.Count;i++)if((source.LaneMasks[i]&mask)!=0){points.Add(source.Incoming[i]);masks.Add(source.LaneMasks[i]);}
   // An unmapped phase must retain native rendering rather than borrow another phase.
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
  internal static List<CrossingFace> CrossingFaces(EntityManager em,Entity node,float3 pole)
  {
   var choices=new List<CrossingFace>();var result=new List<CrossingFace>();if(!em.HasBuffer<SubLane>(node))return result;
   foreach(var item in em.GetBuffer<SubLane>(node,true))
   {
    var lane=item.m_SubLane;if(!em.Exists(lane)||!em.HasComponent<PedestrianLane>(lane)||!em.HasComponent<LaneSignal>(lane)||!em.HasComponent<Curve>(lane)||(em.GetComponentData<PedestrianLane>(lane).m_Flags&PedestrianLaneFlags.Crosswalk)==0)continue;
    var mask=em.GetComponentData<LaneSignal>(lane).m_GroupMask;if(mask==0)continue;
    var curve=em.GetComponentData<Curve>(lane).m_Bezier;bool first=math.distancesq(pole.xz,curve.a.xz)<math.distancesq(pole.xz,curve.d.xz);var end=first?curve.a:curve.d;var other=first?curve.d:curve.a;float distance=math.distancesq(pole.xz,end.xz);
    var facing=math.normalizesafe(new float3(other.x-end.x,0,other.z-end.z));if(distance>576||math.lengthsq(facing)<.5f)continue;
    choices.Add(new CrossingFace{Facing=facing,Mask=mask,Distance=distance});
   }
   choices.Sort((a,b)=>a.Distance.CompareTo(b.Distance));
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
  private static void AddWireSpan(List<ObjectMeshInfo> meshes,float3 delta,float3 offset)
  {
   float span=math.length(delta.xz);var pieces=WirePieces(span);if(pieces.Count==0)return;
   var direction=math.normalizesafe(new float3(delta.x,0,delta.z));var rotation=quaternion.LookRotationSafe(math.cross(direction,new float3(0,1,0)),new float3(0,1,0));
   foreach(var piece in pieces){int units=(int)math.round(piece.y*4);var position=offset+direction*piece.x;position.y+=delta.y*piece.x/span;meshes.Add(Mesh("WireSegment"+units.ToString("00"),position,rotation));}
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

   fallback=NativeSource(fallback);var source=p.GetPrefab<StaticObjectPrefab>(fallback);byte style=em.HasComponent<MGRSignalStyle>(node)?em.GetComponentData<MGRSignalStyle>(node).Value:(byte)0;string id="luminaire|"+style+"|"+source.GetPrefabID();
   if(variants.TryGetValue(id,out var ready))return em.Exists(ready)&&em.HasComponent<StreetLightData>(ready)&&em.HasComponent<ObjectGeometryData>(ready)&&em.GetComponentData<ObjectGeometryData>(ready).m_Layers!=0&&em.HasBuffer<Game.Prefabs.Effect>(ready)&&em.GetBuffer<Game.Prefabs.Effect>(ready,true).Length>0?ready:fallback;
   if(!pending.ContainsKey(id))
   {
    if(!source.TryGet<EffectSource>(out var native)||native.m_Effects==null)return fallback;
    var effects=new List<EffectSource.EffectSettings>();
    foreach(var effect in native.m_Effects)
    {
     if(effect.m_Effect==null||!p.TryGetEntity(effect.m_Effect,out var entity)||!em.HasComponent<LightEffectData>(entity))continue;
     effects.Add(new EffectSource.EffectSettings{m_Effect=effect.m_Effect,m_PositionOffset=new float3(3.18f,style==1?7.916f:8.45f,0),m_Rotation=effect.m_Rotation,m_Scale=effect.m_Scale,m_Intensity=effect.m_Intensity,m_ParentMesh=-1,m_AnimationIndex=-1});
     Mod.Log.Info("Native fixture effect selected: source="+source.name+" effect="+effect.m_Effect.GetPrefabID()+" rotation="+effect.m_Rotation+" intensity="+effect.m_Intensity);
     break; // One native light effect per fixture; never duplicate a donor's multi-arm effects.
    }
    if(effects.Count==0)return fallback;
    var prefab=(StaticObjectPrefab)source.Clone("CSG Junction Luminaire "+(variants.Count+pending.Count));prefab.m_Meshes=new[]{Mesh(style==1?"WireLuminaire":style==2?"RigidLuminaire":"GardenLuminaire",float3.zero,quaternion.identity)};
    prefab.GetComponent<EffectSource>().m_Effects=effects;prefab.Remove<StandingObject>();prefab.Remove<SpawnableObject>();prefab.Remove<ObsoleteIdentifiers>();prefab.Remove<ObjectSubObjects>();prefab.Remove<SubObjectDefaultProbability>();if(prefab.TryGet<UIObject>(out var ui))ui.m_Group=null;
    pending.Add(id,prefab);pendingSources[id]=fallback;Mod.Log.Info("Signal luminaire queued: style="+style+" source="+source.name);
   }
   pendingNodes.Add(node);return fallback;
  }
  internal static float ArmHeadHeight(byte style,float distance,float reach)=>style==2?6.58f:6.48f;
  internal static quaternion DownwardLampRotation()=>quaternion.LookRotationSafe(new float3(0,-1,0),new float3(0,0,1));
  internal static quaternion InwardLampRotation(float3 support,float3 centre)
  {
   var toward=math.normalizesafe(new float3(centre.x-support.x,0,centre.z-support.z));
   return quaternion.LookRotationSafe(math.cross(toward,new float3(0,1,0)),new float3(0,1,0));
  }
  internal static Entity Select(PrefabSystem p,EntityManager em,Entity node,ObjectTransform original,ref ObjectTransform placed,IList<FarSideLightPolicy.Approach> approaches,Entity fallback,ushort originalMask,out ushort pedestrianMask,IList<float3> supports=null,int virtualSupport=-1,IList<int2> requiredSpans=null,int sourceApproach=-1,bool includePole=true,ushort vehicleMask=0)
  {
   pedestrianMask=originalMask;if(!Ready(p,em)){if(TraceEnabled)Trace(node,"module lookup not ready");return fallback;}int selected=sourceApproach>=0?sourceApproach:Nearest(original,approaches);if(selected<0||selected>=approaches.Count){if(TraceEnabled)Trace(node,"no source curb within 12m");return fallback;}var a=PhaseApproach(approaches[selected],vehicleMask);
   if(!TryLayout(a.Incoming,a.Outward,placed.m_Position,out var inward,out var heads)){if(TraceEnabled)Trace(node,"lane fit rejected: incoming="+(a.Incoming?.Count??0)+" source="+original.m_Position+" pole="+placed.m_Position+" outward="+a.Outward);return fallback;}
   byte style=em.HasComponent<MGRSignalStyle>(node)?em.GetComponentData<MGRSignalStyle>(node).Value:(byte)0;
   var rotation=quaternion.LookRotationSafe(math.cross(inward,new float3(0,1,0)),new float3(0,1,0));var inverse=math.inverse(rotation);
   var face=quaternion.LookRotationSafe(new float3(a.Outward.x,0,a.Outward.y),new float3(0,1,0));var headRotation=math.mul(inverse,face);float3 wireSlope=new float3(1,0,0);float armReach=0,wireSpan=0,wireProjection=1;var meshes=new List<ObjectMeshInfo>();
   bool nativeWires=style==1&&p.World.GetOrCreateSystemManaged<SignalWireSystem>().Ready;
   if(style==1)
   {
    if(supports==null||supports.Count<2)return fallback;int origin=-1;float closest=float.MaxValue;
    for(int i=0;i<supports.Count;i++){float d=math.distancesq(supports[i],placed.m_Position);if(d<closest){closest=d;origin=i;}}
    int target=WireNeighbor(supports,origin,inward);if(target<0)return fallback;
    var headDelta=math.mul(inverse,supports[target]-placed.m_Position);if(headDelta.x<heads[heads.Length-1]+.3f)return fallback;wireSlope=headDelta/headDelta.x;
    wireSpan=math.length(headDelta.xz);wireProjection=headDelta.x;
    // Use the same adjacent supports for heads and cables; never a diagonal.
    int cableTarget=WireRingNeighbor(supports,origin);if(cableTarget<0)return fallback;
    meshes.Add(Mesh("WirePole",float3.zero,quaternion.identity));meshes.Add(Mesh("WireTimber",float3.zero,quaternion.identity));
    if(!nativeWires&&(approaches.Count==4&&supports.Count==4||HasRequiredSpan(requiredSpans,origin,cableTarget)))AddWireSpan(meshes,math.mul(inverse,supports[cableTarget]-placed.m_Position),float3.zero);
    // A T junction's fourth support is visual geometry, owned by the first controller.
    if(virtualSupport>=0&&origin==0){int extra=virtualSupport;var offset=math.mul(inverse,supports[extra]-placed.m_Position);meshes.Add(Mesh("WirePole",offset,quaternion.identity));meshes.Add(Mesh("WireTimber",offset,quaternion.identity));int next=WireRingNeighbor(supports,extra);if(!nativeWires&&HasRequiredSpan(requiredSpans,extra,next))AddWireSpan(meshes,math.mul(inverse,supports[next]-supports[extra]),offset);}


   }
   else
   {
    int index=Size(heads[heads.Length-1]+.35f);if(index<0)return fallback;armReach=Reaches[index];
    if(includePole)meshes.Add(Mesh(style==2?"ModularRigidPole":"ModularGardenPole",float3.zero,quaternion.identity));
    meshes.Add(Mesh((style==2?"ModularRigidArm":"ModularGardenArm")+(index+1).ToString("00"),float3.zero,quaternion.identity));
   }
   string head=style==1?"HeadWire":style==2?"HeadYellow":"HeadBlack";
   foreach(float d in heads)
   {
    var headPosition=style==1?wireSlope*d+new float3(0,5.94f,0):new float3(d,ArmHeadHeight(style,d,armReach),0)+math.rotate(headRotation,new float3(0,0,.12f));
    if(nativeWires)headPosition.y-=SignalWirePolicy.Drop(wireSpan,d/wireProjection);
    meshes.Add(Mesh(head,headPosition,headRotation));
   }
   meshes.Add(Mesh(style==2?"LowerHeadYellow":"LowerHeadBlack",math.rotate(headRotation,new float3(0,0,.42f))+new float3(0,3.8f,0),headRotation));
   meshes.Add(Mesh("SignPlate",new float3(1.05f,5.95f,0),quaternion.identity));
   var crossings=CrossingFaces(em,node,placed.m_Position);
   if(crossings.Count>0)
   {
    var crossing=crossings[0];var pedRotation=math.mul(inverse,quaternion.LookRotationSafe(crossing.Facing,new float3(0,1,0)));var pedPosition=math.mul(inverse,crossing.Facing*PedestrianOffset)+new float3(0,2.55f,0);
    meshes.Add(Mesh("PedHead",pedPosition,pedRotation));pedestrianMask=crossing.Mask;
   }
   else pedestrianMask=0;
   return Resolve(p,em,node,fallback,meshes,rotation,ref placed,true,style);
  }
  internal static Entity SelectPedestrian(PrefabSystem p,EntityManager em,Entity node,Entity fallback,ref ObjectTransform placed,CrossingFace face)
  {
   if(!Ready(p,em))return fallback;
   var rotation=quaternion.LookRotationSafe(face.Facing,new float3(0,1,0));
   var meshes=new List<ObjectMeshInfo>{Mesh("PedHead",new float3(0,2.55f,PedestrianOffset),quaternion.identity)};
   return Resolve(p,em,node,fallback,meshes,rotation,ref placed,false,3);
  }
  private static Entity Resolve(PrefabSystem p,EntityManager em,Entity node,Entity fallback,List<ObjectMeshInfo> meshes,quaternion rotation,ref ObjectTransform placed,bool vehicle,byte style)
  {
   fallback=NativeSource(fallback);
   var key=new StringBuilder(p.GetPrefab<StaticObjectPrefab>(fallback).GetPrefabID().ToString()).Append('|').Append(style);
   foreach(var mesh in meshes){key.Append('|').Append(mesh.m_Mesh.name);foreach(float f in new[]{mesh.m_Position.x,mesh.m_Position.y,mesh.m_Position.z,mesh.m_Rotation.value.x,mesh.m_Rotation.value.y,mesh.m_Rotation.value.z,mesh.m_Rotation.value.w})key.Append(':').Append(math.round(f*1000).ToString(CultureInfo.InvariantCulture));}
   string id=key.ToString();if(variants.TryGetValue(id,out var ready)){if(!Initialized(em,ready,vehicle)){if(TraceEnabled)Trace(node,"composite awaiting initialized signal geometry: "+ready);return fallback;}placed.m_Rotation=rotation;return ready;}
   if(pending.ContainsKey(id)){pendingNodes.Add(node);return fallback;}
   if(variants.Count+pending.Count>=2048){if(notices.Add("variant-capacity"))Mod.Log.Warn("Signal composite capacity reached (2048). Retaining native signals for unsupported new configurations; existing prefabs are not freed. Write a snapshot for cache counts.");return fallback;}
   var prefab=(StaticObjectPrefab)p.GetPrefab<StaticObjectPrefab>(fallback).Clone("CSG Applied "+(variants.Count+pending.Count));prefab.m_Meshes=meshes.ToArray();prefab.Remove<EffectSource>();if(!vehicle){prefab.Remove<StandingObject>();var role=prefab.GetComponent<TrafficLightObject>();role.m_VehicleLeft=false;role.m_VehicleRight=false;role.m_CrossingLeft=false;role.m_CrossingRight=true;}prefab.Remove<SpawnableObject>();prefab.Remove<ObsoleteIdentifiers>();prefab.Remove<ObjectSubObjects>();prefab.Remove<SubObjectDefaultProbability>();if(prefab.TryGet<UIObject>(out var ui))ui.m_Group=null;
   // Register before the native prefab initialization pass, never during modification.
   pending.Add(id,prefab);pendingSources[id]=fallback;pendingNodes.Add(node);if(TraceEnabled)Trace(node,"composite queued: style="+style+" vehicle="+vehicle+" meshes="+meshes.Count);return fallback;
  }
 }
 public partial class SignalPrefabRegistrationSystem:Game.GameSystemBase
 {
  protected override void OnUpdate(){World.GetOrCreateSystemManaged<SignalWireSystem>().RegisterPrefab();GardenCityLightPolicy.RegisterPending(World.GetOrCreateSystemManaged<PrefabSystem>());}
 }
}
