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
class MinorWireAngles229
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
        int fits=0,rejected=0;var rows=new List<object>();
        for(int pass=0;pass<20;pass++)foreach(float jitter in new[]{0f,.0005f,.001f,.0015f})foreach(byte style in new byte[]{1,3})
        {
            var b=new List<WireBank229>();var p=new List<float3>();
            for(int i=0;i<4;i++)
            {
                float angle=pass*math.PI/10+i*math.PI/2+(i<2?jitter:-jitter);var d=new float2(math.cos(angle),math.sin(angle));var side=new float2(-d.y,d.x);
                var mouth=new float3(d.x*16,100,d.y*16);var lateral=new float3(side.x,0,side.y);
                Func<float3,float[]> A=x=>new[]{x.x,x.y,x.z};
                b.Add(new WireBank229{left=A(mouth+lateral*8),right=A(mouth-lateral*8),outward=new[]{d.x,d.y},lanes=new[]{A(mouth+lateral*1.5f),A(mouth+lateral*4.5f)}});
                float corner=pass*math.PI/10+i*math.PI/2+math.PI*1.25f;p.Add(new float3(math.cos(corner)*24,100,math.sin(corner)*24));
            }
            var roads=Roads(b.ToArray());var sources=new List<int>{0,1,2,3};var args=new object[]{p,roads,sources,style,null};
            bool ok=(bool)Call("TryFit",args);if(ok){fits++;Check((bool)Call("Valid",args[4],roads,sources,style),"Accepted invalid correction");}else rejected++;
            rows.Add(new{pass,jitter,style,ok});
        }
        File.WriteAllText("WireCorner229-MinorAngles.json",new JavaScriptSerializer().Serialize(new{fits,rejected,checks,rows,scope="Opposing native-shaped road tangents differ by up to .172 degrees; a common transverse layout remains within the existing .115 degree per-road tolerance. No native generation claim."}));
        Console.WriteLine("Minor opposing-angle reproduction: "+fits+" fitted, "+rejected+" rejected; "+checks+" validity checks.");
    }
}
