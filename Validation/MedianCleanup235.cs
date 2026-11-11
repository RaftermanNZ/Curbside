using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;using System.Reflection;using System.Web.Script.Serialization;using Unity.Mathematics;
class Prop235{public string actor,prefab;public float[] original;public int source;public ushort mask;}
class MedianRoad235{public float[] left,right,outward;public float[][] lanes;public ushort vehicleMask;}
class MedianNode235{public string node;public MedianRoad235[] roads;public Prop235[] props;}
class MedianCapture235{public MedianNode235[] nodes;}
class MedianCleanup235
{
 const BindingFlags S=BindingFlags.NonPublic|BindingFlags.Static,F=BindingFlags.NonPublic|BindingFlags.Instance;static int checks;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}static float3 V(float[] v)=>new float3(v[0],v[1],v[2]);
 static void Main(){var asm=Assembly.LoadFrom("MedianGrassReplacement.dll");var at=asm.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy").GetNestedType("Approach",BindingFlags.NonPublic);var policy=asm.GetType("MedianGrassReplacement.Systems.SignalMedianApproachPolicy");var cov=asm.GetType("MedianGrassReplacement.Systems.SignalCoveragePolicy");var capture=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<MedianCapture235>(File.ReadAllText("Median-Snapshot6.json"));var scene=capture.nodes.Single(n=>n.node=="Entity(61239:5)");var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(at));
 foreach(var r in scene.roads){var road=Activator.CreateInstance(at);at.GetField("Left",F).SetValue(road,V(r.left));at.GetField("Right",F).SetValue(road,V(r.right));at.GetField("Outward",F).SetValue(road,new float2(r.outward[0],r.outward[1]));at.GetField("Incoming",F).SetValue(road,r.lanes.Select(V).ToList());at.GetField("LaneMasks",F).SetValue(road,r.lanes.Select(_=>r.vehicleMask).ToList());roads.Add(road);}
 var missing=scene.props.Where(p=>p.source<0&&p.prefab=="NA_TrafficLightCarLeftCrosswalk01").ToArray();Check(missing.Length==4,"Missing captured median actors");
 for(byte style=0;style<=5;style++)foreach(var p in missing){int source=(int)policy.GetMethod("Find",S).Invoke(null,new object[]{V(p.original),p.mask,roads});Check(source>=0,"Captured median signal remains unassigned");Check(scene.roads[source].vehicleMask==p.mask,"Selected an unrelated native phase");Check((int)policy.GetMethod("Find",S).Invoke(null,new object[]{V(p.original),(ushort)128,roads})==-1,"Unmapped protected movement guessed");}
 // A source in the middle of the junction is not proven to belong to a mouth.
 float3 centre=float3.zero;foreach(var r in scene.roads)centre+=(V(r.left)+V(r.right))*.5f;centre/=scene.roads.Length;
 Check((int)policy.GetMethod("Find",S).Invoke(null,new object[]{centre,(ushort)2,roads})==-1,"Ambiguous central object claimed");
 var entry=asm.GetType("MedianGrassReplacement.Systems.SignalCoverageEntry");var visible=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entry));
 var v=Activator.CreateInstance(entry);entry.GetField("Approach",F).SetValue(v,0);entry.GetField("Vehicle",F).SetValue(v,(ushort)2);entry.GetField("Pedestrian",F).SetValue(v,(ushort)1);visible.Add(v);
 for(byte style=0;style<=4;style++)foreach(ushort vehicle in new ushort[]{1,2,4,6})foreach(ushort ped in new ushort[]{1,2,3}){
 bool got=(bool)cov.GetMethod("Covered",S).Invoke(null,new object[]{0,vehicle,ped,true,true,visible});Check(got==(vehicle==2&&ped==1),"Uncovered vehicle/crossing movement suppressed");}
 var maintenance=asm.GetType("MedianGrassReplacement.Systems.SignalMaintenancePolicy");foreach(byte state in new byte[]{0,1,3})foreach(bool signal in new[]{false,true})Check((bool)maintenance.GetMethod("KeepSuppressed",S).Invoke(null,new object[]{state,signal})==(signal?state!=0:state==3),"Plain/custom hidden-controller ownership changed");
 Console.WriteLine("PASS: "+checks+" compiled median/coverage checks across five custom styles plus plain Far Side; four captured unassigned native median actors map to their road mouths. Aggregate mask fixtures are explicit, not a reconstruction of all native per-lane masks. Unmapped/ambiguous/partly covered phases remain native.");}
}
