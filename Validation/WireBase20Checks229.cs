using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using Unity.Mathematics;
class WireBank229 {public float[] left,right,outward;public float[][] lanes;}
class WireCase229 {public string node,time;public WireBank229[] banks;public float[][] supports;public int[] sources;public bool expectedRepair;}
class WireBase20Checks229
{
    const BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic,F=BindingFlags.Instance|BindingFlags.NonPublic;
    static Assembly assembly;static Type approach,policy;static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static object Call(string method,params object[] args)=>policy.GetMethod(method,S).Invoke(null,args);
    static float3 V(float[] p)=>new float3(p[0],p[1],p[2]);
    static IList Roads(WireBank229[] banks)
    {
        var result=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(approach));
        foreach(var b in banks)
        {
            var road=Activator.CreateInstance(approach);
            approach.GetField("Left",F).SetValue(road,V(b.left));approach.GetField("Right",F).SetValue(road,V(b.right));
            approach.GetField("Outward",F).SetValue(road,new float2(b.outward[0],b.outward[1]));
            approach.GetField("Incoming",F).SetValue(road,b.lanes.Select(V).ToList());
            approach.GetField("LaneMasks",F).SetValue(road,b.lanes.Select(_=>(ushort)1).ToList());result.Add(road);
        }
        return result;
    }
    static void Main()
    {
        assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");
        approach=assembly.GetType("MedianGrassReplacement.Systems.FarSideLightPolicy+Approach",true);
        policy=assembly.GetType("MedianGrassReplacement.Systems.WireCornerLayoutPolicy",true);
        var pack=assembly.GetType("GardenCitySignals.SignalPackPolicy",true);
        pack.GetProperty("UseRigidSignLayout",S).GetSetMethod(true).Invoke(null,new object[]{true});
        pack.GetProperty("UseLowerRigidSignLayout",S).GetSetMethod(true).Invoke(null,new object[]{true});
        for(int pass=0;pass<20;pass++)
        {
            float angle=pass*math.PI/10,mirror=pass%2==0?1:-1;
            Func<float3,float3> P=x=>new float3(mirror*x.x*math.cos(angle)-x.z*math.sin(angle)+pass*10,x.y,mirror*x.x*math.sin(angle)+x.z*math.cos(angle)-pass*7);
            Func<float3,float[]> A=x=>new[]{x.x,x.y,x.z};
            var banks=new List<WireBank229>();
            for(int i=0;i<4;i++)
            {
                float theta=i*math.PI/2;var d=new float2(math.cos(theta),math.sin(theta));var side=new float3(-d.y,0,d.x);var mouth=new float3(d.x*16,0,d.y*16);
                var outward=P(new float3(d.x,0,d.y))-P(float3.zero);
                banks.Add(new WireBank229{left=A(P(mouth+side*8)),right=A(P(mouth-side*8)),outward=new[]{outward.x,outward.z},lanes=new[]{A(P(mouth+side*2))}});
            }
            var roads=Roads(banks.ToArray());
            Check(!(bool)Call("ClearBase",P(new float3(17,0,8.45f)),roads),"Centre-only false clearance admitted pole base");
            Check(!(bool)Call("ClearBase",P(new float3(17,0,0)),roads),"Road centre admitted");
            Check(!(bool)Call("ClearBase",P(new float3(17,0,3.5f)),roads),"Median admitted");
            Check((bool)Call("ClearBase",P(new float3(17,0,8.8f)),roads),"Safe outer curb rejected");
            Check(!(bool)Call("ClearBase",new float3(float.NaN,0,0),roads),"NaN admitted");
        }
        Console.WriteLine("PASS: "+checks+" full-base/road/median/invalid-input assertions across 20 rotations/reflections/translations. Outward bank envelopes only; native curved turning-lane sweeps not executed.");
    }
}
