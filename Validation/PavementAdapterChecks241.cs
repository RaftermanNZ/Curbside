using System;
using System.Reflection;
class PavementAdapterChecks241
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Main()
    {
        var a=Assembly.LoadFrom("MedianGrassReplacement.dll");const BindingFlags f=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        var adapter=a.GetType("MedianGrassReplacement.Systems.NativeAreaMaterials",true);
        Check((bool)adapter.GetProperty("Supported",f).GetValue(null),"Installed game managed material layout is unsupported");
        Check(typeof(Game.Prefabs.RenderedAreaData).GetField("m_BatchIndex").FieldType==typeof(int),"Native area batch index contract changed");
        Check(typeof(Game.Prefabs.AreasConfigurationPrefab).GetField("m_RenderedAreaMaterial").FieldType==typeof(UnityEngine.Material),"Native fallback template contract changed");
        var kind=a.GetType("MedianGrassReplacement.Systems.GlobalSurfacePolicy",true).GetMethod("AssetKind",f);
        foreach(string shader in new[]{"BH/AreaDecalShader","Shader Graphs/AreaDecalShader"})
        {
            Check((int)kind.Invoke(null,new object[]{"Area batch (Pavement Surface 01)",shader})==4,"Pavement01 must use road");
            Check((int)kind.Invoke(null,new object[]{"Area batch (Pavement Surface 02)",shader})==2,"Pavement02 must use sidewalk");
        }
        foreach(string name in new[]{"Pavement Surface 01","Area batch (Pavement Surface 03)","Area batch (Custom Pavement Surface 02)","Area batch (Pavement Surface 01 Custom)","Area batch (Grass Surface 01)","Area batch (Pavement Surface 010)"})
            Check((int)kind.Invoke(null,new object[]{name,"BH/AreaDecalShader"})==0,"Untargeted surface admitted");
        foreach(string shader in new[]{null,"","BuildingShader","Custom/AreaDecalShader","BH/AreaDecalShader Custom","BH/NetCompositionMeshLitShader"})
            Check((int)kind.Invoke(null,new object[]{"Area batch (Pavement Surface 02)",shader})==0,"Untargeted shader admitted");
        Check(a.GetName().Version.ToString()=="0.5.1.0","Save assembly identity changed");
        Console.WriteLine("PASS: "+checks+" installed-SDK adapter/routing checks; actual shader rendering remains untested.");
    }
}
