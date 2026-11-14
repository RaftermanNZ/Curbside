using System;
using System.Reflection;
class SaveScopeIsolationChecks242
{
    static int count;
    static void Check(bool value,string message){count++;if(!value)throw new Exception(message);}
    static void Main()
    {
        var a=Assembly.LoadFrom("MedianGrassReplacement.dll");const BindingFlags f=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        var profile=a.GetType("MedianGrassReplacement.Systems.MarkingSerializationProfile",true);
        Check(!(bool)profile.GetField("Supported",f).GetValue(null),"Current native retention gate was enabled without native validation");
        var match=profile.GetMethod("Matches",f);
        Check((bool)match.Invoke(null,new object[]{new Guid("e15551e9-3dea-4855-8dde-1da3cea7d7ef"),new Guid("3d6b23ae-b281-44a6-a5f4-cbd5fd3d75e6"),new Guid("b5ff7e4e-51b0-4c1f-a177-2afdfc22906b")}),"Previously reviewed profile changed");
        Check(!(bool)match.Invoke(null,new object[]{Guid.Empty,Guid.Empty,Guid.Empty}),"Unknown modules admitted");
        var policy=a.GetType("MedianGrassReplacement.Systems.CrosswalkVisualPolicy",true).GetMethod("WithoutPaint",f);
        var before=new Game.Prefabs.NetLaneGeometryData();var type=before.GetType();object boxed=before;
        type.GetField("m_GameLayers").SetValue(boxed,Enum.ToObject(type.GetField("m_GameLayers").FieldType,7));
        type.GetField("m_EditorLayers").SetValue(boxed,Enum.ToObject(type.GetField("m_EditorLayers").FieldType,7));
        var after=policy.Invoke(null,new[]{boxed});
        Check(Convert.ToUInt64(type.GetField("m_GameLayers").GetValue(after))==0&&Convert.ToUInt64(type.GetField("m_EditorLayers").GetValue(after))==0,"Crosswalk render suppression changed");
        foreach(var field in type.GetFields(BindingFlags.Public|BindingFlags.Instance))
            if(field.Name!="m_GameLayers"&&field.Name!="m_EditorLayers")Check(Equals(field.GetValue(boxed),field.GetValue(after)),"Non-render crosswalk geometry/routing changed");
        Check(a.GetName().Version.ToString()=="0.5.1.0","Save assembly identity changed");
        Console.WriteLine("PASS: "+count+" compiled save-scope isolation checks; native round trips are separate.");
    }
}
