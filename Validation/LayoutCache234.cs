using System;using System.Collections;using System.Collections.Generic;using Game.Net;using Game.Prefabs;using Unity.Entities;using Unity.Mathematics;
// Controlled storage backend for the actual production cache source. This is
// not a Unity World, job scheduler, serializer or live performance benchmark.
namespace Unity.Entities
{
 public sealed class TestBuffer<T>:IEnumerable<T>{internal T[] Data;public int Length=>Data.Length;public T this[int i]{get=>Data[i];set=>Data[i]=value;}public IEnumerator<T> GetEnumerator()=>((IEnumerable<T>)Data).GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();}
 public sealed class EntityManager
 {
  public uint GlobalSystemVersion;public readonly Dictionary<Entity,uint> RoadVersions=new Dictionary<Entity,uint>();
  public sealed class TestChunk{internal uint Version;public bool DidChange(ref int handle,uint required)=>required==0||unchecked((int)(Version-required))>0;}
  public TestChunk GetChunk(Entity actor)=>new TestChunk{Version=RoadVersions.TryGetValue(actor,out uint version)?version:0};
  public void Kill(Entity actor){alive.Remove(actor);}
  readonly Dictionary<string,object> values=new Dictionary<string,object>();readonly HashSet<Entity> alive=new HashSet<Entity>();
  string Key(Entity e,Type t)=>e.Index+":"+e.Version+"|"+t.FullName;
  public void Put<T>(Entity e,T value){values[Key(e,typeof(T))]=value;alive.Add(e);}
  public bool HasComponent<T>(Entity e)=>values.ContainsKey(Key(e,typeof(T)));
  public T GetComponentData<T>(Entity e)=>(T)values[Key(e,typeof(T))];
  public bool Exists(Entity e)=>alive.Contains(e);
  public IEnumerable<Entity> Known()=>alive;
  public bool HasType(Entity e,Type t)=>values.ContainsKey(Key(e,t));
  public void Drop<T>(Entity e){values.Remove(Key(e,typeof(T)));}
  public void Buffer<T>(Entity e,T[] data){values[Key(e,typeof(TestBuffer<T>))]=new TestBuffer<T>{Data=data};alive.Add(e);}
  public bool HasBuffer<T>(Entity e)=>values.ContainsKey(Key(e,typeof(TestBuffer<T>)));
  public TestBuffer<T> GetBuffer<T>(Entity e,bool readOnly)=>(TestBuffer<T>)values[Key(e,typeof(TestBuffer<T>))];
 }
}
namespace MedianGrassReplacement.Systems
{
 internal static class FarSideLightPolicy
 {
  internal struct Approach{internal Entity Road;internal List<float3> Incoming;internal List<float3> WireBank;internal List<Colossal.Mathematics.Bezier4x3> WireCurbs;internal List<ushort> LaneMasks;internal ushort VehicleMask;internal float3 Left,Right,JunctionCentre;internal float2 Outward;internal bool HasJunctionCentre;}
  internal static bool ArmCandidate(float3 p,Approach road,IList<float3> occupied,out float reach){throw new Exception("Unexpected arm fitting on cached bind");}
  internal static bool DriverRightBank(float3 p,Approach road){throw new Exception("Unexpected bank fitting on cached bind");}
  internal static float ArmBankCost(float reach,bool right){throw new Exception("Unexpected arm cost on cached bind");}
  internal static bool TrySignalMountFrom(Game.Objects.Transform original,IList<Approach> roads,int source,out Game.Objects.Transform placed){throw new Exception("Unexpected released fitting during cached binding");}
 }
 public partial class FarSideLightSystem
 {
  internal EntityManager EntityManager=new EntityManager();private List<FarSideLightPolicy.Approach> _approaches=new List<FarSideLightPolicy.Approach>();
  internal List<FarSideLightPolicy.Approach> FixtureRoads=new List<FarSideLightPolicy.Approach>();internal List<float3> FixtureSurface=new List<float3>{new float3(1,123,2)};
  internal int actualReads,surfaceReads,maskBindings;
  private bool Live(Entity e)=>EntityManager.Exists(e)&&!EntityManager.HasComponent<Game.Common.Deleted>(e)&&!EntityManager.HasComponent<Game.Tools.Temp>(e);
  #if !NATIVE_SIGNAL_PIPELINE
  private void TrackLayoutNode(Entity node){}
  #endif
  private List<FarSideLightPolicy.Approach> ReadApproachesUncached(Entity node){actualReads++;_approaches.Clear();_approaches.AddRange(FixtureRoads);return _approaches;}
  private void BindApproachMasks(Entity node){maskBindings++;}
  private List<float3> SurfacePoints(Entity node){surfaceReads++;return FixtureSurface;}
  internal void Read(Entity node){ReadApproaches(node);ReadLayoutSurface(node);}
  internal float CachedHeight(Entity node)=>signalLayouts[node].Surface[0].y;
  internal void Loaded(){ClearSignalLayouts();}
 }
}
class LayoutCache234
{
 static int checks;static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static Entity E(int i)=>new Entity{Index=i,Version=1};
 static void Main()
 {
  var system=new MedianGrassReplacement.Systems.FarSideLightSystem();var m=system.EntityManager;var node=E(1);m.Put(node,new Node{m_Position=new float3(10,123,20)});
  var edges=new ConnectedEdge[4];
  for(int i=0;i<4;i++){
   var road=E(10+i);var edge=E(20+i);var comp=E(30+i);edges[i]=new ConnectedEdge{m_Edge=road};
   m.Put(road,new Edge{m_Start=node,m_End=E(50+i)});m.Put(road,new EdgeGeometry());m.Put(road,new StartNodeGeometry());
   m.Put(road,new Composition{m_Edge=edge,m_StartNode=comp,m_EndNode=E(60+i)});
   m.Put(edge,new NetCompositionData{m_Width=24});m.Put(comp,new NetCompositionData{m_Width=24});
   m.Buffer(edge,new[]{new NetCompositionLane{m_Position=new float3(3,0,0)}});m.Buffer(comp,new NetCompositionPiece[0]);
   system.FixtureRoads.Add(new MedianGrassReplacement.Systems.FarSideLightPolicy.Approach{Road=road,Incoming=new List<float3>{new float3(0,123,0)},LaneMasks=new List<ushort>{1},VehicleMask=1});
  }
  m.Buffer(node,edges);system.Read(node);Check(system.actualReads==1&&system.surfaceReads==1,"Initial network capture");
  for(int i=0;i<1000;i++){
   // Newly generated native light/cable identities, masks and node children
   // must not become inputs to a stable road geometry cache.
   var actor=E(1000+i);m.Put(actor,new Game.Objects.TrafficLight{m_GroupMask0=(ushort)(i%4+1)});m.Buffer(node,new[]{new Game.Objects.SubObject{m_SubObject=actor}});
   system.Read(node);
  }
  Check(system.actualReads==1&&system.surfaceReads==1,"Actor/mask churn refitted geometry");Check(system.maskBindings==1000,"Controller rebinding was skipped");
  Array.Reverse(edges);m.Buffer(node,edges);system.Read(node);Check(system.actualReads==1,"Native edge order invalidated layout");
  system.FixtureSurface.Clear();system.FixtureSurface.Add(new float3(5,900,6));system.Read(node);Check(system.CachedHeight(node)==123,"Retained a mutable SurfacePoints list");
  var firstComp=E(20);var data=m.GetComponentData<NetCompositionData>(firstComp);m.Put(firstComp,data);system.Read(node);Check(system.actualReads==1,"Equal native write invalidated geometry");
  data.m_Width=25;m.Put(firstComp,data);system.Read(node);Check(system.actualReads==2&&system.surfaceReads==2,"Road width change missed");Check(system.CachedHeight(node)==900,"New network retained stale ground samples");
  var geometry=m.GetComponentData<EdgeGeometry>(E(10));geometry.m_Start.m_Left.a.x=1;m.Put(E(10),geometry);system.Read(node);Check(system.actualReads==3,"Actual curve edit missed");
  var lanes=m.GetBuffer<NetCompositionLane>(firstComp,true);var lane=lanes[0];lane.m_Position.x+=.25f;lanes[0]=lane;system.Read(node);Check(system.actualReads==4,"Mutable native lane-buffer edit missed");
  m.Buffer(E(30),new[]{new NetCompositionPiece{m_Offset=new float3(1,0,0)}});system.Read(node);Check(system.actualReads==5,"Curb/median piece change missed");
  m.Put(node,new Node{m_Position=new float3(10,124,20)});system.Read(node);Check(system.actualReads==6,"Elevation edit missed");
  var ped=E(9000);m.Put(ped,new Game.Net.PedestrianLane{m_Flags=PedestrianLaneFlags.Crosswalk});m.Buffer(node,new[]{new Game.Net.SubLane{m_SubLane=ped}});system.Read(node);Check(system.actualReads==7,"New crossing footprint missed");
  m.Buffer(node,new Game.Net.SubLane[0]);system.Read(node);Check(system.actualReads==7,"Transient missing lanes invalidated stable geometry");
  system.Loaded();system.Read(node);Check(system.actualReads==8,"Load kept old entity-bound cache");
  Console.WriteLine("PASS: "+checks+" production-cache assertions; 1000 actor/mask changes reused geometry; width, curve, lane, curb piece, elevation and crossing edits invalidated it. Controlled storage, not native ECS execution.");
 }
}
