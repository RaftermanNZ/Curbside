using System;using System.Collections;using System.Collections.Generic;using Game;using Game.Common;using Game.Net;using Game.Objects;using Game.Prefabs;using Unity.Entities;using Unity.Mathematics;
// Controlled ECS storage, never a native World. The production event adapter,
// child merge, maintenance, gate and presentation-only path are compiled below.
namespace Game{public class UpdateSystem{public SystemUpdatePhase currentPhase;}}
namespace Unity.Entities
{
 public struct ComponentType{public Type Type;public static ComponentType ReadOnly<T>()=>new ComponentType{Type=typeof(T)};}
 public class EntityQueryDesc{public ComponentType[] All,Any,None;}
 public sealed class TestEntityArray:IEnumerable<Entity>,IDisposable{internal List<Entity> Items;public IEnumerator<Entity> GetEnumerator()=>Items.GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();public void Dispose(){}}
 public sealed class EntityQuery
 {
  internal Func<TestEntityArray> Read;public TestEntityArray ToEntityArray(Unity.Collections.Allocator allocator)=>Read();
 }
}
namespace MedianGrassReplacement.Systems
{
 public struct MGRFarSideLights:IComponentData{}
 public struct MGRGardenCitySignals:IComponentData{}
 public struct MGRSignalStyle:IComponentData{public byte Value;}
 public partial class FarSideLightSystem
 {
  internal sealed class TestWorld{internal Game.UpdateSystem Update=new Game.UpdateSystem();internal T GetOrCreateSystemManaged<T>()=> (T)(object)Update;}
  internal TestWorld World=new TestWorld();internal int QueryReads,PresentationWrites,FullApplications;
  private readonly Dictionary<Entity,byte> _maintenanceNodes=new Dictionary<Entity,byte>();
  private struct Moved{internal Entity Node,Owner,Prefab,AppliedPrefab;internal Game.Objects.Transform Applied;internal ushort AppliedVehicleMask,AppliedPedMask;}
  private struct Suppressed{internal Entity Original,Applied;internal ushort VehicleMask,PedestrianMask;}
  private readonly Dictionary<Entity,Moved> _moved=new Dictionary<Entity,Moved>(),_lamps=new Dictionary<Entity,Moved>();
  private readonly Dictionary<Entity,Entity> _hiddenSignals=new Dictionary<Entity,Entity>();
  private readonly Dictionary<Entity,Suppressed> _suppressedPrefabs=new Dictionary<Entity,Suppressed>();
  private bool EnabledNode(Entity e)=>Live(e)&&EntityManager.HasComponent<Node>(e)&&EntityManager.HasComponent<MGRFarSideLights>(e)&&EntityManager.HasComponent<Game.Net.TrafficLights>(e);
  private byte MaintenanceNode(Entity e)=>SignalMaintenancePolicy.State(EnabledNode(e),EntityManager.HasComponent<MGRGardenCitySignals>(e));
  private void Undo(Entity e,Moved moved){}
  private void ReleaseHiddenSignal(Entity e){_hiddenSignals.Remove(e);_suppressedPrefabs.Remove(e);maintenance.Forget(e,SignalMaintenanceIndex<Entity>.Hidden);}
  private void RetireGeneratedLamps(Entity e){}
  private void UnwatchLampRoads(Entity e){}
  private void NodeDuplicateSignals(Entity e,IList<FarSideLightPolicy.Approach> roads){PresentationWrites++;}
  private bool EdgeMedianSignalsForApproaches(Entity e,bool apply,IList<FarSideLightPolicy.Approach> roads)=>false;
  private EntityQuery GetEntityQuery(params EntityQueryDesc[] descriptions)
  {
   return new EntityQuery{Read=()=>{QueryReads++;var actors=new List<Entity>();foreach(var actor in EntityManager.Known())foreach(var desc in descriptions){bool all=true;foreach(var t in desc.All)all&=EntityManager.HasType(actor,t.Type);if(!all)continue;bool any=false;foreach(var t in desc.Any)any|=EntityManager.HasType(actor,t.Type);if(!any)continue;bool none=true;foreach(var t in desc.None)none&=!EntityManager.HasType(actor,t.Type);if(none){actors.Add(actor);break;}}return new TestEntityArray{Items=actors};}};
  }
  internal void InitEvents()=>InitializeNativeSignalEvents();
  internal HashSet<Entity> EventPass(SystemUpdatePhase phase,bool saving=false)
  {
   World.Update.currentPhase=phase;var nodes=new HashSet<Entity>();
   if(SignalNativePhasePolicy.CanApply(phase,saving,true))GatherNativeSignalEvents(nodes);
   return nodes;
  }
  internal Entity[] Children(Entity node){var result=new List<Entity>();CopySignalChildren(node,result);return result.ToArray();}
  internal void Own(Entity actor,Entity node){var b=EntityManager.GetComponentData<Game.Objects.TrafficLight>(actor);_moved[actor]=new Moved{Node=node,Owner=node,Prefab=EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab,AppliedPrefab=EntityManager.GetComponentData<PrefabRef>(actor).m_Prefab,Applied=EntityManager.GetComponentData<Game.Objects.Transform>(actor),AppliedVehicleMask=b.m_GroupMask0,AppliedPedMask=b.m_GroupMask1};maintenance.Track(actor,node,SignalMaintenanceIndex<Entity>.Moved);}
  internal void Maintain(HashSet<Entity> nodes)=>MaintainChangedSignals(nodes,false);
  internal bool Owns(Entity actor)=>_moved.ContainsKey(actor);
  internal void Remember(Entity node)=>RememberSignalNode(node);
  internal bool Same(Entity node)=>UnchangedSignalNode(node);
  internal void Wait(Entity node)=>WaitForSignalDefinition(node);
  internal void Ready(Entity node)=>waitingSignalDefinitions.Remove(node);
  internal void Review(Entity node)=>ReviewSignalPresentation(node);
 }
}
class NativeSignalPipeline236
{
 static int checks;static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}static Entity E(int id,int version=1)=>new Entity{Index=id,Version=version};
 static void Actor(Unity.Entities.EntityManager m,Entity actor,Entity owner,Entity prefab){m.Put(actor,new Owner{m_Owner=owner});m.Put(actor,new PrefabRef{m_Prefab=prefab});m.Put(actor,new Game.Objects.Transform(new float3(2,123,3),quaternion.identity));m.Put(actor,new Game.Objects.TrafficLight{m_GroupMask0=1,m_GroupMask1=2});}
 static void Main(){var s=new MedianGrassReplacement.Systems.FarSideLightSystem();var m=s.EntityManager;var node=E(1);var prefab=E(2);m.Put(prefab,new TrafficLightData());m.Put(node,new Node{m_Position=new float3(0,123,0)});m.Put(node,new Game.Net.TrafficLights());m.Put(node,new MedianGrassReplacement.Systems.MGRFarSideLights());m.Put(node,new MedianGrassReplacement.Systems.MGRGardenCitySignals());m.Put(node,new MedianGrassReplacement.Systems.MGRSignalStyle{Value=1});
 var roads=new ConnectedEdge[4];for(int i=0;i<4;i++){var road=E(10+i);var edge=E(20+i);var comp=E(30+i);roads[i]=new ConnectedEdge{m_Edge=road};m.Put(road,new Edge{m_Start=node,m_End=E(50+i)});m.Put(road,new Road());m.Put(road,new EdgeGeometry());m.Put(road,new StartNodeGeometry());m.Put(road,new Composition{m_Edge=edge,m_StartNode=comp,m_EndNode=E(60+i)});m.Put(edge,new NetCompositionData{m_Width=24});m.Put(comp,new NetCompositionData{m_Width=24});m.Buffer(edge,new[]{new NetCompositionLane{m_Position=new float3(3,0,0)}});m.Buffer(comp,new NetCompositionPiece[0]);s.FixtureRoads.Add(new MedianGrassReplacement.Systems.FarSideLightPolicy.Approach{Road=road,Incoming=new List<float3>{new float3(0,123,0)},LaneMasks=new List<ushort>{1},VehicleMask=1});}m.Buffer(node,roads);m.Buffer(node,new Game.Objects.SubObject[0]);s.Read(node);s.InitEvents();
 Check(s.EventPass(SystemUpdatePhase.Modification4B).Count==0&&s.QueryReads==0,"Applied before native barrier");Check(s.EventPass(SystemUpdatePhase.Serialize).Count==0,"Applied in serializer");Check(s.EventPass(SystemUpdatePhase.Modification5,true).Count==0,"Applied during save hold");
 // Model the 4B playback: actors and group bindings now exist, references don't.
 var old=E(100);Actor(m,old,node,prefab);m.Put(old,new Created());m.Put(node,new Updated());m.Put(E(10),new Updated());
 var dirty=s.EventPass(SystemUpdatePhase.Modification5);Check(dirty.Count==1&&dirty.Contains(node),"Multiple native notifications did not coalesce");Check(s.Children(node).Length==1&&s.Children(node)[0]==old,"Missed native actor before secondary references");
 m.Buffer(node,new[]{new Game.Objects.SubObject{m_SubObject=old}});Check(s.Children(node).Length==1,"Reference completion duplicated actor");s.Own(old,node);s.Remember(node);
 // Clear tags as native cleanup would. Recoloring alone isn't a layout event.
 m.Drop<Created>(old);m.Drop<Updated>(node);m.Drop<Updated>(E(10));
 for(int i=0;i<1000;i++)Check(s.EventPass(SystemUpdatePhase.Modification5).Count==0,"Idle/color-only pass requested appearance work");
 m.Put(old,new Updated());Check(s.EventPass(SystemUpdatePhase.Modification5).Count==0,"Our own applied write fed back into a rebuild");m.Drop<Updated>(old);
 // Save restore / native replacement: same index, new version, old buffer stale.
 for(int pass=0;pass<100;pass++){
  var next=E(100,pass+2);m.Put(old,new Deleted());Actor(m,next,node,prefab);m.Put(next,new Created());
  dirty=s.EventPass(SystemUpdatePhase.Modification5);Check(dirty.Contains(node),"Replacement notification lost");var visible=s.Children(node);Check(visible.Length==1&&visible[0]==next,"Stale owner reference hid or duplicated replacement");s.Maintain(dirty);Check(!s.Owns(old),"Deleted predecessor retained ownership");s.Read(node);Check(s.actualReads==1,"Controller replacement refitted road geometry");Check(!s.Same(node),"Replacement identity accepted as unchanged");s.Own(next,node);m.Buffer(node,new[]{new Game.Objects.SubObject{m_SubObject=next}});s.Remember(node);m.Drop<Created>(next);m.Kill(old);old=next;
 }
 // A first definition callback can precede native geometry readiness.
 s.Wait(node);Check(!s.Same(node),"Incomplete presentation was memoized healthy");s.Ready(node);Check(s.Same(node),"Definition readiness discarded stable placement");
 s.Review(node);Check(s.PresentationWrites==1,"Unchanged-node duplicate review was skipped");m.Put(old,new Game.Tools.Hidden());s.Review(node);Check(s.PresentationWrites==1,"Presentation repair touched hidden preview originals");m.Drop<Game.Tools.Hidden>(old);m.Put(node,new Game.Tools.Hidden());s.Review(node);Check(s.PresentationWrites==1,"Presentation repair touched preview-hidden junction");
 Console.WriteLine("PASS: "+checks+" production pipeline assertions: 4B/5 ordering guard, pre-reference controller snapshot, 100 versioned replacements, geometry reuse, 1,000 idle passes, self-write filtering, definition waits and presentation-only preview guard. Controlled storage/stages; native scheduler and rendering not executed.");}
}
