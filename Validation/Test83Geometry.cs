using System;using System.Collections;using System.Collections.Generic;using System.Reflection;using Unity.Mathematics;using Transform=Game.Objects.Transform;
class Test83Geometry
{
 static Assembly asm=Assembly.LoadFrom("MedianGrassReplacement.dll");
 static BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
 static Type policy=asm.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy"),approach=policy.GetNestedType("Approach",BindingFlags.NonPublic),garden=asm.GetType("MedianGrassReplacement.Systems.GardenCityLightPolicy"),power=asm.GetType("MedianGrassReplacement.Systems.VisualPowerlinePolicy");
 static int checks,fits,fallbacks;
 static void Check(bool v,string m){checks++;if(!v)throw new Exception(m);}
 static void Field(object o,string n,object v){approach.GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,v);}
 static object Road(float angle,float width,int lanes,float shift)
 {
  float2 outwards=new float2((float)Math.Cos(angle),(float)Math.Sin(angle)),side=new float2(-outwards.y,outwards.x);
  float2 centre=outwards*(14+width*.25f)+side*shift;
  object road=Activator.CreateInstance(approach);var incoming=new List<float3>();var masks=new List<ushort>();
  for(int lane=0;lane<lanes;lane++){float2 p=centre+side*(.8f+2.8f*(lane+.5f));incoming.Add(new float3(p.x,0,p.y));masks.Add((ushort)(1<<(lane%3)));}
  Field(road,"Left",new float3(centre.x-side.x*width,0,centre.y-side.y*width));Field(road,"Right",new float3(centre.x+side.x*width,0,centre.y+side.y*width));Field(road,"Outward",outwards);Field(road,"Incoming",incoming);Field(road,"LaneMasks",masks);return road;
 }
 static void Main()
 {
  foreach(int legs in new[]{2,3,4,5,6,7,8,10,12})foreach(int degree in new[]{0,17,43,91,133,211,289})foreach(int skew in new[]{0,9,-11})foreach(int lanes in new[]{1,2,4,8,12,16})foreach(float width in new[]{4f,10f,22f})
  {
   var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(approach));
   for(int i=0;i<legs;i++)roads.Add(Road((degree+i*360f/legs+(i%2)*skew)*(float)Math.PI/180,width+(i%3)*2,lanes,i%2==0?0:3));
   var source=new Transform{m_Position=(float3)approach.GetField("Left",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(roads[0]),m_Rotation=quaternion.identity};
   object[] args={source,roads,0,new List<float3>(),null};bool fit=(bool)policy.GetMethod("TryArmMountFrom",flags).Invoke(null,args);
   if(!fit){fallbacks++;continue;}fits++;var placed=(Transform)args[4];Check(math.all(math.isfinite(placed.m_Position)),"nonfinite support");
   object[] candidate={placed.m_Position,roads[0],null,0f};Check((bool)policy.GetMethod("ArmCandidate",flags).Invoke(null,candidate),"invalid accepted lane envelope");Check((float)candidate[3]<=30f,"oversize arm");
   foreach(ushort mask in new ushort[]{1,2,4,8})
   {
    object filtered=garden.GetMethod("PhaseApproach",flags).Invoke(null,new object[]{roads[0],mask});
    var kept=(List<ushort>)approach.GetField("LaneMasks",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(filtered);
    foreach(ushort item in kept)Check((item&mask)!=0,"foreign protected phase included");
    if(mask==8)Check(kept.Count==0,"unknown phase borrowed another lane");
   }
  }
  for(int legs=2;legs<=12;legs++)for(int degree=0;degree<360;degree+=15)
  {
   var points=new List<float3>();var tangents=new List<float3>();var alley=new List<bool>();
   for(int i=0;i<legs;i++){float a=(degree+i*360f/legs)*(float)Math.PI/180;var radial=new float3((float)Math.Cos(a),0,(float)Math.Sin(a));points.Add(radial*(18+i%3*5));tangents.Add(radial);alley.Add(i%4==0);}
   var pairs=(List<int2>)power.GetMethod("StraightConnections",flags).Invoke(null,new object[]{points,tangents,alley,float3.zero});var parent=new int[legs];for(int i=0;i<legs;i++)parent[i]=i;Func<int,int> root=null;root=i=>parent[i]==i?i:root(parent[i]);
   foreach(var pair in pairs){Check(pair.x>=0&&pair.y<legs&&pair.x!=pair.y,"invalid span index");Check(root(pair.x)!=root(pair.y),"utility loop");parent[root(pair.x)]=root(pair.y);foreach(var other in pairs)if(pair.x!=other.x&&pair.x!=other.y&&pair.y!=other.x&&pair.y!=other.y)Check(!(bool)power.GetMethod("Crosses",flags).Invoke(null,new object[]{points[pair.x].xz,points[pair.y].xz,points[other.x].xz,points[other.y].xz}),"crossing spans");}
  }
  // Two parallel roadside runs should remain straight across a four-ended node.
  var p=new List<float3>{new float3(-10,0,-4),new float3(10,0,-4),new float3(-10,0,4),new float3(10,0,4)};var t=new List<float3>{new float3(1,0,0),new float3(1,0,0),new float3(1,0,0),new float3(1,0,0)};
  var result=(List<int2>)power.GetMethod("StraightConnections",flags).Invoke(null,new object[]{p,t,new List<bool>{false,false,false,false},float3.zero});
  Check(result.Count>=2&&result[0].x==0&&result[0].y==1&&result[1].x==2&&result[1].y==3,"straight roadside spans lost to short corner connections");
  var longRun=new List<float3>{new float3(-35,0,0),new float3(35,0,0),new float3(-34,0,2)};
  var longTangents=new List<float3>{new float3(1,0,0),new float3(1,0,0),new float3(0,0,1)};
  var longPairs=(List<int2>)power.GetMethod("StraightConnections",flags).Invoke(null,new object[]{longRun,longTangents,new List<bool>{false,false,false},float3.zero});
  Check(longPairs.Count>0&&longPairs[0].x==0&&longPairs[0].y==1,"short angled shortcut displaced a valid long straight run");
  Console.WriteLine("PASS Test83 geometry: "+checks+" invariants; "+fits+" fitted and "+fallbacks+" safely unsupported configurations; 2-12 legs, rotations/skew/width/lane/phase matrix; utility no-loop/no-crossing and straight-priority checks.");
 }
}
