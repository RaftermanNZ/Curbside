using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;using System.Reflection;using System.Web.Script.Serialization;using Unity.Mathematics;using Colossal.Mathematics;using Curbside.CornerStudy;
class ParallelNativeIntegration238
{
 const BindingFlags S=BindingFlags.NonPublic|BindingFlags.Static,F=BindingFlags.NonPublic|BindingFlags.Instance;
 static float3 V(float[] p)=>new float3(p[0],p[1],p[2]);static int checks,layouts;static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static void Main(string[] args){bool baseline=Array.IndexOf(args,"--baseline")>=0;var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var roadType=a.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy").GetNestedType("Approach",BindingFlags.NonPublic);var planner=a.GetType("MedianGrassReplacement.Systems.WireLayoutPlanner").GetMethod("Fit",S);a.GetType("GardenCitySignals.SignalPackPolicy").GetProperty("UseRigidSignLayout",S).GetSetMethod(true).Invoke(null,new object[]{true});var capture=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<Capture234>(File.ReadAllText("CapturedCorners233.json"));
 foreach(var scene in capture.nodes)for(int pass=0;pass<20;pass++){
  float theta=pass*math.PI/10;var shift=pass<10?float3.zero:new float3(6000,0,-7000);float3 P(float3 p)=>shift+new float3(p.x*math.cos(theta)-p.z*math.sin(theta),p.y,p.x*math.sin(theta)+p.z*math.cos(theta));float2 D(float2 p)=>new float2(p.x*math.cos(theta)-p.y*math.sin(theta),p.x*math.sin(theta)+p.y*math.cos(theta));
  var slices=scene.slices.Select(s=>new CurbSlice{Road=s.roadIndex,Side=s.sideIndex,Curb=new Bezier4x3(P(V(s.curb[0])),P(V(s.curb[1])),P(V(s.curb[2])),P(V(s.curb[3]))),Back=new Bezier4x3(P(V(s.back[0])),P(V(s.back[1])),P(V(s.back[2])),P(V(s.back[3]))),HasBack=true,Outside=P(V(s.Outside))-shift}).ToList();
  var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(roadType));foreach(var r in scene.roads){var road=Activator.CreateInstance(roadType);void Set(string name,object value)=>roadType.GetField(name,F).SetValue(road,value);Set("Left",P(V(r.left)));Set("Right",P(V(r.right)));Set("Outward",D(new float2(r.outward[0],r.outward[1])));Set("Incoming",r.lanes.Select(v=>P(V(v))).ToList());roads.Add(road);}
  var preferred=scene.supports.Select(p=>P(V(p.position))).ToList();var sources=scene.sourceIndices.ToList();int first=-1,second=-1;
  for(int i=0;i<slices.Count&&first<0;i++)for(int j=i+1;j<slices.Count;j++)if(slices[i].Road!=slices[j].Road&&math.distance(slices[i].Back.d.xz,slices[j].Back.d.xz)<.05f){first=i;second=j;break;}Check(first>=0,"Native corner pair missing");
  foreach(byte style in new byte[]{1,3}){
   var original=new object[]{slices,.7238954f,roads,sources,preferred,style,null,null,null};Check((bool)planner.Invoke(null,original),"Original captured layout regressed");var points=(float3[])original[6];
   var modified=slices.ToList();var one=modified[first];var two=modified[second];var direction=math.normalizesafe(math.normalizesafe(one.Outside)+math.normalizesafe(two.Outside));one.Outside=direction;two.Outside=direction;modified[first]=one;modified[second]=two;
   var changed=new object[]{modified,.7238954f,roads,sources,preferred,style,null,null,null};bool valid=(bool)planner.Invoke(null,changed);Check(valid!=baseline,"Complete native-geometry plan still depends on nonparallel Outside hints: "+scene.node+" pass="+pass+" reason="+changed[8]);layouts++;
   if(valid){var restored=(float3[])changed[6];Check(restored.Length==points.Length,"Physical support count changed");foreach(var p in restored)Check(points.Any(q=>math.distance(p,q)<.01f),"Restored placement changed the validated 1.5m native corner targets");}
  }
 }
 Console.WriteLine((baseline?"BASELINE REPRODUCED: ":"PASS: ")+checks+" checks; "+layouts+" full wire plans on four captured junctions transformed twenty times, both wire styles. Only the paired Outside hints are made parallel; native curb/back curves, lane samples, footprint and setback remain unchanged. No World/renderer.");}
}
