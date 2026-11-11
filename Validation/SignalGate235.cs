using System;using System.Collections.Generic;using Game.Net;using Game.Prefabs;using Unity.Entities;using Unity.Mathematics;using Game.Common;
namespace MedianGrassReplacement.Systems
{
 public struct MGRGardenCitySignals:IComponentData{}
 public struct MGRSignalStyle:IComponentData{public byte Value;}
 public partial class FarSideLightSystem
 {
  private readonly HashSet<Entity> forcedSignalWork=new HashSet<Entity>();private static readonly HashSet<Entity> waitingSignalDefinitions=new HashSet<Entity>();private long unchangedSignalNotifications;
  private bool EnabledNode(Entity node)=>EntityManager.Exists(node);
  internal void Remember(Entity node)=>RememberSignalNode(node);
  internal bool Unchanged(Entity node)=>UnchangedSignalNode(node);
  internal void Force(Entity node,bool value){if(value)forcedSignalWork.Add(node);else forcedSignalWork.Remove(node);}
 }
}
class SignalGate235
{
 static int checks;static Entity E(int i)=>new Entity{Index=i,Version=1};static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static void Main(){var s=new MedianGrassReplacement.Systems.FarSideLightSystem();var m=s.EntityManager;var node=E(1);m.Put(node,new Node{m_Position=new float3(0,123,0)});m.Put(node,new MedianGrassReplacement.Systems.MGRGardenCitySignals());m.Put(node,new MedianGrassReplacement.Systems.MGRSignalStyle{Value=1});
 var edges=new ConnectedEdge[4];for(int i=0;i<4;i++){var road=E(10+i);var edge=E(20+i);var comp=E(30+i);edges[i]=new ConnectedEdge{m_Edge=road};m.Put(road,new Edge{m_Start=node,m_End=E(50+i)});m.Put(road,new EdgeGeometry());m.Put(road,new StartNodeGeometry());m.Put(road,new Composition{m_Edge=edge,m_StartNode=comp,m_EndNode=E(60+i)});m.Put(edge,new NetCompositionData{m_Width=24});m.Put(comp,new NetCompositionData{m_Width=24});m.Buffer(edge,new[]{new NetCompositionLane{m_Position=new float3(3,0,0)}});m.Buffer(comp,new NetCompositionPiece[0]);s.FixtureRoads.Add(new MedianGrassReplacement.Systems.FarSideLightPolicy.Approach{Road=road,Incoming=new List<float3>{new float3(0,123,0)},LaneMasks=new List<ushort>{1},VehicleMask=1});}
 m.Buffer(node,edges);var actor=E(100);var prefab=E(101);m.Put(prefab,new TrafficLightData());m.Put(actor,new PrefabRef{m_Prefab=prefab});m.Put(actor,new Game.Objects.Transform(new float3(1,123,2),quaternion.identity));m.Put(actor,new Owner{m_Owner=node});m.Put(actor,new Game.Objects.TrafficLight{m_GroupMask0=1,m_GroupMask1=2});var original=new Game.Objects.SubObject{m_SubObject=actor};m.Buffer(node,new[]{original});s.Read(node);s.Remember(node);
 for(int i=0;i<1000;i++){var unrelated=E(1000+i);m.Put(unrelated,new PrefabRef{m_Prefab=E(5000+i)});m.Put(unrelated,new Game.Objects.Transform(new float3(i,123,3),quaternion.identity));m.Put(unrelated,new Owner{m_Owner=node});m.Buffer(node,new[]{new Game.Objects.SubObject{m_SubObject=unrelated},original});Check(s.Unchanged(node),"Nearby unrelated prop forced a light refresh");}
 Check(s.actualReads==1&&s.surfaceReads==1,"Unchanged notification performed geometry or surface fitting");
 var b=m.GetComponentData<Game.Objects.TrafficLight>(actor);b.m_GroupMask0=4;m.Put(actor,b);Check(!s.Unchanged(node),"Native phase change skipped");b.m_GroupMask0=1;m.Put(actor,b);Check(s.Unchanged(node),"Equal phase write refreshed");
 var native=E(102);m.Put(native,new TrafficLightData());m.Put(actor,new PrefabRef{m_Prefab=native});Check(!s.Unchanged(node),"Native definition reset skipped repair");m.Put(actor,new PrefabRef{m_Prefab=prefab});
 m.Put(actor,new Owner{m_Owner=E(999)});Check(!s.Unchanged(node),"Foreign owner change skipped");m.Put(actor,new Owner{m_Owner=node});
 m.Put(node,new MedianGrassReplacement.Systems.MGRSignalStyle{Value=3});Check(!s.Unchanged(node),"Style change skipped");m.Put(node,new MedianGrassReplacement.Systems.MGRSignalStyle{Value=1});
 var geo=m.GetComponentData<EdgeGeometry>(E(10));geo.m_Start.m_Left.a.x=1;m.Put(E(10),geo);Check(!s.Unchanged(node),"Network geometry change skipped");geo.m_Start.m_Left.a.x=0;m.Put(E(10),geo);
 s.Force(node,true);Check(!s.Unchanged(node),"Explicit prefab-readiness callback skipped");s.Force(node,false);Check(s.Unchanged(node),"Stable recovery notification refreshed");
 Console.WriteLine("PASS: "+checks+" production gate assertions: 1,000 unrelated building/prop notifications skip geometry, fitting and application; native reset, phase, style, owner, network and readiness edits remain actionable. Controlled storage, not native building jobs.");}
}
