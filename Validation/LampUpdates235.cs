using System;using System.Collections.Generic;using Game.Net;using Unity.Entities;
namespace UnityEngine{public static class Time{public static float realtimeSinceStartup;}}
namespace MedianGrassReplacement.Systems
{
 public partial class FarSideLightSystem
 {
  private readonly SignalMaintenanceIndex<Entity> maintenance=new SignalMaintenanceIndex<Entity>();
  private readonly List<SignalMaintenanceIndex<Entity>.Entry> maintenanceWork=new List<SignalMaintenanceIndex<Entity>.Entry>();
  private float _nextLampState;internal int lampWrites;
  private bool EnabledNode(Entity node)=>EntityManager.Exists(node);
  private int GetComponentTypeHandle<T>(bool readOnly)=>0;
  private void SynchronizeLampState(Entity actor,Entity node,bool preserve){lampWrites++;}
  internal void FixtureWatch(Entity node,Entity road){EntityManager.Put(node,new Node());signalLayouts[node]=new SignalLayoutEntry{Inputs=new[]{new LayoutRoadInput{Road=road}}};WatchLampRoads(node);}
  internal void FixtureLamp(Entity actor,Entity node){maintenance.Track(actor,node,SignalMaintenanceIndex<Entity>.Lamp);}
  internal void FixtureUnwatch(Entity node)=>UnwatchLampRoads(node);
  internal void Poll(float time){UnityEngine.Time.realtimeSinceStartup=time;SynchronizeChangedLampStates();}
  internal int Watched=>lampRoadOwners.Count;
  internal long Checks=>lampRoadChecks;
 }
}
class LampUpdates235
{
 static int checks;static Entity E(int i)=>new Entity{Index=i,Version=1};static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static void Main(){var s=new MedianGrassReplacement.Systems.FarSideLightSystem();var m=s.EntityManager;var road=E(1);var a=E(2);var b=E(3);m.GlobalSystemVersion=10;m.Put(road,new Road());m.RoadVersions[road]=10;s.FixtureWatch(a,road);s.FixtureWatch(b,road);s.FixtureLamp(E(10),a);s.FixtureLamp(E(11),b);s.Poll(0);Check(s.lampWrites==2&&s.Watched==1,"Shared native road duplicated or initial lamps missed");
 for(int i=1;i<=1000;i++){m.GlobalSystemVersion++;s.Poll(i);Check(s.lampWrites==2,"Unchanged road caused a city lamp walk");}Check(s.Checks==1001,"Shared road checked twice per tick");
 m.GlobalSystemVersion++;m.RoadVersions[road]=m.GlobalSystemVersion;s.Poll(1001);Check(s.lampWrites==2,"Coarse native write with equal flags refreshed lamps");
 m.GlobalSystemVersion++;m.Put(road,new Road{m_Flags=RoadFlags.LightsOff});m.RoadVersions[road]=m.GlobalSystemVersion;s.Poll(1001.2f);Check(s.lampWrites==2,"Poll throttle lost budget");m.GlobalSystemVersion+=5;s.Poll(1002);Check(s.lampWrites==4,"Power/night edit between polls was lost");
 s.FixtureUnwatch(a);m.GlobalSystemVersion++;m.Put(road,new Road());m.RoadVersions[road]=m.GlobalSystemVersion;s.Poll(1003);Check(s.lampWrites==5,"Unsubscribed node or shared owner handling failed");
 m.Kill(road);s.Poll(1004);Check(s.Watched==0,"Deleted native road retained watches");s.FixtureUnwatch(b);Check(s.Watched==0,"Deleted road cleanup invalidated owner index");
 Console.WriteLine("PASS: "+checks+" production lamp-update assertions: 1,000 idle polls perform no lamp walks, shared roads checked once, equal native writes ignored, actual LightsOff changes between polls retained, owners/deletion cleaned. Controlled versions/storage, not native scheduling or timings.");}
}
