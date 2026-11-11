using System;
using System.IO;
using System.Reflection;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

class NativeFootprintCache230Checks
{
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static Bezier4x3 Curve(float z)=>new Bezier4x3(new float3(20,0,z),new float3(10,0,z),new float3(-5,0,z*.5f),new float3(-5,0,z*.5f));
    static void Main()
    {
        var t=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.SignalJunctionFootprintCache",true);
        var cache=Activator.CreateInstance(t,true);var get=t.GetMethod("Get",BindingFlags.NonPublic|BindingFlags.Instance);
        var data=new NetCompositionData{m_Width=20,m_SyncVertexOffsetsLeft=new float4(.1f,.3f,.6f,.9f),m_SyncVertexOffsetsRight=new float4(.1f,.3f,.6f,.9f)};
        var geo=new EdgeNodeGeometry{m_Left=new Segment{m_Left=Curve(-10),m_Right=Curve(0)},m_Right=new Segment{m_Left=Curve(0),m_Right=Curve(10)},m_SyncVertexTargetsLeft=data.m_SyncVertexOffsetsLeft,m_SyncVertexTargetsRight=data.m_SyncVertexOffsetsRight};
        var road=new Entity{Index=1,Version=1};var composition=new Entity{Index=10,Version=1};
        Func<Entity,bool,Entity,EdgeNodeGeometry,NetCompositionData,float,float,object> read=(r,s,c,g,d,lo,hi)=>get.Invoke(cache,new object[]{r,s,c,g,d,lo,hi});
        var first=read(road,true,composition,geo,data,-8,8);Check(first!=null,"Valid outline missing");
        for(int i=0;i<5000;i++)Check(ReferenceEquals(first,read(road,true,composition,geo,data,-8,8)),"Unchanged geometry recomputed");
        Check(!ReferenceEquals(first,read(road,false,composition,geo,data,-8,8)),"Opposite endpoint aliased");
        var version=road;version.Version++;Check(!ReferenceEquals(first,read(version,true,composition,geo,data,-8,8)),"Reused entity version aliased");
        var moved=geo;moved.m_Left.m_Left.c.x+=.25f;var changed=read(road,true,composition,moved,data,-8,8);Check(!ReferenceEquals(first,changed),"Control-point change missed");
        var width=data;width.m_Width=22;Check(!ReferenceEquals(changed,read(road,true,composition,moved,width,-8,8)),"Width change missed");
        var sync=geo;sync.m_SyncVertexTargetsRight.y+=.05f;Check(!ReferenceEquals(first,read(road,true,composition,sync,data,-8,8)),"Native sync change missed");
        Check(!ReferenceEquals(first,read(road,true,composition,geo,data,-7,8)),"Curb-anchor change missed");
        var radius=geo;radius.m_MiddleRadius=2;Check(read(road,true,composition,radius,data,-8,8)==null,"Unsupported geometry guessed");
        Check(read(road,true,composition,geo,data,-8,8)!=null,"Negative entry was not invalidated");
        for(int i=0;i<1100;i++)read(new Entity{Index=100+i,Version=1},true,composition,geo,data,-8,8);
        int count=(int)t.GetProperty("Count",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(cache);
        Check(count==1024,"Cache exceeded fixed capacity");
        t.GetMethod("Clear",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(cache,null);
        Check((int)t.GetProperty("Count",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(cache)==0,"Scene clear retained geometry");
        File.WriteAllText("NativeFootprintCache230Checks.json",new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{checks,stableReads=5000,capacity=1024,scope="Exact compiled value cache; no live native component reads or FPS claim."}));
        Console.WriteLine("PASS: "+checks+" cache assertions: 5,000 unchanged reads reuse one outline; endpoint/version/geometry/width/sync/curb invalidation, bounded capacity and scene reset.");
    }
}
