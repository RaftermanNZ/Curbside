using System;using System.Collections.Generic;using System.Reflection;using Colossal.Mathematics;using Unity.Mathematics;
class StopPriorityChecks
{
 static Bezier4x3 Line(float3 a,float3 b)=>new Bezier4x3(a,math.lerp(a,b,1f/3),math.lerp(a,b,2f/3),b);
 static void Main()
 {
  var type=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.StopPaintPriority");var exclude=type.GetMethod("Exclude",BindingFlags.NonPublic|BindingFlags.Static);int cases=0;
  for(int degrees=0;degrees<360;degrees+=15)foreach(float width in new[]{.15f,.4f,.8f})foreach(float slope in new[]{0f,.2f})
  {
   float angle=degrees*(float)Math.PI/180;var along=new float3((float)Math.Cos(angle),0,(float)Math.Sin(angle));var across=new float3(-along.z,0,along.x);var stop=Line(-along*3,along*3);
   var stroke=Line(-across*5,new float3(across.x*5,slope,across.z*5));var result=(List<Bezier4x3>)exclude.Invoke(null,new object[]{stroke,stop,width,3f});if(result.Count!=2)throw new Exception("Stop strip not cut out of crossing hatch");
   foreach(var piece in result)for(int i=0;i<=100;i++){var p=MathUtils.Position(piece,i/100f);if(Math.Abs(math.dot(p,across))<width-.002f)throw new Exception("Hatch still covers stop bar");}
   var cap=Line(-along*2,along*2);result=(List<Bezier4x3>)exclude.Invoke(null,new object[]{cap,stop,width,3f});if(result.Count!=0)throw new Exception("Parallel hatch cap hides stop bar");
   var opposite=Line(along*6-across*5,along*6+across*5);result=(List<Bezier4x3>)exclude.Invoke(null,new object[]{opposite,stop,width,3f});if(result.Count!=1||Math.Abs(MathUtils.Length(result[0])-10)>0.01)throw new Exception("Opposite-carriageway hatch was cut");
   var elevated=Line(-across*5+new float3(0,4,0),across*5+new float3(0,4,0));result=(List<Bezier4x3>)exclude.Invoke(null,new object[]{elevated,stop,width,3f});if(result.Count!=1)throw new Exception("Another road deck was clipped");cases++;
  }
  Console.WriteLine("PASS: "+cases+" rotated stop-bar/hatch cases; caps, sloping paint and separate decks.");
 }
}
