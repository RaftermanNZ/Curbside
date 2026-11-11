using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;using System.Reflection;using System.Web.Script.Serialization;
using Unity.Mathematics;using Curbside.CornerStudy;using Colossal.Mathematics;
class LiveFit235
{
 const BindingFlags S=BindingFlags.NonPublic|BindingFlags.Static,F=BindingFlags.NonPublic|BindingFlags.Instance;
 static int checks,plans,targets;static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static float3 V(float[] v)=>new float3(v[0],v[1],v[2]);
 static void Main(string[] args){var json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};var asm=Assembly.LoadFrom("MedianGrassReplacement.dll");var at=asm.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy",true).GetNestedType("Approach",BindingFlags.NonPublic);var planner=asm.GetType("MedianGrassReplacement.Systems.WireLayoutPlanner",true);asm.GetType("GardenCitySignals.SignalPackPolicy",true).GetProperty("UseRigidSignLayout",S).GetSetMethod(true).Invoke(null,new object[]{true});var output=new List<object>();
 foreach(var path in Directory.GetFiles(args[0],"Snapshot-*.json")){
 var capture=json.Deserialize<LiveCapture235>(File.ReadAllText(path));foreach(var scene in capture.nodes.Where(n=>n.slices.Length==n.roads.Length*2&&n.slices.Length>0))for(int pass=0;pass<20;pass++){
 float theta=pass*math.PI/10;var shift=pass<10?float3.zero:new float3(6000,0,-7000);Func<float3,float3> P=p=>shift+new float3(p.x*math.cos(theta)-p.z*math.sin(theta),p.y,p.x*math.sin(theta)+p.z*math.cos(theta));Func<float2,float2>D=p=>new float2(p.x*math.cos(theta)-p.y*math.sin(theta),p.x*math.sin(theta)+p.y*math.cos(theta));
 var slices=scene.slices.Select(s=>new CurbSlice{Road=s.roadIndex,Side=s.sideIndex,Curb=new Bezier4x3(P(V(s.curb[0])),P(V(s.curb[1])),P(V(s.curb[2])),P(V(s.curb[3]))),Back=new Bezier4x3(P(V(s.back[0])),P(V(s.back[1])),P(V(s.back[2])),P(V(s.back[3]))),HasBack=true,Outside=P(V(s.Outside))-shift,Kind=BoundaryKind.Curb}).ToList();
 var alternatives=new List<CornerProposal>();foreach(float fraction in new[]{.5f,.45f,.55f})alternatives.AddRange(CornerRule.Calculate(slices,.7238954f,null,CornerRule.Setback,fraction));
 var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(at));foreach(var r in scene.roads){var road=Activator.CreateInstance(at);at.GetField("Left",F).SetValue(road,P(V(r.left)));at.GetField("Right",F).SetValue(road,P(V(r.right)));at.GetField("Outward",F).SetValue(road,D(new float2(r.outward[0],r.outward[1])));at.GetField("Incoming",F).SetValue(road,r.lanes.Select(p=>P(V(p))).ToList());if(scene.centre!=null){at.GetField("HasJunctionCentre",F).SetValue(road,true);at.GetField("JunctionCentre",F).SetValue(road,P(V(scene.centre)));}roads.Add(road);}
 var sources=scene.sourceIndices.ToList();var preferred=scene.roads.Select(r=>P(V(r.left))).ToList();
 foreach(byte style in new byte[]{1,3}){
 var call=new object[]{slices,.7238954f,roads,sources,preferred,style,null,null,null};bool valid=(bool)planner.GetMethod("Fit",S).Invoke(null,call);Check(valid,"Captured fit failed "+path+" "+scene.node+" pass="+pass+": "+call[8]);plans++;
 var points=(float3[])call[6];var pairs=(int2[])call[7];Check(points.Length==(scene.roads.Length==3?4:scene.roads.Length),"Missing physical corner: "+path+" "+scene.node+" roads="+scene.roads.Length+" supports="+points.Length);Check(points.Select(p=>p.xz).Distinct().Count()==points.Length,"Corner assigned twice");
 foreach(var point in points){var matching=alternatives.Where(p=>p.Fits&&math.distance(p.Position,point)<.002f).ToArray();Check(matching.Length>0,"Pole escaped a validated corner strip");Check(matching.Any(p=>p.Clearance>1.49f&&p.Clearance<1.52f),"Setback was not 1.5 metres");targets++;}
 if(pass==0)output.Add(new{snapshot=Path.GetFileName(path),scene.node,style,sources,supports=points.Select(p=>new[]{p.x,p.y,p.z}).ToArray(),pairs=pairs.Select(p=>new[]{p.x,p.y}).ToArray()});
 }
 }}
 File.WriteAllText("Live-Fit235.json",json.Serialize(new{checks,plans,targets,scope="Compiled placement on complete corner captures from eight Test234 snapshots, transformed twenty times and both wire styles. These repeat captured junction states, not distinct cities. Native centres used where logged; no World or renderer.",layouts=output}));Console.WriteLine("PASS: "+checks+" checks, "+plans+" compiled snapshot layout plans, "+targets+" 1.5m contained targets including the rejected skew junction.");}
}
