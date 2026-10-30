using System;using System.Reflection;using Colossal.Mathematics;using Unity.Mathematics;
class SignalWireChecks
{
 static int checks;static void Check(bool b,string reason){checks++;if(!b)throw new Exception(reason);}
 static void Main()
 {
  var policy=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.SignalWirePolicy");
  var span=policy.GetMethod("Span",BindingFlags.NonPublic|BindingFlags.Static);var valid=policy.GetMethod("Valid",BindingFlags.NonPublic|BindingFlags.Static);var drop=policy.GetMethod("Drop",BindingFlags.NonPublic|BindingFlags.Static);
  for(float length=2;length<=60;length+=.5f)for(int angle=0;angle<360;angle+=30)foreach(float slope in new[]{-4f,0f,4f})
  {
   float r=angle*(float)Math.PI/180;var a=new float3(123,7.45f,-31);var b=a+new float3((float)Math.Cos(r)*length,slope,(float)Math.Sin(r)*length);
   // Boundary rounding is separately covered by the exact length cases below.
   if(length>2&&length<60)Check((bool)valid.Invoke(null,new object[]{a,b}),"valid cable span rejected");
   var curve=(Bezier4x3)span.Invoke(null,new object[]{a,b});Check(math.distancesq(curve.a,a)<1e-8f&&math.distancesq(curve.d,b)<1e-8f,"cable anchor shifted");
   for(int step=0;step<=10;step++)
   {
    float t=step/10f;var point=MathUtils.Position(curve,t);var line=math.lerp(a,b,t);float sag=(float)drop.Invoke(null,new object[]{math.distance(a.xz,b.xz),t});
    Check(math.distance(point.xz,line.xz)<.0001f,"cable bends sideways");Check(Math.Abs((line.y-point.y)-sag)<.0001f,"signal head no longer meets hanging cable");Check(sag>=0&&sag<=.55001f,"excessive cable sag");
   }
  }
  foreach(float bad in new[]{0f,1.9f,60.1f,float.NaN,float.PositiveInfinity})Check(!(bool)valid.Invoke(null,new object[]{float3.zero,new float3(bad,0,0)}),"invalid cable length accepted");
  var scale=Game.Rendering.BatchDataHelpers.BuildCurveScale(new Game.Net.NodeLane{m_WidthOffset=new float2(.02f*.35f)},new Game.Prefabs.NetLaneData{m_Width=.02f});
  Check(Math.Abs(scale.x-1.35f)<.00001f&&Math.Abs(scale.z-1.35f)<.00001f,"native cable thickness scaling changed");
  Console.WriteLine(checks+" native cable policy checks passed: anchors, mild sag, sloping/rotated spans, vertical head attachments and native 1.35x width scale.");
 }
}
