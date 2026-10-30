using System.Collections.Generic;using Colossal.Mathematics;using Unity.Mathematics;
namespace MedianGrassReplacement.Systems
{
 internal static class StopPaintPriority
 {
  // Subtract only hatch paint inside the physical stop-bar strip. Bays and stop bars stay unchanged.
  internal static List<Bezier4x3> Exclude(Bezier4x3 stroke,Bezier4x3 stop,float halfWidth,float reach)
  {
   var result=new List<Bezier4x3>();var axis=math.normalizesafe(stop.d.xz-stop.a.xz);if(math.lengthsq(axis)<.5f||halfWidth<=0){result.Add(stroke);return result;}
   var centre=(stop.a+stop.d)*.5f;
   float min=float.MaxValue,max=float.MinValue,minAlong=float.MaxValue,maxAlong=float.MinValue;foreach(var point in new[]{stroke.a,stroke.b,stroke.c,stroke.d}){var d=point.xz-centre.xz;float side=d.x*axis.y-d.y*axis.x,along=math.dot(d,axis);min=math.min(min,side);max=math.max(max,side);minAlong=math.min(minAlong,along);maxAlong=math.max(maxAlong,along);}
   if(min>halfWidth||max< -halfWidth||minAlong>reach||maxAlong< -reach){result.Add(stroke);return result;}
   System.Func<float,bool> covered=t=>
   {
    var p=MathUtils.Position(stroke,t);var d=p.xz-centre.xz;
    return math.abs(d.x*axis.y-d.y*axis.x)<=halfWidth&&math.abs(math.dot(d,axis))<=reach&&math.abs(p.y-centre.y)<1;
   };
   float start=0;bool previous=covered(0);int segments=math.clamp((int)math.ceil(MathUtils.Length(stroke)/.05f),16,2048);
   for(int i=1;i<=segments;i++)
   {
    float t=i/(float)segments;bool next=covered(t);
    if(next!=previous)
    {
     float a=(i-1f)/segments,b=t;for(int step=0;step<14;step++){float mid=(a+b)*.5f;if(covered(mid)==previous)a=mid;else b=mid;}
     float cut=(a+b)*.5f;if(!previous&&cut-start>.0001f)result.Add(MathUtils.Cut(stroke,new float2(start,cut)));start=cut;previous=next;
    }
   }
   if(!previous&&1-start>.0001f)result.Add(MathUtils.Cut(stroke,new float2(start,1)));
   return result;
  }
 }
}
