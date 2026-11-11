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
class WireCornerChecks229
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
        var cases=new JavaScriptSerializer().Deserialize<WireCase229[]>(File.ReadAllText("WireCorners229.json"));
        foreach(var input in cases)foreach(byte style in new byte[]{1,3})
        {
            var roads=Roads(input.banks);var points=input.supports.Select(V).ToList();var sources=input.sources.ToList();
            bool before=(bool)Call("Valid",points,roads,sources,style);
            Check(before!=input.expectedRepair,"Original classification "+input.node);
            var args=new object[]{points,roads,sources,style,null};
            Check((bool)Call("TryFit",args),"Captured fit rejected "+input.node+" style"+style);
            var fit=(List<float3>)args[4];Check((bool)Call("Valid",fit,roads,sources,style),"Accepted invalid fit");
            Check(fit.Count==input.supports.Length,"Support count changed");
            if(!input.expectedRepair)Check(points.SequenceEqual(fit),"Valid captured control moved");
            for(int i=0;i<fit.Count;i++)for(int j=0;j<i;j++)Check(math.distancesq(fit[i].xz,fit[j].xz)>=6.25f,"Duplicate corner");
            for(int support=0;support<fit.Count;support++)
            {
                float3 centre=float3.zero;foreach(var p in fit)centre+=p;centre/=fit.Count;
                var shifted=(List<float3>)Call("Shift",fit,roads,support,fit[support]+new float3(math.normalizesafe(fit[support].xz-centre.xz)*.1f,0).xzy);
                Check(shifted!=null,"Coherent shift failed");
                Check((bool)Call("Valid",shifted,roads,sources,style),"Coherent shift broke topology "+input.node);
            }
            var bad=new List<float3>(fit);bad[0]=new float3(float.NaN,0,0);
            Check(!(bool)Call("Valid",bad,roads,sources,style),"NaN accepted");
            var duplicate=new List<int>(sources);duplicate[1]=duplicate[0];
            Check(!(bool)Call("Valid",fit,roads,duplicate,style),"Repeated approach accepted");
        }
        Console.WriteLine("PASS: "+checks+" compiled captured wire corner/shift guards across "+cases.Length+" snapshots and timber/metal styles. Native ECS, terrain and rendered acceptance pending.");
    }
}
