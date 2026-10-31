using System;
using System.Reflection;
using System.Runtime.Serialization;
using Colossal.Mathematics;
using Unity.Mathematics;
class NativeJunctionChecks
{
    static void Main()
    {
        var a=Assembly.LoadFrom("MedianGrassReplacement.dll");var t=a.GetType("MedianGrassReplacement.Setting",true);
        var flags=BindingFlags.NonPublic|BindingFlags.Static;var target=a.GetType("MedianGrassReplacement.Systems.NativeJunctionColorPolicy",true).GetMethod("Target",flags);
        int cases=0;
        var at=a.GetType("MedianGrassReplacement.Systems.HighwayPaintAnchor",true);
        var anchors=(System.Collections.IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(at));
        void Add(float3 position,bool yellow){var entry=Activator.CreateInstance(at);at.GetField("Native",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(entry,position);at.GetField("Paint",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(entry,position);at.GetField("Yellow",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(entry,yellow);anchors.Add(entry);}
        var yellowPath=a.GetType("MedianGrassReplacement.Systems.HighwayPaintAnchorPolicy",true).GetMethod("Yellow",flags);
        // Actual Test125 snapshot: one-ended gore and native median continuation.
        var tip=new float3(-561.7762f,124.811f,-3160.352f);var yellowEnd=new float3(-551.9919f,124.8046f,-3163.883f);
        var whiteEnd=new float3(-551.0298f,124.8046f,-3160f);Add(yellowEnd,true);Add(whiteEnd,false);
        foreach(bool reverse in new[]{false,true})foreach(bool yellow in new[]{false,true}){
            var start=tip;var end=yellow?yellowEnd:whiteEnd;var path=new Bezier4x3(start,math.lerp(start,end,.33f),math.lerp(start,end,.66f),end);
            if(reverse)path=new Bezier4x3(path.d,path.c,path.b,path.a);
            if((bool)yellowPath.Invoke(null,new object[]{path,anchors})!=yellow)throw new Exception("Captured one-ended gore lost its boundary color");cases++;
        }
        var returnPath=new Bezier4x3(yellowEnd,tip,tip,yellowEnd);
        if(!(bool)yellowPath.Invoke(null,new object[]{returnPath,anchors}))throw new Exception("Same-road median return was rejected");cases++;
        foreach(bool opposing in new[]{false,true})foreach(bool edge in new[]{false,true})foreach(bool eu in new[]{false,true})foreach(bool theme in new[]{false,true})
        {
            byte color=(byte)target.Invoke(null,new object[]{opposing,edge,eu,theme});
            if(color==2&&(!opposing||eu))throw new Exception("Non-opposing marking was turned yellow");
            if(!opposing&&!edge&&color!=0)throw new Exception("Unclassified lane/merge marking was recolored");
            if(opposing&&color!=(eu?1:2))throw new Exception("Eligible opposing-boundary theme was lost");
            if(!opposing&&edge&&eu&&theme&&color!=1)throw new Exception("Eligible European edge color was lost");
            cases++;
        }
        var settings=FormatterServices.GetUninitializedObject(t);
        foreach(var field in t.GetFields(BindingFlags.NonPublic|BindingFlags.Instance))if(field.FieldType.Name=="DecalNumberDraft")field.SetValue(settings,Activator.CreateInstance(field.FieldType,true));
        var reset=t.GetMethod("SetDefaultValues",BindingFlags.NonPublic|BindingFlags.Instance);
        reset.Invoke(settings,new object[]{"C:/Curbside-Settings-Fixture"});
        var opt=t.GetProperty("PreserveVanillaJunctionLines");
        if((bool)opt.GetValue(settings))throw new Exception("New option must not change existing defaults");
        foreach(string blocker in new[]{"DisableAutomaticMarkings","DisableAutomaticHighwayMarkings","DisableAutomaticLineAdjustments"})
        {
            reset.Invoke(settings,new object[]{"C:/Curbside-Settings-Fixture"});t.GetProperty(blocker).SetValue(settings,true);opt.SetValue(settings,true);
            if(!(bool)t.GetProperty(blocker).GetValue(settings))throw new Exception("Opt-in silently changed a saved opt-out");
            string status=(string)t.GetProperty("JunctionMarkingStatus").GetValue(settings);
            if(status.IndexOf("blocked",StringComparison.OrdinalIgnoreCase)<0)throw new Exception("Conflicting setting is not explained next to the checkbox");
            opt.SetValue(settings,false);if(!(bool)t.GetProperty(blocker).GetValue(settings))throw new Exception("Opt-out silently changed another control");cases++;
        }
        reset.Invoke(settings,new object[]{"C:/Curbside-Settings-Fixture"});if((bool)opt.GetValue(settings))throw new Exception("Reset must restore opt-in off");
        Console.WriteLine("PASS: "+cases+" native-color/blocked-setting cases; opt-in off and unrelated opt-outs retained. Runtime native path/width/dash, regeneration and save tests pending.");
    }
}
