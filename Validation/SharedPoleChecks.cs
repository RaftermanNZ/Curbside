using System;using System.Collections;using System.Collections.Generic;using System.Reflection;using Unity.Mathematics;
class SharedPoleChecks {
 static int checks;static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static readonly Type Policy=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.FarSideLightPolicy");
 static readonly Type Approach=Policy.GetNestedType("Approach",BindingFlags.NonPublic);
 static readonly MethodInfo Shared=Policy.GetMethod("CanSharePole",BindingFlags.Static|BindingFlags.NonPublic),Far=Policy.GetMethod("IsFarSide",BindingFlags.Static|BindingFlags.NonPublic),Reach=Policy.GetMethod("ArmCandidate",BindingFlags.Static|BindingFlags.NonPublic);
 static float angle,mirror;static float3 shift;
 static float3 P(float x,float z){x*=mirror;return shift+new float3(x*(float)Math.Cos(angle)-z*(float)Math.Sin(angle),0,x*(float)Math.Sin(angle)+z*(float)Math.Cos(angle));}
 static float2 D(float x,float z)=>P(x,z).xz-shift.xz;
 static object Road(float x,float z,float dx,float dz,float half,float laneX,float laneZ){var r=Activator.CreateInstance(Approach);var side=new float2(-dz,dx)*half;foreach(var item in new Dictionary<string,object>{{"Left",P(x+side.x,z+side.y)},{"Right",P(x-side.x,z-side.y)},{"Outward",D(dx,dz)},{"Incoming",new List<float3>{P(laneX,laneZ)}},{"JunctionCentre",shift},{"HasJunctionCentre",true}})Approach.GetField(item.Key,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(r,item.Value);return r;}
 static IList Roads(){var r=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Approach));r.Add(Road(0,-12,0,-1,10,2,-12));r.Add(Road(-20,0,-1,0,12,-20,2));r.Add(Road(0,12,0,1,10,-2,12));r.Add(Road(20,0,1,0,12,20,-2));return r;}
 static bool Share(float3 p,IList roads,int a,int b)=>(bool)Shared.Invoke(null,new object[]{p,roads,a,b,0f});
 static bool F(float3 p,IList roads,int a)=>(bool)Far.Invoke(null,new object[]{p,roads,a});
 static void Main(){
 foreach(float reflect in new[]{-1f,1f})for(int rotation=0;rotation<360;rotation+=15)foreach(float height in new[]{0f,35f})foreach(float offset in new[]{0f,10000f}){
  mirror=reflect;angle=rotation*(float)Math.PI/180;shift=new float3(offset,height,-offset*.4f);var roads=Roads();
  var wrong=P(20.6f,-12.6f);Check((bool)Reach.Invoke(null,new object[]{wrong,roads[0],null,0f})&&(bool)Reach.Invoke(null,new object[]{wrong,roads[1],null,0f}),"fixture must reproduce reachable-but-wrong-side bug");
  Check(!Share(wrong,roads,0,1),"near-side pole shared despite reachable heads");
  Check(!Share(wrong,roads,1,0),"sharing order changed rejection");
  Check(Share(P(20.6f,12.6f),roads,0,1),"valid far-side common bank rejected");
  Check(!Share(P(20.6f,12.6f),roads,0,0),"same approach shared with itself");
  Check(!F(P(10.6f,1),roads,0),"pole just past centre but short of far bank accepted");
  Check(!F(P(0,13),roads,0),"median/roadway support accepted");
  roads.Add(Road(15,15,.70710678f,.70710678f,12,15,15));Check(!Share(P(20.6f,12.6f),roads,0,1),"fifth-road footprint ignored");
  roads=Roads();roads.RemoveAt(2);Check(F(P(10.6f,13),roads,0),"T-junction far sidewalk rejected");Check(!F(P(10.6f,-13),roads,0),"T-junction near sidewalk accepted");
  Check(!Share(new float3(float.NaN,0,0),roads,0,1),"nonfinite pole accepted");Check(!Share(P(20,20),roads,-1,1),"invalid approach accepted");
 }
 foreach(float reflect in new[]{-1f,1f})for(int rotation=0;rotation<360;rotation+=30)foreach(float skew in new[]{-30f,-15f,15f,30f}){
  mirror=reflect;angle=rotation*(float)Math.PI/180;shift=new float3(137,17,-94);float s=(float)Math.Sin(skew*Math.PI/180),c=(float)Math.Cos(skew*Math.PI/180);
  var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Approach));
  roads.Add(Road(0,-12,0,-1,10,2,-12));roads.Add(Road(-20*c,-20*s,-c,-s,12,-20*c-2*s,-20*s+2*c));roads.Add(Road(0,12,0,1,10,-2,12));roads.Add(Road(20*c,20*s,c,s,12,20*c+2*s,20*s-2*c));
  float z=s>0?s*20.6f+c*12.6f:12.6f;float x=s>0?c*20.6f-s*12.6f:(20.6f-s*z)/c;Check(Share(P(x,z),roads,0,1),"valid skew curb bank rejected rotation="+rotation+" skew="+skew+" mirror="+reflect);
  x=(20.6f+s*12.6f)/c;Check(!Share(P(x,-12.6f),roads,0,1),"skew near-side common pole accepted");
 }
 Console.WriteLine(checks+" shared-pole invariants passed: reach-only regression, both approaches, rotations, reflected traffic, translations, elevated nodes, T and five-leg roads, near-side/median rejection.");
 }
}