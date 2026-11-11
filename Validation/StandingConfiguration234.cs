using System;using System.Collections.Generic;using System.Runtime.Serialization;using Colossal.Mathematics;using Game.Prefabs;using Unity.Mathematics;
// Plain model holders exercise the production configuration source without
// creating Unity objects. The real game API is checked by the candidate build.
namespace Game.Prefabs
{
 public sealed class RenderPrefab:RenderPrefabBase{public new string name;public Bounds3 bounds;}
 public sealed class StandingObject{public float3 m_LegSize=new float3(.2f,1.7f,.2f);public float2 m_LegGap;public bool m_CircularLeg;}
 public sealed class StaticObjectPrefab
 {
  public StandingObject Standing=new StandingObject();
  public T AddOrGetComponent<T>()where T:class{return Standing as T;}
  public void Remove<T>(){if(typeof(T)==typeof(StandingObject))Standing=null;}
 }
}
namespace MedianGrassReplacement.Systems
{
 internal static class SignalMastSnapshotPolicy{internal static bool Pole(string n)=>n.Contains("WirePole")||n.Contains("WireTimber");}
}
class StandingConfiguration234
{
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
 static ObjectMeshInfo Part(string name,float3 position)
 {
  var render=(RenderPrefab)FormatterServices.GetUninitializedObject(typeof(RenderPrefab));render.name=name;render.bounds=new Bounds3(new float3(-.28f,0,-.28f),new float3(.28f,7.8f,.28f));return new ObjectMeshInfo{m_Mesh=render,m_Position=position,m_Rotation=quaternion.identity};
 }
 static void Main()
 {
  var local=Part("CSGMetalWirePole Mesh",float3.zero);var remote=Part("CSGMetalWirePole Mesh",new float3(20,0,20));var head=Part("CSGHeadWire Mesh",new float3(5,6,0));
  var model=new StaticObjectPrefab();MedianGrassReplacement.Systems.WireStandingGeometry.Configure(model,new[]{local,head});Check(model.Standing!=null&&model.Standing.m_LegSize.x>.79f&&model.Standing.m_LegSize.x<.8f,"Physical base footprint not corrected");
  var t=new StaticObjectPrefab();MedianGrassReplacement.Systems.WireStandingGeometry.Configure(t,new[]{local,remote,head});Check(t.Standing!=null&&t.Standing.m_LegSize.Equals(model.Standing.m_LegSize),"T support lost standing semantics or expanded the local leg over the road");
  var follower=new StaticObjectPrefab();MedianGrassReplacement.Systems.WireStandingGeometry.Configure(follower,new[]{head});Check(follower.Standing==null,"Head-only phase kept a phantom ground leg");
  var remoteOnly=new StaticObjectPrefab();MedianGrassReplacement.Systems.WireStandingGeometry.Configure(remoteOnly,new[]{remote});Check(remoteOnly.Standing!=null&&remoteOnly.Standing.m_LegSize.x==.2f,"Remote-only unsupported shape changed native standing definition");
  Console.WriteLine("PASS: production standing configuration corrects local pole footprint, retains T/remote support standing semantics and removes only head-only phantom legs. Plain model holders; native initialization not run.");
 }
}
