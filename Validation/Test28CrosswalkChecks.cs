using System;using System.Reflection;using Unity.Mathematics;using Colossal.Mathematics;
class Test28CrosswalkChecks
{
 static int count,accepted;static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;}
 static float3 R(float3 p,float t)=>new float3(p.x*math.cos(t)-p.z*math.sin(t),p.y,p.x*math.sin(t)+p.z*math.cos(t))+new float3(700,8,-1200);
 static void Main(){
 var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var method=a.GetType("MedianGrassReplacement.Systems.CrosswalkPlacement").GetMethod("TryNativeFootprint",BindingFlags.Static|BindingFlags.NonPublic);
 foreach(float angle in new[]{0f,.5f,1.6f,3.2f})foreach(float length in new[]{4f,12f,40f})foreach(float bend in new[]{0f,.1f})foreach(float nativeWidth in new[]{1f,2f,4f,8f})foreach(float requested in new[]{2f,3f,12f})foreach(float lineWidth in new[]{.1f,.3f}){
  var curve=new Bezier4x3(R(new float3(-length/2,0,0),angle),R(new float3(-length/6,0,length*bend),angle),R(new float3(length/6,0,length*bend),angle),R(new float3(length/2,0,0),angle));
  object[] args={curve,nativeWidth,requested,lineWidth,default(Bezier4x3),default(Bezier4x3)};
  bool ok=(bool)method.Invoke(null,args);if(bend==0)Check(ok,"straight crossing must convert without a later road edit");
  if(!ok)continue;accepted++;
  foreach(var result in new[]{(Bezier4x3)args[4],(Bezier4x3)args[5]}){float previous=-1;for(int i=0;i<=16;i++){
   var point=MathUtils.Position(result,i/16f);float distance=MathUtils.Distance(curve,point,out float t);
   Check(math.all(math.isfinite(point)),"finite native-footprint result");
   Check(distance+lineWidth*.5f+.1f<=nativeWidth*.5f-.099f,"line and smoothing remain within native paint band");
   Check(t>=previous-.002f,"fallback cannot fold back across the crossing");previous=t;
  }}
 }
 var source=new Bezier4x3(new float3(-5,0,0),new float3(-2,0,0),new float3(2,0,0),new float3(5,0,0));
 foreach(float invalid in new[]{0f,-1f,float.NaN,float.PositiveInfinity,.2f}){object[] args={source,invalid,4f,.3f,default(Bezier4x3),default(Bezier4x3)};Check(!(bool)method.Invoke(null,args),"invalid or too narrow native band rejected");}
 Console.WriteLine(count+" compiled crosswalk footprint checks passed ("+accepted+" supported cases).");
 }
}
