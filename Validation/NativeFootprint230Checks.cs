using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Unity.Mathematics;

class NativeFootprint230Checks
{
    static readonly BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic, F=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly Type T=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.SignalJunctionFootprint",true);
    static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static object Call(string name,params object[] args)=>T.GetMethod(name,S).Invoke(null,args);
    static bool Clear(object shape,float2 point,float radius)=> (bool)T.GetMethod("HasClearance",F).Invoke(shape,new object[]{point,radius});
    static Bezier4x3 Curve(float z)=>new Bezier4x3(new float3(20,0,z),new float3(10,0,z),new float3(-5,0,z*.5f),new float3(-5,0,z*.5f));
    static void Main()
    {
        var native=Assembly.LoadFrom("NativeCurveOracle230.dll").GetType("NativeCurveOracle",true);
        Check(native!=null,"Installed native secondary-object job missing");
        var method=native.GetMethod("LerpRemap",BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
        Check(method!=null,"Installed native curve method missing");var job=Activator.CreateInstance(native);
        int curves=0,sites=0;
        for(int profile=0;profile<40;profile++)for(int lateral=0;lateral<=20;lateral++)
        {
            var left=Curve(-10);var right=Curve(10);
            float angle=profile*math.PI/10;var shift=new float3(profile%2==0?0:12000,profile%3,-9000);
            Func<float3,float3> P=p=>shift+new float3(p.x*math.cos(angle)-p.z*math.sin(angle),p.y,p.x*math.sin(angle)+p.z*math.cos(angle));
            Func<Bezier4x3,Bezier4x3> B=b=>new Bezier4x3(P(b.a),P(b.b),P(b.c),P(b.d));left=B(left);right=B(right);
            var offsets=profile<20?new float4(.1f,.3f,.6f,.9f):new float4(.3f,.3000001f,.6f,.9f);var targets=profile%2==0?offsets:new float4(.05f,.4f,.7f,.95f);
            float at=profile>=20&&lateral<3?new[]{.3f,.30000004f,.3000001f}[lateral]:lateral/20f;var expected=(Bezier4x3)method.Invoke(job,new object[]{left,right,at,offsets,targets});
            var actual=(Bezier4x3)Call("Curve",left,right,at,offsets,targets);
            Check(math.distance(expected.a,actual.a)<.003f&&math.distance(expected.b,actual.b)<.003f&&math.distance(expected.c,actual.c)<.003f&&math.distance(expected.d,actual.d)<.003f,"Native remap differs");curves++;
        }
        for(int pass=0;pass<20;pass++)
        {
            float angle=pass*math.PI/10;var shift=new float3(pass%2==0?0:12000,pass%3,pass%2==0?0:-9000);
            Func<float3,float3> P=p=>shift+new float3(p.x*math.cos(angle)-p.z*math.sin(angle),p.y,p.x*math.sin(angle)+p.z*math.cos(angle));
            Func<Bezier4x3,Bezier4x3> B=b=>new Bezier4x3(P(b.a),P(b.b),P(b.c),P(b.d));
            var data=new NetCompositionData{m_Width=20,m_MiddleOffset=0,m_SyncVertexOffsetsLeft=new float4(.1f,.3f,.6f,.9f),m_SyncVertexOffsetsRight=new float4(.1f,.3f,.6f,.9f)};
            var geometry=new EdgeNodeGeometry{m_Left=new Segment{m_Left=B(Curve(-10)),m_Right=B(Curve(0))},m_Right=new Segment{m_Left=B(Curve(0)),m_Right=B(Curve(10))},m_SyncVertexTargetsLeft=data.m_SyncVertexOffsetsLeft,m_SyncVertexTargetsRight=data.m_SyncVertexOffsetsRight};
            var shape=Call("Create",geometry,data,-8f,8f);Check(shape!=null,"Valid native-shaped junction rejected");
            Check(!Clear(shape,P(new float3(5,0,0)).xz,.41f),"Pavement interior admitted");
            Check(Clear(shape,P(new float3(5,0,30)).xz,.41f),"Clear outer site rejected");
            for(int x=-25;x<=25;x++)for(int y=-20;y<=20;y++)foreach(float radius in new[]{0f,.41f,.65f})
            {
                var point=P(new float3(x,0,y)).xz;var args=new object[]{point,radius,null};
                bool slow=(bool)T.GetMethod("Clear",F).Invoke(shape,args);bool fast=Clear(shape,point,radius);
                Check(slow==fast,"Accelerated footprint check weakened clearance");sites++;
            }
            geometry.m_MiddleRadius=5;Check(Call("Create",geometry,data,-8f,8f)==null,"Unimplemented split-radius geometry guessed");
        }
        Check(Call("FromOutline",new List<float2>{float2.zero,new float2(1,0),new float2(float.NaN,1)})==null,"Nonfinite polygon accepted");
        Check(Call("FromOutline",new List<float2>{float2.zero,new float2(1,0),new float2(2,0)})==null,"Degenerate polygon accepted");
        File.WriteAllText("NativeFootprint230Checks.json",new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{checks,curves,sites,scope="Installed native LerpRemap IL in an isolated oracle; no native job execution, synthetic EdgeNodeGeometry factory and full-base polygon checks. No native rendering, actual city curves, physics, save or FPS execution."}));
        Console.WriteLine("PASS: "+checks+" assertions; "+curves+" installed-native curve comparisons, "+sites+" fast/slow clearance comparisons over20 transforms, including large world coordinates.");
    }
}
