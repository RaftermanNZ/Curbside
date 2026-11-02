using System;using System.Collections;using System.Collections.Generic;using System.Reflection;using Unity.Mathematics;using Transform=Game.Objects.Transform;
class RightBankChecks {
 static int checks;static void Check(bool b,string s){checks++;if(!b)throw new Exception(s);}
 static readonly Type Policy=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.FarSideLightPolicy");
 static readonly Type Approach=Policy.GetNestedType("Approach",BindingFlags.NonPublic);
 static readonly MethodInfo Mount=Policy.GetMethod("TryArmMountFrom",BindingFlags.NonPublic|BindingFlags.Static),Right=Policy.GetMethod("DriverRightBank",BindingFlags.NonPublic|BindingFlags.Static),Adjust=Policy.GetMethod("AcceptArmAdjustment",BindingFlags.NonPublic|BindingFlags.Static);
 static object Road(float half,float z,List<float3> lanes){var a=Activator.CreateInstance(Approach);foreach(var item in new Dictionary<string,object>{{"Left",new float3(-half,0,z)},{"Right",new float3(half,0,z)},{"Outward",new float2(0,Math.Sign(z))},{"Incoming",lanes}})Approach.GetField(item.Key,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(a,item.Value);return a;}
 static bool IsRight(float3 p,object a)=>(bool)Right.Invoke(null,new object[]{p,a});
 static Transform Place(float half,List<float3> lanes,out object a){a=Road(20,-20,lanes);var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Approach));roads.Add(a);roads.Add(Road(half,20,new List<float3>()));object[] args={new Transform{m_Position=new float3(20.6f,0,-20),m_Rotation=quaternion.identity},roads,0,new List<float3>(),null};Check((bool)Mount.Invoke(null,args),"no valid support found");return (Transform)args[4];}
 static void Main(){
  object a;var lanes=new List<float3>{new float3(-2.5f,0,-20),new float3(.5f,0,-20)};var pole=Place(10,lanes,out a);
  Check(IsRight(pole.m_Position,a)&&pole.m_Position.x>10,"comparable shorter left bank displaced feasible driver-right support");
  Check(!(bool)Adjust.Invoke(null,new object[]{pole.m_Position,new float3(-10.6f,0,20),a,13.5f,12f,.75f}),"curb snap or sharing downgraded right to shorter left");
  Check((bool)Adjust.Invoke(null,new object[]{new float3(-10.6f,0,20),pole.m_Position,a,13.5f,15f,.75f}),"feasible driver-right promotion rejected");
  pole=Place(10,new List<float3>{new float3(-5,0,-20),new float3(-2,0,-20)},out a);
  Check(!IsRight(pole.m_Position,a),"substantially longer right bank should yield to shorter valid left");
  Check((bool)Adjust.Invoke(null,new object[]{new float3(10.6f,0,20),new float3(-10.6f,0,20),a,16.5f,9f,.75f}),"substantial same-validity reduction rejected");
  pole=Place(3,new List<float3>{new float3(2,0,-20),new float3(5,0,-20)},out a);
  Check(!IsRight(pole.m_Position,a)&&pole.m_Position.x<0,"narrow opposite curb inside lane envelope should fall back to valid left bank");
  pole=Place(3,new List<float3>{new float3(5,0,-20),new float3(8,0,-20)},out a);
  Check(!IsRight(pole.m_Position,a)&&pole.m_Position.x>0,"fallback should use shortest valid left-of-lanes support, not road curb label");
  pole=Place(10,new List<float3>{new float3(2,0,-20),new float3(5,0,-20)},out a);
  Check(IsRight(pole.m_Position,a),"ordinary driver-right support rejected");
  Check(!IsRight(new float3(float.NaN,0,0),a),"nonfinite driver-side classification accepted");
  Console.WriteLine(checks+" right-bank preference checks passed: feasible right before shorter left; snap/share preservation; narrow-road straddling and one-sided projected-envelope fallback.");
 }
}