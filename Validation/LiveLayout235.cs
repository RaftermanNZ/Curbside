using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;using System.Reflection;using System.Web.Script.Serialization;
using Unity.Mathematics;using Curbside.CornerStudy;using Colossal.Mathematics;
class LiveNode235 {public string node;public float[] centre;public CSlice234[] slices;public Road234[] roads;public Support234[] supports;public int[] sourceIndices;}
class LiveCapture235 {public LiveNode235[] nodes;}
class LiveLayout235
{
 const BindingFlags S=BindingFlags.NonPublic|BindingFlags.Static,F=BindingFlags.NonPublic|BindingFlags.Instance;
 static float3 V(float[] v)=>new float3(v[0],v[1],v[2]);
 static void Main(string[] args){var json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};var capture=json.Deserialize<LiveCapture235>(File.ReadAllText(args[0]));var asm=Assembly.LoadFrom("MedianGrassReplacement.dll");var at=asm.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy",true).GetNestedType("Approach",BindingFlags.NonPublic);var planner=asm.GetType("MedianGrassReplacement.Systems.WireLayoutPlanner",true);var garden=asm.GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy",true);asm.GetType("GardenCitySignals.SignalPackPolicy",true).GetProperty("UseRigidSignLayout",S).GetSetMethod(true).Invoke(null,new object[]{true});var output=new List<object>();
 foreach(var scene in capture.nodes.Where(n=>n.slices.Length==n.roads.Length*2&&n.slices.Length>0))foreach(float setback in new[]{1f,1.5f}){
 var slices=scene.slices.Select(s=>new CurbSlice{Road=s.roadIndex,Side=s.sideIndex,Curb=new Bezier4x3(V(s.curb[0]),V(s.curb[1]),V(s.curb[2]),V(s.curb[3])),Back=new Bezier4x3(V(s.back[0]),V(s.back[1]),V(s.back[2]),V(s.back[3])),HasBack=true,Outside=V(s.Outside),Kind=BoundaryKind.Curb}).ToList();var proposals=CornerRule.Calculate(slices,.7238954f,null,setback);
 if(args.Length>2){float fraction=float.Parse(args[2],System.Globalization.CultureInfo.InvariantCulture);proposals=proposals.Select(p=>(CornerProposal)typeof(CornerRule).GetMethod("Propose",S).Invoke(null,new object[]{slices.Single(s=>s.Road==p.RoadA&&s.Side==p.SideA),slices.Single(s=>s.Road==p.RoadB&&s.Side==p.SideB),slices,setback,.7238954f,null,fraction})).ToList();}
 var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(at));foreach(var r in scene.roads){var road=Activator.CreateInstance(at);at.GetField("Left",F).SetValue(road,V(r.left));at.GetField("Right",F).SetValue(road,V(r.right));at.GetField("Outward",F).SetValue(road,new float2(r.outward[0],r.outward[1]));at.GetField("Incoming",F).SetValue(road,r.lanes.Select(V).ToList());if(scene.centre!=null){at.GetField("HasJunctionCentre",F).SetValue(road,true);at.GetField("JunctionCentre",F).SetValue(road,V(scene.centre));}roads.Add(road);}
 var sources=scene.sourceIndices.ToList();var preferred=scene.roads.Select(r=>V(r.left)).ToList();
 foreach(byte style in new byte[]{1,3}){
 var call=new object[]{proposals,roads,sources,preferred,style,null,null,null};bool valid=(bool)planner.GetMethod("Build",S).Invoke(null,call);var matrix=new List<object>();
 for(int i=0;i<roads.Count;i++)for(int c=0;c<proposals.Count;c++){
 var p=proposals[c];var road=roads[i];bool across=(bool)planner.GetMethod("Across",S).Invoke(null,new object[]{p.Position,roads,i});bool appearance=(bool)garden.GetMethod("AppearanceCandidate",S).Invoke(null,new object[]{road,p.Position,style});
 var lr=new object[]{scene.roads[i].lanes.Select(V).ToList(),new float2(scene.roads[i].outward[0],scene.roads[i].outward[1]),p.Position,null,null};bool fit=(bool)garden.GetMethod("TryLayout",S).Invoke(null,lr);int other=-1;float projection=0,required=0;
 if(fit){var inward=(float3)lr[3];var heads=(float[])lr[4];other=(int)garden.GetMethod("WireNeighborForLayout",S).Invoke(null,new object[]{proposals.Select(q=>q.Position).ToList(),c,inward,true});if(other>=0)projection=math.dot(proposals[other].Position-p.Position,inward);required=heads.Last()+.3f;}
 matrix.Add(new{source=i,corner=c,p.Fits,p.Reason,across,appearance,fit,other,projection,required});}
 output.Add(new{scene.node,setback,style,valid,reason=call[7],proposals=proposals.Select(p=>new{p.Fits,p.Reason,p.Clearance,position=new[]{p.Position.x,p.Position.y,p.Position.z}}),matrix});
 Console.WriteLine(scene.node+" setback="+setback+" style="+style+" valid="+valid+" reason="+call[7]);
 }
 }
 File.WriteAllText(args[1],json.Serialize(output));}
}
