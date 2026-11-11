using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;using System.Reflection;using System.Web.Script.Serialization;
using Unity.Mathematics;using Curbside.CornerStudy;using Colossal.Mathematics;
class CSlice234 {public int roadIndex,sideIndex;public float[][] curb,back;public float[] Outside;}
class Road234 {public float[] left,right,outward;public float[][] lanes;}
class Support234 {public float[] position;}
class Node234 {public string node;public CSlice234[] slices;public Road234[] roads;public Support234[] supports;public int[] sourceIndices;}
class Capture234 {public Node234[] nodes;}
class CapturedCorners235
{
 const BindingFlags S=BindingFlags.NonPublic|BindingFlags.Static,F=BindingFlags.NonPublic|BindingFlags.Instance;
 static int checks;static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static float3 V(float[] v)=>new float3(v[0],v[1],v[2]);static Bezier4x3 B(float[][] c)=>new Bezier4x3(V(c[0]),V(c[1]),V(c[2]),V(c[3]));
 static void Main(){var json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};var capture=json.Deserialize<Capture234>(File.ReadAllText("CapturedCorners233.json"));var asm=Assembly.LoadFrom("MedianGrassReplacement.dll");var at=asm.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy",true).GetNestedType("Approach",BindingFlags.NonPublic);var planner=asm.GetType("MedianGrassReplacement.Systems.WireLayoutPlanner",true);var pack=asm.GetType("GardenCitySignals.SignalPackPolicy",true);pack.GetProperty("UseRigidSignLayout",S).GetSetMethod(true).Invoke(null,new object[]{true});var output=new List<object>();
 foreach(var scene in capture.nodes)for(int pass=0;pass<20;pass++){
  float theta=pass*math.PI/10;var shift=pass<10?float3.zero:new float3(6000,0,-7000);
  Func<float3,float3> P=p=>shift+new float3(p.x*math.cos(theta)-p.z*math.sin(theta),p.y,p.x*math.sin(theta)+p.z*math.cos(theta));
  Func<float2,float2> D=p=>new float2(p.x*math.cos(theta)-p.y*math.sin(theta),p.x*math.sin(theta)+p.y*math.cos(theta));
  var slices=scene.slices.Select(s=>new CurbSlice{Road=s.roadIndex,Side=s.sideIndex,Curb=new Bezier4x3(P(V(s.curb[0])),P(V(s.curb[1])),P(V(s.curb[2])),P(V(s.curb[3]))),Back=new Bezier4x3(P(V(s.back[0])),P(V(s.back[1])),P(V(s.back[2])),P(V(s.back[3]))),HasBack=true,Outside=P(V(s.Outside))-shift,Kind=BoundaryKind.Curb}).ToList();
  var proposals=CornerRule.Calculate(slices,.7238954f);Check(proposals.Count==4&&proposals.TrueForAll(p=>p.Fits),"Actual native corner strip rejected "+scene.node+" pass="+pass);
  foreach(var p in proposals){Check(p.Clearance>=1.495f&&p.Clearance<1.52f,"Curb setback not 1.5 metres: "+p.Clearance);Check(math.abs(p.Position.y)>100,"Native road elevation lost");}
  var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(at));
  foreach(var r in scene.roads){var road=Activator.CreateInstance(at);at.GetField("Left",F).SetValue(road,P(V(r.left)));at.GetField("Right",F).SetValue(road,P(V(r.right)));at.GetField("Outward",F).SetValue(road,D(new float2(r.outward[0],r.outward[1])));at.GetField("Incoming",F).SetValue(road,r.lanes.Select(v=>P(V(v))).ToList());roads.Add(road);}
  var sources=scene.sourceIndices.ToList();var preferred=scene.supports.Select(p=>P(V(p.position))).ToList();
  foreach(byte style in new byte[]{1,3}){
   var args=new object[]{proposals,roads,sources,preferred,style,null,null,null};bool valid=(bool)planner.GetMethod("Build",S).Invoke(null,args);Check(valid,"Complete captured planner rejected "+scene.node+": "+args[7]);
   var points=(float3[])args[5];var spans=(int2[])args[6];Check(points.Length==4&&spans.Length>=2,"Incomplete physical layout");
   Check(points.Select(p=>p.xz).Distinct().Count()==4,"Shared physical corner assigned twice");
   foreach(var pair in spans){Check(pair.x>=0&&pair.y>=0&&pair.x<4&&pair.y<4&&pair.x!=pair.y,"Invalid cable pair");Check(math.distance(points[pair.x].xz,points[pair.y].xz)>=2,"Degenerate span");}
   if(pass==0)output.Add(new{scene.node,style,sources,supports=points.Select(p=>new[]{p.x,p.y,p.z}).ToArray(),pairs=spans.Select(p=>new[]{p.x,p.y}).ToArray()});
  }
 }
 File.WriteAllText("Captured-Corners235.json",json.Serialize(new{checks,scope="Compiled Test235 corner and complete lane/appearance/span assignment policies on the four actual captured junctions, twenty transforms and both wire styles. Node centre inferred by existing released fallback because exact native Node centre was not logged. No native World or rendering execution.",layouts=output}));Console.WriteLine("PASS: "+checks+" checks; 160 compiled captured-junction/style layout plans, 320 1.5-metre corner targets.");
 }
}
