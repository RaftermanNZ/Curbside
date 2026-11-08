using System;
using System.Reflection;
using Colossal.Mathematics;
using Unity.Mathematics;

class SuppliedPreviewGeometryChecks
{
    static MethodInfo overlaps,same;static int checks;
    static Bezier4x3 Line(float3 a,float3 b)=>new Bezier4x3(a,math.lerp(a,b,1f/3),math.lerp(a,b,2f/3),b);
    static Bezier4x3 Move(Bezier4x3 c,float3 d)=>new Bezier4x3(c.a+d,c.b+d,c.c+d,c.d+d);
    static void Check(MethodInfo method,Bezier4x3 a,Bezier4x3 b,bool expected,string note)
    {if((bool)method.Invoke(null,new object[]{a,b})!=expected)throw new Exception(note);checks++;}
    static void Main()
    {
        var type=Assembly.LoadFrom("MedianGrassReplacement.dll").GetType("MedianGrassReplacement.Systems.MarkingPreviewPolicy",true);
        overlaps=type.GetMethod("Overlaps",BindingFlags.NonPublic|BindingFlags.Static);
        same=type.GetMethod("SameCurve",BindingFlags.NonPublic|BindingFlags.Static);
        for(int angle=0;angle<360;angle+=15)
        {
            float r=math.radians(angle);var forward=new float3(math.cos(r),0,math.sin(r));var side=new float3(-forward.z,0,forward.x);
            foreach(var origin in new[]{float3.zero,new float3(1000,25,-2000)})
            {
                var road=Line(origin,origin+forward*80);var dash=Line(origin+forward*30,origin+forward*34);
                Check(overlaps,road,dash,true,"Solid/dash preview overlap lost");
                Check(overlaps,dash,road,true,"Asymmetric solid/dash matching");
                Check(overlaps,road,new Bezier4x3(dash.d,dash.c,dash.b,dash.a),true,"Reversed marking overlap lost");
                Check(overlaps,road,Move(road,side*.4f),true,"Nearby aligned preview failed");
                Check(overlaps,road,Move(road,side*3),false,"Adjacent lane incorrectly hidden");
                Check(overlaps,road,Move(road,new float3(0,4,0)),false,"Separate deck incorrectly hidden");
                Check(overlaps,road,Line(origin+forward*40-side*15,origin+forward*40+side*15),false,"Crossing road incorrectly hidden");
                Check(same,road,road,true,"Identical curve invalidated cache");
                Check(same,road,Move(road,new float3(.01f,0,0)),false,"Moved cursor guide reused stale geometry");
                Check(same,road,new Bezier4x3(road.a,road.b+new float3(0,.01f,0),road.c,road.d),false,"Changed tangent reused stale geometry");
            }
        }
        Console.WriteLine("PASS: "+checks+" supplied marking-preview geometry cases: solid/dash, reversal, lane/deck separation, crossing rejection and cache invalidation. ECS visibility/runtime timings remain unverified.");
    }
}
