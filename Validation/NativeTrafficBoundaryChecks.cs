using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Colossal.Mathematics;
using Unity.Mathematics;
class NativeTrafficBoundaryChecks
{
 static void Main()
 {
  var assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");
  var item=assembly.GetType("MedianGrassReplacement.Systems.NativeTrafficBoundary",true);
  var method=assembly.GetType("MedianGrassReplacement.Systems.NativeTrafficBoundaryPolicy",true).GetMethod("Classify",BindingFlags.NonPublic|BindingFlags.Static);
  IList lanes=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(item));
  Bezier4x3 Path(float x,bool reverse=false,float y=0){var a=new float3(x,y,0);var d=new float3(x,y,40);return reverse?new Bezier4x3(d,math.lerp(d,a,1f/3),math.lerp(d,a,2f/3),a):new Bezier4x3(a,math.lerp(a,d,1f/3),math.lerp(a,d,2f/3),d);}
  void Add(float x,bool reverse,bool left,bool right,float y=0){var lane=Activator.CreateInstance(item);void Set(string name,object value)=>item.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(lane,value);Set("Curve",Path(x,reverse,y));Set("Width",3f);Set("Left",left);Set("Right",right);lanes.Add(lane);}
  int cases=0;
  void Check(Bezier4x3 path,byte expected,string label){byte actual=(byte)method.Invoke(null,new object[]{path,lanes});if(actual!=expected)throw new Exception(label+": "+actual+" expected "+expected);cases++;}
  foreach(bool reversePaint in new[]{false,true})
  {
   lanes.Clear();Add(1.5f,false,true,true);
   Check(Path(0,reversePaint),1,"one-way traffic-left boundary");Check(Path(3,reversePaint),2,"outside edge");
   Add(-1.5f,true,true,true);Check(Path(0,reversePaint),1,"opposing native lanes");
   lanes.Clear();Add(-1.5f,false,false,true);Add(1.5f,false,true,false);Check(Path(0,reversePaint),3,"same-direction divider must remain white");
   lanes.Clear();Add(1.5f,false,false,true);Check(Path(0,reversePaint),0,"unproven left limit");
   lanes.Clear();Add(1.5f,false,true,true,2);Check(Path(0,reversePaint),0,"grade-separated lane");
   lanes.Clear();Add(1.5f,false,true,true);Check(Path(-.8f,reversePaint),0,"distant boundary");
   var crossing=new Bezier4x3(new float3(-10,0,20),new float3(-3,0,20),new float3(3,0,20),new float3(10,0,20));Check(crossing,0,"crossing traffic");
  }
  Console.WriteLine("PASS: "+cases+" native traffic-boundary cases: directed lanes, median, outer edge, divider, grade, distance and crossing rejection. Runtime native road configurations still require visual validation.");
 }
}
