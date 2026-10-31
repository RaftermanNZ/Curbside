using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Mathematics;
using Transform=Game.Objects.Transform;
class ArmBankChecks
{
 static int checks;static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
 static readonly Assembly Assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");
 static readonly Type Policy=Assembly.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy");
 static readonly Type Approach=Policy.GetNestedType("Approach",BindingFlags.NonPublic);
 static readonly MethodInfo Mount=Policy.GetMethod("TryArmMountFrom",BindingFlags.NonPublic|BindingFlags.Static);
 static float3 Rotate(float x,float z,float angle){return new float3(x*(float)Math.Cos(angle)-z*(float)Math.Sin(angle),0,x*(float)Math.Sin(angle)+z*(float)Math.Cos(angle));}
 static object Road(float half,float z,float angle,List<float3> incoming)
 {
  object road=Activator.CreateInstance(Approach);
  foreach(var item in new Dictionary<string,object>{{"Left",Rotate(-half,z,angle)},{"Right",Rotate(half,z,angle)},{"Outward",Rotate(0,Math.Sign(z),angle).xz},{"Incoming",incoming}})Approach.GetField(item.Key,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(road,item.Value);
  return road;
 }
 static void Main()
 {
  for(int count=1;count<=8;count++)foreach(float laneWidth in new[]{2.6f,3.2f,3.8f})foreach(float oppositeHalf in new[]{3f,10f,20f})foreach(float laneSide in new[]{-1f,1f})for(int degrees=0;degrees<360;degrees+=30)foreach(float sourceSide in new[]{-1f,1f})
  {
   float angle=degrees*(float)Math.PI/180;var incoming=new List<float3>();
   for(int i=0;i<count;i++)incoming.Add(Rotate(laneSide*(.8f+laneWidth*(i+.5f)),-20,angle));
   float half=count*laneWidth+1;var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Approach));roads.Add(Road(half,-20,angle,incoming));roads.Add(Road(oppositeHalf,20,angle,new List<float3>()));
   var source=new Transform{m_Position=Rotate(sourceSide*half,-20,angle),m_Rotation=quaternion.identity};
   object[] args={source,roads,0,new List<float3>(),null};bool found=(bool)Mount.Invoke(null,args);
   float best=float.MaxValue,bestCost=float.MaxValue;bool bestRight=false;
   foreach(float bank in new[]{sourceSide*(oppositeHalf+.25f),-oppositeHalf-.6f,oppositeHalf+.6f})
   {
    bool valid=true;float reach=0;
    for(int i=0;i<count;i++){float distance=Math.Abs(laneSide*(.8f+laneWidth*(i+.5f))-bank);if(distance<.349f||distance>29.501f)valid=false;reach=Math.Max(reach,Math.Min(29.5f,Math.Max(.35f,distance))+.35f);}
    float first=laneSide*(.8f+laneWidth*.5f)-bank,last=laneSide*(.8f+laneWidth*(count-.5f))-bank;
    if(first*last<0)valid=false;bool right=bank-Math.Max(laneSide*(.8f+laneWidth*.5f),laneSide*(.8f+laneWidth*(count-.5f)))>=.349f;float meshReach=3f+1.5f*(float)Math.Ceiling(Math.Max(0,(reach-3f-.001f)/1.5f));float cost=meshReach+(right?0:3f);if(valid&&(cost<bestCost-.05f||(Math.Abs(cost-bestCost)<=.05f&&((right&&!bestRight)||(right==bestRight&&reach<best-.05f))))){best=reach;bestRight=right;bestCost=cost;}
   }
   Check(found==(best<float.MaxValue),"unsupported lane envelope accepted, or valid bank rejected");if(!found)continue;
   var placed=(Transform)args[4];var candidate=Policy.GetMethod("ArmCandidate",BindingFlags.Static|BindingFlags.NonPublic);object[] fit={placed.m_Position,roads[0],null,0f};
   Check((bool)candidate.Invoke(null,fit),"selected arm does not cover its lanes");Check(Math.Abs((float)fit[3]-best)<.06f,"arm did not choose preferred-side shortest valid bank count="+count+" laneWidth="+laneWidth+" oppositeHalf="+oppositeHalf+" side="+laneSide+" source="+sourceSide+" rotation="+degrees+" expected="+best+" actual="+fit[3]+" position="+placed.m_Position);
   Check(math.abs(math.dot(placed.m_Position.xz,Rotate(0,1,angle).xz)-20)<.001f,"pole moved off far-side bank");
   var occupied=new List<float3>{placed.m_Position};args=new object[]{source,roads,0,occupied,null};
   if((bool)Mount.Invoke(null,args))Check(math.distancesq(((Transform)args[4]).m_Position.xz,placed.m_Position.xz)>=1.5625f,"two poles occupy one mount");
  }
  for(int count=1;count<=3;count++)for(int degrees=0;degrees<360;degrees+=30)foreach(float sourceSide in new[]{-1f,1f})
  {
   float angle=degrees*(float)Math.PI/180,half=count*3.2f+1;var incoming=new List<float3>();for(int i=0;i<count;i++)incoming.Add(Rotate(.8f+3.2f*(i+.5f),-20,angle));
   var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Approach));roads.Add(Road(half,-20,angle,incoming));
   foreach(float side in new[]{-1f,1f})
   {
    object through=Activator.CreateInstance(Approach);
    foreach(var item in new Dictionary<string,object>{{"Left",Rotate(side*20,-5,angle)},{"Right",Rotate(side*20,5,angle)},{"Outward",Rotate(side,0,angle).xz},{"Incoming",new List<float3>()}})Approach.GetField(item.Key,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(through,item.Value);
    roads.Add(through);
   }
   var source=new Transform{m_Position=Rotate(sourceSide*half,-20,angle),m_Rotation=quaternion.identity};object[] args={source,roads,0,new List<float3>(),null};
   Check((bool)Mount.Invoke(null,args),"T junction without opposite road rejected");var placed=(Transform)args[4];
   Check(math.dot(placed.m_Position.xz,Rotate(0,1,angle).xz)>5.2f,"T junction pole is not on far sidewalk");
   Check(math.dot(placed.m_Position.xz,Rotate(1,0,angle).xz)>0,"T junction chose unnecessarily distant bank");
  }
  Console.WriteLine(checks+" arm-bank checks passed across rotated narrow, wide, divided, one-way and asymmetric lane envelopes; unsupported reaches and occupied mounts checked.");
 }
}
