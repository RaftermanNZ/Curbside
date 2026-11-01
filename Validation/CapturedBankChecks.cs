using System;using System.Collections;using System.Collections.Generic;using System.Reflection;using Unity.Mathematics;using Transform=Game.Objects.Transform;
class CapturedBankChecks {
 static readonly Type Policy=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.FarSideLightPolicy"),Approach=Policy.GetNestedType("Approach",BindingFlags.NonPublic);
 static void Add(IList roads,float3 left,float3 right,float2 outward,List<float3> incoming){var a=Activator.CreateInstance(Approach);foreach(var kv in new Dictionary<string,object>{{"Left",left},{"Right",right},{"Outward",outward},{"Incoming",incoming}})Approach.GetField(kv.Key,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(a,kv.Value);roads.Add(a);}
 static void Main(){var roads=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Approach));
Add(roads,new float3(-1075.69f,126.1831f,-2999.273f),new float3(-1069.311f,126.1831f,-3016.104f),new float2(0.9351004f,0.3543831f),new List<float3>{new float3(-1074.45f,126.1831f,-3002.546f),new float3(-1073.387f,126.1831f,-3005.351f)});
Add(roads,new float3(-1101.312f,126.2239f,-3032.015f),new float3(-1073.259f,126.2239f,-3021.384f),new float2(0.3543641f,-0.9351075f),new List<float3>{new float3(-1084.948f,126.2239f,-3025.814f),new float3(-1082.142f,126.2239f,-3024.75f),new float3(-1079.337f,126.2239f,-3023.688f),new float3(-1076.532f,126.2239f,-3022.625f)});
Add(roads,new float3(-1115.783f,126.3651f,-2993.831f),new float3(-1087.061f,126.3496f,-2984.962f),new float2(-0.3543724f,0.9351044f),new List<float3>{new float3(-1112.432f,126.3633f,-2992.796f),new float3(-1109.56f,126.3618f,-2991.909f),new float3(-1106.688f,126.3602f,-2991.022f),new float3(-1103.815f,126.3587f,-2990.135f)});
Add(roads,new float3(-1108.429f,126.2878f,-3028.789f),new float3(-1119.06f,126.2879f,-3000.736f),new float2(-0.9351018f,-0.3543792f),new List<float3>{new float3(-1109.669f,126.2878f,-3025.517f),new float3(-1110.732f,126.2878f,-3022.711f),new float3(-1111.795f,126.2878f,-3019.906f),new float3(-1112.858f,126.2878f,-3017.101f)});
 var originals=new[]{new float3(-1072.501f,126.1831f,-3007.689f),new float3(-1072.92f,126.2209f,-3020.725f),new float3(-1116.109f,126.3605f,-2994.455f),new float3(-1107.776f,126.2843f,-3029.103f)};
 for(int i=0;i<roads.Count;i++){
  object[] args={new Transform{m_Position=originals[i],m_Rotation=quaternion.identity},roads,i,new List<float3>(),null};if(!(bool)Policy.GetMethod("TryArmMountFrom",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args))throw new Exception("Captured mouth has no candidate: "+i);
  var placed=(Transform)args[4];bool right=(bool)Policy.GetMethod("DriverRightBank",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{placed.m_Position,roads[i]});
  object[] fit={placed.m_Position,roads,i,null,0f};if(!(bool)Policy.GetMethod("RoadsideArmCandidate",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,fit))throw new Exception("Invalid native-bank result");
  Console.WriteLine("captured mouth="+i+" driverRight="+right+" requiredReach="+fit[4]+" position="+placed.m_Position);
  if(right!=(i!=0))throw new Exception("Captured geometry did not choose expected efficient side");
 }
 Console.WriteLine("PASS: four captured native mouths retain valid far-side, roadside, lane-covering candidates; narrow incoming arm avoids excessive driver-right reach. Grounding, occupation order and live phases still require in-game verification.");
 }
}
