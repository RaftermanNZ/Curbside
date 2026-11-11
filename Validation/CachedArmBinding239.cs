using System;using System.Collections.Generic;using Unity.Entities;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems {
 public partial class FarSideLightSystem {
  internal void FixtureArmPlan(Entity node){FixturePlan(node);EntityManager.Put(node,new MGRSignalStyle{Value=0});var entry=signalLayouts[node];entry.Arms=entry.Wire;entry.Arms.Style=0;entry.ArmAttemptStyle=0;}
  internal bool FixtureArmBind(Entity node){_plannedSignals.Clear();_supportPositions.Clear();return TryPrepareArmCornerLayout(node,FixtureRoads);}
 }
}
class CachedArmBinding239 {
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static void Main(){
  var system=new MedianGrassReplacement.Systems.FarSideLightSystem();var node=new Entity{Index=1,Version=1};
  for(int i=0;i<4;i++)system.FixtureRoads.Add(new MedianGrassReplacement.Systems.FarSideLightPolicy.Approach{Road=new Entity{Index=10+i,Version=1},Incoming=new List<float3>{new float3(i,123,0)}});
  system.FixtureArmPlan(node);
  for(int i=0;i<1000;i++){system.FixtureActors(100+i*10,4);Check(system.FixtureArmBind(node)&&system.Bound==4&&system.Supports==4,"Replacement arm actors lost cached corner plan");}
  Check(system.CornerReads==0&&MedianGrassReplacement.Systems.GardenCityLightPolicy.DiscoveryCalls==0,"Actor churn repeated corner/module fitting");
  foreach(int count in new[]{0,1,2,3}){system.FixtureActors(20000,count);Check(!system.FixtureArmBind(node)&&system.Bound==0&&system.Supports==0,"Partial native controller set published a partial arm layout");}
  system.FixtureActors(30000,4);system.EntityManager.Put(node,new MedianGrassReplacement.Systems.MGRSignalStyle{Value=2});system.World.AllowLookup=true;
  MedianGrassReplacement.Systems.GardenCityLightPolicy.DiscoveryAllowed=true;
  Check(!system.FixtureArmBind(node)&&system.Bound==0&&system.CornerReads==0,"Changed style reused a different style plan before module readiness");
  Check(MedianGrassReplacement.Systems.GardenCityLightPolicy.DiscoveryCalls==1,"Explicit style change failed to request its profile");
  Console.WriteLine("PASS: actual arm-cache binding source reused one layout for 1000 native actor generations, refused incomplete controller sets atomically, and invalidated on an explicit style change; no corner refits on cache hits. Controlled storage, not native ECS.");
 }
}
