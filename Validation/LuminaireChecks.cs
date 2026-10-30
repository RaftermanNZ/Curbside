using System;using System.Collections.Generic;using System.Reflection;using Unity.Mathematics;
class LuminaireChecks
{
 static void Main()
 {
  var assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");var type=assembly.GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy");
  var pair=type.GetMethod("LuminairePair",BindingFlags.NonPublic|BindingFlags.Static);var rotation=type.GetMethod("InwardLampRotation",BindingFlags.NonPublic|BindingFlags.Static);
  int count=0;
  foreach(int n in new[]{2,3,4,5,6})foreach(float w in new[]{8f,16f,30f})for(int angle=0;angle<360;angle+=15)
  {
   var points=new List<float3>();for(int i=0;i<n;i++){float a=(angle+i*360f/n)*(float)Math.PI/180;points.Add(new float3((float)Math.Cos(a)*w,3,(float)Math.Sin(a)*11));}
   var selected=(int2)pair.Invoke(null,new object[]{points});if(selected.x==selected.y||selected.x<0||selected.y>=n)throw new Exception("Not two distinct supports");
   float d=math.distancesq(points[selected.x].xz,points[selected.y].xz);foreach(var a in points)foreach(var b in points)if(math.distancesq(a.xz,b.xz)>d+.0001f)throw new Exception("Adjacent pair selected");
   foreach(int i in new[]{selected.x,selected.y})
   {
    var q=(quaternion)rotation.Invoke(null,new object[]{points[i],new float3(0,3,0)});var heading=math.rotate(q,new float3(1,0,0));
    if(math.dot(heading,math.normalizesafe(new float3(-points[i].x,0,-points[i].z)))<.999f)throw new Exception("Fixture faces away from centre");
   }
   count++;
  }
  var invalid=(int2)pair.Invoke(null,new object[]{new List<float3>()});if(invalid.x!=-1||invalid.y!=-1)throw new Exception("Invalid support set accepted");
  Console.WriteLine("PASS: "+count+" rotated, mixed-width and multi-leg two-fixture placements; native fixture axis faces centre.");
 }
}
