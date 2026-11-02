using System;
using System.Reflection;
using Colossal.Mathematics;
using Unity.Mathematics;
class PreviewContinuityChecks
{
    static MethodInfo same;static int checks;
    static Bezier4x3 Path(float3[] p)=>new Bezier4x3(p[0],p[1],p[2],p[3]);
    static void Check(Bezier4x3 a,Bezier4x3 b,bool expected,string note)
    {if((bool)same.Invoke(null,new object[]{a,b})!=expected)throw new Exception(note);checks++;}
    static void Main()
    {
        var assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");
        same=assembly.GetType("MedianGrassReplacement.Systems.PaintPreviewContinuityPolicy",true).GetMethod("SamePath",BindingFlags.Static|BindingFlags.NonPublic);
        var points=new[]{new float3(0,0,0),new float3(2,0,1),new float3(4,0,2),new float3(6,0,3)};
        var original=Path(points);Check(original,original,true,"Unchanged preview lost continuity");
        for(int p=0;p<4;p++)for(int axis=0;axis<3;axis++)
        {
            foreach(float delta in new[]{-.005f,.005f})
            {var moved=(float3[])points.Clone();moved[p][axis]+=delta;Check(original,Path(moved),false,"Changed endpoint/tangent/height retained old paint");}
            var submillimeter=(float3[])points.Clone();submillimeter[p][axis]+=.00025f;
            Check(original,Path(submillimeter),true,"Submillimeter native rounding discarded identical path");
            foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            {var broken=(float3[])points.Clone();broken[p][axis]=invalid;Check(original,Path(broken),false,"Invalid preview geometry was accepted");Check(Path(broken),original,false,"Invalid original geometry was accepted");}
        }
        var reversed=(float3[])points.Clone();Array.Reverse(reversed);Check(original,Path(reversed),false,"Reversed path treated as unchanged");
        Console.WriteLine("PASS: "+checks+" preview geometry continuity cases (all controls, height, tangent, reversal, rounding and nonfinite input). Runtime rendering/FPS require in-game validation.");
    }
}
