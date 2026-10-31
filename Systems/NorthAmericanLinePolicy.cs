using System;

using System.Collections.Generic;

using Unity.Mathematics;

namespace MedianGrassReplacement.Systems

{

 internal static class NorthAmericanLinePolicy

 {

  internal struct Traffic {internal float X,Width,Y;internal bool Reverse;internal Traffic(float x,float width,float y,bool reverse){X=x;Width=width;Y=y;Reverse=reverse;}}

  // The two nearest physical traffic lanes bound a gap even on asymmetric roads.

  internal static bool OpposingBoundary(float x,IList<Traffic> lanes)

  {

   if(!math.isfinite(x))return false;

   int left=-1,right=-1;float l=float.MinValue,r=float.MaxValue;

   for(int i=0;i<lanes.Count;i++){var lane=lanes[i];if(lane.X<x&&lane.X>l){left=i;l=lane.X;}if(lane.X>x&&lane.X<r){right=i;r=lane.X;}}

   return left>=0&&right>=0&&lanes[left].Reverse!=lanes[right].Reverse;

  }

  internal static bool YellowBoundary(float x,IList<Traffic> lanes)

  {

   if(!math.isfinite(x)||lanes.Count==0)return false;

   if(OpposingBoundary(x,lanes))return true;

   bool reverse=lanes[0].Reverse;float edge=reverse?float.MinValue:float.MaxValue;

   foreach(var lane in lanes){if(lane.Reverse!=reverse)return false;edge=reverse?math.max(edge,lane.X+lane.Width*.5f):math.min(edge,lane.X-lane.Width*.5f);}

   return reverse?x>=edge-.6f:x<=edge+.6f;

  }

  internal static List<float2> Merge(IList<float2> input)

  {

   var sorted=new List<float2>();foreach(var span in input)if(math.all(math.isfinite(span))&&span.y>span.x)sorted.Add(span);

   sorted.Sort((a,b)=>a.x.CompareTo(b.x));var result=new List<float2>();

   foreach(var span in sorted){int last=result.Count-1;if(last>=0&&span.x<=result[last].y+.05f)result[last]=new float2(result[last].x,math.max(result[last].y,span.y));else result.Add(span);}return result;

  }

  internal static string YellowMeshKey(string name){return name.Replace("White","Yellow");}

 }

}
