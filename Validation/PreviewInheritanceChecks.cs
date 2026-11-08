using System;
using System.Reflection;
using Colossal.Mathematics;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

class PreviewInheritanceChecks
{
    static int checks;
    static MethodInfo map, delta, place, active;
    static Bezier4x3 Line(float3 a, float3 b)=>new Bezier4x3(a, math.lerp(a,b,1f/3), math.lerp(a,b,2f/3), b);
    static Bezier4x3 Offset(Bezier4x3 c,float3 d)=>new Bezier4x3(c.a+d,c.b+d,c.c+d,c.d+d);
    static bool Near(float3 a,float3 b)=>math.distance(a,b)<.01f;
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
    static Bezier4x3 Map(Bezier4x3 stroke,Bezier4x3 original,Bezier4x3 preview)
    {object[] args={stroke,original,preview,default(Bezier4x3)};Assert((bool)map.Invoke(null,args),"Valid mapped preview rejected");return (Bezier4x3)args[3];}
    static byte Delta(Entity a,Transform p,Entity b,Transform q,bool pose=true)=>(byte)delta.Invoke(null,new object[]{a,p,b,q,pose});
    static void Main()
    {
        var a=Assembly.LoadFrom("MedianGrassReplacement.dll");
        var policy=a.GetType("MedianGrassReplacement.Systems.PreviewVisualPolicy",true);
        active=policy.GetMethod("Active",BindingFlags.Static|BindingFlags.NonPublic);
        delta=policy.GetMethod("Delta",BindingFlags.Static|BindingFlags.NonPublic);
        place=policy.GetMethod("Place",BindingFlags.Static|BindingFlags.NonPublic);
        map=a.GetType("MedianGrassReplacement.Systems.PreviewPaintGeometry",true).GetMethod("Map",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(TempFlags flag in Enum.GetValues(typeof(TempFlags)))
        {
            bool expected=flag!=TempFlags.Delete&&flag!=TempFlags.Cancel&&flag!=TempFlags.Hidden;
            Assert((bool)active.Invoke(null,new object[]{flag})==expected,"Preview flags skipped a legitimate tool mode or retained deletion/cancellation");
        }
        var p=new Transform {m_Position=new float3(10,3,20),m_Rotation=quaternion.identity};
        var q=p;q.m_Position.x+=2;
        var source=new Entity{Index=10,Version=1};var target=new Entity{Index=11,Version=1};
        Assert(Delta(source,p,target,q)==3,"Initial custom preview must update prefab and pose");
        int writes=0;
        for(int i=0;i<10000;i++)if(Delta(target,q,target,q)!=0)writes++;
        Assert(writes==0,"Stationary custom light repeatedly refreshes");
        Assert(Delta(target,q,target,p)==2,"A changed road pose must update without replacing the light prefab");
        Assert(Delta(target,q,source,q)==1,"A changed light style must update the prefab only");
        Assert(Delta(target,q,target,p,false)==0,"Paint-only request unexpectedly writes a transform");
        Assert(Delta(new Entity{Index=11,Version=2},q,target,q)==1,"Entity version reuse bypassed refresh");
        for(int angle=0;angle<360;angle+=15)
        {
            var r=quaternion.RotateY(math.radians(angle));
            float3 f=math.mul(r,new float3(1,0,0));float3 n=math.mul(r,new float3(0,0,1));
            foreach(var offset in new[]{float3.zero,new float3(1000,40,-2000)})
            {
                var original=Line(offset,offset+f*20);
                var stroke=Offset(original,n*.8f+new float3(0,.02f,0));
                var unchanged=Map(stroke,original,original);
                Assert(unchanged.a.Equals(stroke.a)&&unchanged.b.Equals(stroke.b)&&unchanged.c.Equals(stroke.c)&&unchanged.d.Equals(stroke.d),"Hover preview changed line output");
                var moved=Offset(original,new float3(7,2,-9));
                var result=Map(stroke,original,moved);
                Assert(Near(result.a,stroke.a+new float3(7,2,-9))&&Near(result.d,stroke.d+new float3(7,2,-9)),"Crosswalk failed to follow moved native geometry");
                var longer=Line(offset,offset+f*35);
                result=Map(stroke,original,longer);
                Assert(Near(result.a,offset+n*.8f+new float3(0,.02f,0))&&Near(result.d,offset+f*35+n*.8f+new float3(0,.02f,0)),"Connected-road preview lost offsets or proposed width");
                var native=new Transform{m_Position=offset,m_Rotation=r};
                var applied=native;applied.m_Position+=n*5;applied.m_Rotation=math.mul(r,quaternion.RotateY(.4f));
                var proposed=native;proposed.m_Position+=f*8;
                object[] args={native,applied,proposed,default(Transform)};
                Assert((bool)place.Invoke(null,args),"Valid signal placement rejected");
                Assert(Near(((Transform)args[3]).m_Position,applied.m_Position),"Signal moved away from committed position");
                proposed.m_Rotation=math.mul(r,quaternion.RotateY(math.PI*.5f));
                args=new object[]{native,applied,proposed,default(Transform)};
                Assert((bool)place.Invoke(null,args),"Rotated road preview rejected");
                Assert(Near(((Transform)args[3]).m_Position,applied.m_Position),"Cursor rotation moved the committed signal");
                // Delta, rather than reapplying this offset to an already-custom pose,
                // is the no-refresh path tested above.
            }
        }
        var invalid=new Bezier4x3(new float3(float.NaN,0,0),float3.zero,float3.zero,float3.zero);
        var same=a.GetType("MedianGrassReplacement.Systems.PreviewPaintGeometry",true).GetMethod("SamePath",BindingFlags.Static|BindingFlags.NonPublic);
        var baseline=Line(float3.zero,new float3(20,0,0));
        Assert((bool)same.Invoke(null,new object[]{baseline,Offset(baseline,new float3(.00025f,0,0))}),"Native submillimeter rounding caused preview-copy churn");
        Assert(!(bool)same.Invoke(null,new object[]{baseline,Offset(baseline,new float3(.005f,0,0))}),"Proposed geometry change was mistaken for an unchanged hover");
        object[] broken={invalid,Line(float3.zero,new float3(20,0,0)),Line(float3.zero,new float3(25,0,0)),default(Bezier4x3)};
        Assert(!(bool)map.Invoke(null,broken),"Invalid geometry produced a preview actor");
        object[] degenerate={Line(float3.zero,new float3(20,0,0)),default(Bezier4x3),default(Bezier4x3),default(Bezier4x3)};
        Assert(!(bool)map.Invoke(null,degenerate),"Degenerate native path bypassed the unchanged-geometry fast path");
        Console.WriteLine("PASS: "+checks+" preview inheritance/refresh decisions, including 10,000 unchanged-light evaluations with zero requested writes. Native frame time and tool visuals remain unverified.");
    }
}
