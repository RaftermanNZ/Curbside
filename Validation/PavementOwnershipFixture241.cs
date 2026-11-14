using System;
using System.Collections.Generic;
using MedianGrassReplacement.Systems;

// Managed adapters only: execute the actual ownership code without a Unity
// player. The installed-game adapter shape is checked separately.
namespace UnityEngine
{
    public class Texture { public string name; public Texture(string n="native"){name=n;} }
    public class Texture2D:Texture { public static Texture grayTexture=new Texture("gray"); }
    public class Shader { public string name="BH/AreaDecalShader";public static int PropertyToID(string n)=>n.GetHashCode(); }
    public class Material
    {
        public string name;public Shader shader=new Shader();private Texture texture;
        public Material(string n,Texture t){name=n;texture=t;}
        public bool HasProperty(string n)=>n=="_BaseColorMap";
        public Texture GetTexture(string n)=>texture;
        public void SetTexture(string n,Texture t){texture=t;}
    }
}
namespace Colossal.IO.AssetDatabase
{
    public class TextureAsset
    {
        public bool isValid=true,isBuiltin=true,isObjectLoaded=true;
        private UnityEngine.Texture m_Instance;
        public TextureAsset(UnityEngine.Texture texture){m_Instance=texture;}
    }
    public class SurfaceAsset
    {
        public string name;public bool isValid=true,isBuiltin=true,isDataLoaded=true;
        public Dictionary<string,TextureAsset> textures=new Dictionary<string,TextureAsset>();
    }
    public class ObjectMap:Dictionary<int,object>{public bool ContainsObject(object value)=>ContainsValue(value);}
    public class Resources {public ObjectMap unityObjectsMap=new ObjectMap();}
    public class AssetDatabase
    {
        public static AssetDatabase global=new AssetDatabase();public Resources resources=new Resources();
        public List<object> assets=new List<object>();
        public IEnumerable<T> GetAssets<T>(){foreach(var asset in assets)if(asset is T value)yield return value;}
    }
}
namespace Game.Prefabs
{
    public class Asset {public bool isValid=true,isBuiltin=true;}
    public class AreaPrefab
    {
        public string name;public bool isBuiltin=true;public Asset asset;
        public RenderedArea rendered=new RenderedArea();
        public bool TryGet<T>(out T value) where T:class {value=rendered as T;return value!=null;}
    }
    public class RenderedArea
    {
        public enum Version{Default,OverrideMaterial}
        public Version m_Version;public UnityEngine.Material m_Material;
        public Colossal.IO.AssetDatabase.TextureAsset m_BaseColorMap;
    }
}
namespace Game.Rendering
{
    public class ManagedBatchSystem
    {
        public struct MaterialKey {public Dictionary<int,object> textures;}
        public Dictionary<MaterialKey,UnityEngine.Material> materials=new Dictionary<MaterialKey,UnityEngine.Material>();
    }
    public class AreaBatchSystem
    {
        private class ManagedBatchData {public UnityEngine.Material m_Material;}
        private List<ManagedBatchData> m_ManagedBatchData=new List<ManagedBatchData>();
        public void Add(UnityEngine.Material material){m_ManagedBatchData.Add(new ManagedBatchData{m_Material=material});}
    }
}
class PavementOwnershipFixture241
{
    static int checks;
    static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
    static void Main()
    {
        var texture=new UnityEngine.Texture();var nativeAsset=new Colossal.IO.AssetDatabase.TextureAsset(texture);
        for(int pass=0;pass<20;pass++)foreach(string target in new[]{"Pavement Surface 01","Pavement Surface 02"})
        {
            var original=new UnityEngine.Material("native template",texture);
            var prefab=new Game.Prefabs.AreaPrefab{name=target};
            prefab.rendered.m_Material=original;
            prefab.rendered.m_Version=Game.Prefabs.RenderedArea.Version.OverrideMaterial;
            prefab.rendered.m_BaseColorMap=nativeAsset;
            var material=new UnityEngine.Material(VanillaSurfacePolicy.AreaBatchName(target),texture);
            var own=new VanillaSurfaceOwnership();own.BeginAreas(null);own.AddAreaPrefab(prefab);
            Check(!own.Allows(material,"_BaseColorMap",texture),"Name plus vanilla texture must not grant ownership");
            own.BindAreaMaterial(prefab,material);
            Check(own.Allows(material,"_BaseColorMap",texture),"Actual native target rejected");
            Check(!own.Allows(original,"_BaseColorMap",texture),"Shared template must never be changed");
            var fake=new UnityEngine.Material(material.name,texture);
            Check(!own.Allows(fake,"_BaseColorMap",texture),"Same-name material spoof accepted");
            var foreign=new UnityEngine.Texture("other mod");
            Check(!own.Allows(material,"_BaseColorMap",foreign),"Later texture replacement overwritten");
            Check(!own.Allows(material,"_NormalMap",texture),"Wrong texture channel accepted");
            var renderer=new Game.Rendering.AreaBatchSystem();renderer.Add(fake);renderer.Add(material);
            Check(NativeAreaMaterials.Supported,"Managed adapter shape unsupported");
            Check(ReferenceEquals(NativeAreaMaterials.Read(renderer,1),material),"Native batch material not resolved");
            Check(NativeAreaMaterials.Read(renderer,-1)==null&&NativeAreaMaterials.Read(renderer,2)==null&&NativeAreaMaterials.Read(null,0)==null,"Invalid batch accepted");
            foreach(string other in new[]{"Pavement Surface 03","Pavement Surface 010","Pavement Surface 01 Custom","Custom Pavement Surface 02","pavement surface 02","Grass Surface 01","Concrete Surface 01","Asphalt Surface"})
            {
                var custom=new Game.Prefabs.AreaPrefab{name=other};custom.rendered=prefab.rendered;
                var customMaterial=new UnityEngine.Material(VanillaSurfacePolicy.AreaBatchName(other),texture);
                own.AddAreaPrefab(custom);own.BindAreaMaterial(custom,customMaterial);
                Check(!own.Allows(customMaterial,"_BaseColorMap",texture),"Unrequested surface admitted");
            }
            var collision=new Game.Prefabs.AreaPrefab{name=target,isBuiltin=false,asset=new Game.Prefabs.Asset{isBuiltin=false}};
            collision.rendered=prefab.rendered;own.AddAreaPrefab(collision);own.BindAreaMaterial(collision,fake);
            Check(!own.Allows(fake,"_BaseColorMap",texture),"Modded same-name area admitted");
            Check(!own.Allows(material,"_BaseColorMap",texture),"Ambiguous source not closed");
            // Reproduce cataloging before the native fallback enables overrides.
            own.BeginAreas(original);prefab.rendered.m_Material=null;
            prefab.rendered.m_Version=Game.Prefabs.RenderedArea.Version.Default;
            own.AddAreaPrefab(prefab);own.BindAreaMaterial(prefab,material);
            Check(own.Allows(material,"_BaseColorMap",texture),"Native null-template override path missed");
            nativeAsset.isObjectLoaded=false;
            Check(!own.Allows(material,"_BaseColorMap",texture),"Unloaded texture guessed");
            nativeAsset.isObjectLoaded=true;
            Check(own.Allows(material,"_BaseColorMap",texture),"Deferred native texture load not recognized");
            nativeAsset.isBuiltin=false;
            Check(!own.Allows(material,"_BaseColorMap",texture),"Modded source texture accepted");
            nativeAsset.isBuiltin=true;
            // Legacy native material templates need texture-asset provenance,
            // not merely presence in the mutable Unity object map.
            own.BeginAreas(original);prefab.rendered.m_BaseColorMap=null;prefab.rendered.m_Material=original;
            Colossal.IO.AssetDatabase.AssetDatabase.global.assets.Clear();
            Colossal.IO.AssetDatabase.AssetDatabase.global.resources.unityObjectsMap[0]=original;
            Colossal.IO.AssetDatabase.AssetDatabase.global.resources.unityObjectsMap[1]=texture;
            own.AddAreaPrefab(prefab);own.BindAreaMaterial(prefab,material);
            Check(!own.Allows(material,"_BaseColorMap",texture),"Unity map membership falsely proves native texture");
            Colossal.IO.AssetDatabase.AssetDatabase.global.assets.Add(nativeAsset);
            own.BeginAreas(original);own.AddAreaPrefab(prefab);own.BindAreaMaterial(prefab,material);
            Check(own.Allows(material,"_BaseColorMap",texture),"Loaded built-in legacy texture rejected");
            original.SetTexture("_BaseColorMap",foreign);material.SetTexture("_BaseColorMap",foreign);
            Colossal.IO.AssetDatabase.AssetDatabase.global.assets.Add(new Colossal.IO.AssetDatabase.TextureAsset(foreign){isBuiltin=false});
            own.BeginAreas(original);own.AddAreaPrefab(prefab);own.BindAreaMaterial(prefab,material);
            Check(!own.Allows(material,"_BaseColorMap",foreign),"Foreign edit to template mistaken for vanilla original");
            own.Clear();Check(!own.Allows(material,"_BaseColorMap",texture),"Stale load binding retained");
        }
        Console.WriteLine("PASS: "+checks+" production ownership/managed-adapter checks. Native rendering and visuals still require in-game testing.");
    }
}
