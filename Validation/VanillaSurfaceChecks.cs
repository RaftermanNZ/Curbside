using System;
using System.Reflection;
using System.Runtime.Serialization;
class VanillaSurfaceChecks
{
    static readonly Assembly A=Assembly.LoadFrom("MedianGrassReplacement.dll");
    static int n;
    static void Check(bool ok,string message){n++;if(!ok)throw new Exception(message);}
    static void Main()
    {
        var policy=A.GetType("MedianGrassReplacement.Systems.VanillaSurfacePolicy",true).GetMethod("Allows",BindingFlags.Static|BindingFlags.NonPublic);
        Check((bool)policy.Invoke(null,new object[]{true,false,true,true}),"Verified unchanged vanilla source rejected");
        foreach(var example in new[]{new[]{false,false,true,true},new[]{true,true,true,true},new[]{true,false,false,true},new[]{true,false,true,false},new[]{false,false,false,false}})
            Check(!(bool)policy.Invoke(null,new object[]{example[0],example[1],example[2],example[3]}),"Custom, ambiguous, unknown or replaced texture accepted");
        var type=A.GetType("MedianGrassReplacement.Setting",true);var setting=FormatterServices.GetUninitializedObject(type);
        foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.NonPublic))if(field.FieldType.Name=="DecalNumberDraft")field.SetValue(setting,Activator.CreateInstance(field.FieldType,true));
        var defaults=type.GetMethod("SetDefaultValues",BindingFlags.Instance|BindingFlags.NonPublic);
        defaults.Invoke(setting,new object[]{System.IO.Path.GetFullPath("C:/Curbside-Surface-Fixture")});
        var enabled=type.GetProperty("SurfaceTexturesOnAssets");
        Check(!(bool)enabled.GetValue(setting),"New/reset defaults enabled asset replacement");
        enabled.SetValue(setting,true);type.GetProperty("SurfaceDefaultVersion").SetValue(setting,1);type.GetProperty("GlobalTextureSelectionVersion").SetValue(setting,1);
        type.GetMethod("MigrateSurfaceDefaults",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(setting,null);
        Check((bool)enabled.GetValue(setting),"Existing explicit opt-in was reset by migration");
        defaults.Invoke(setting,new object[]{System.IO.Path.GetFullPath("C:/Curbside-Surface-Fixture")});
        Check(!(bool)enabled.GetValue(setting),"Reset did not return to safe default");
        var asset=typeof(Colossal.IO.AssetDatabase.TextureAsset);
        var loaded=asset.GetField("m_Instance",BindingFlags.Instance|BindingFlags.NonPublic);
        Check(loaded!=null&&!loaded.IsStatic&&loaded.FieldType==typeof(UnityEngine.Texture),"Installed game's read-only loaded-texture adapter differs; fail closed");
        Check(typeof(Game.Rendering.ManagedBatchSystem).GetProperty("materials")!=null,"Native batch provenance API missing");
        var batchName=A.GetType("MedianGrassReplacement.Systems.VanillaSurfacePolicy",true).GetMethod("AreaBatchName",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(string name in new[]{"Asphalt Surface","Concrete Pavement","ExtraAssetsImporter Pavement =15= Mike's Asphalt__ OVER Surface","Surface_Placeholder"})
            Check((string)batchName.Invoke(null,new object[]{name})=="Area batch ("+name+")","Area renderer's prefab-name wrapper mismatch");
        Check(typeof(Game.Prefabs.RenderedArea).GetField("m_BaseColorMap").FieldType==typeof(Colossal.IO.AssetDatabase.AssetReference<Colossal.IO.AssetDatabase.TextureAsset>),"Rendered-area texture metadata API differs");
        var target=A.GetType("MedianGrassReplacement.Systems.VanillaSurfacePolicy",true).GetMethod("TargetArea",BindingFlags.Static|BindingFlags.NonPublic);
        var assetKind=A.GetType("MedianGrassReplacement.Systems.GlobalSurfacePolicy",true).GetMethod("AssetKind",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(var pair in new[]{new object[]{"Pavement Surface 01",4},new object[]{"Pavement Surface 02",2}})
        {
            var name=(string)pair[0];
            Check((bool)target.Invoke(null,new object[]{name}),"Requested pavement definition excluded");
            Check((int)assetKind.Invoke(null,new object[]{"Area batch ("+name+")","Shader Graphs/AreaDecalShader"})==(int)pair[1],"Pavement road/sidewalk routing incorrect");
            Check((int)assetKind.Invoke(null,new object[]{name,"Shader Graphs/AreaDecalShader"})==0,"Shared original template admitted");
            Check((int)assetKind.Invoke(null,new object[]{"Area batch ("+name+")","Unrelated/Shader"})==0,"Non-area material admitted");
        }
        foreach(string name in new[]{null,"","Pavement Surface 03","Pavement Surface 010","Pavement Surface 01 Custom","Custom Pavement Surface 02","pavement surface 01","Pavement Surface 01 ","Asphalt Surface","Parking Lot","Sidewalk Surface","Concrete Surface 01","Concrete Surface 02","Grass Surface 01","Sand Surface 01","Tiles Surface 01","Forestry Lot"})
        {
            Check(!(bool)target.Invoke(null,new object[]{name}),"Unrequested surface entered ownership catalog");
            Check((int)assetKind.Invoke(null,new object[]{"Area batch ("+name+")","Shader Graphs/AreaDecalShader"})==0,"Broad surface matching returned");
        }
        Console.WriteLine("PASS: "+n+" vanilla-surface cases: protected custom/ambiguous/third-party replacements; off defaults, explicit opt-in persistence and installed-game provenance API. In-game surface appearance remains pending.");
    }
}
