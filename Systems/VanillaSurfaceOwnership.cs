using System;
using System.Collections.Generic;
using System.Reflection;
using Colossal.IO.AssetDatabase;
using Game.Rendering;
using Game.Prefabs;
using UnityEngine;

namespace MedianGrassReplacement.Systems
{
    internal static class VanillaSurfacePolicy
    {
        internal static string AreaBatchName(string prefabName)=>"Area batch ("+prefabName+")";
        internal static bool TargetArea(string prefabName)=>prefabName=="Pavement Surface 01"||prefabName=="Pavement Surface 02";
        internal static bool Allows(bool builtinSource,bool ambiguousSource,bool builtinTexture,bool sameTexture)
            =>builtinSource&&!ambiguousSource&&builtinTexture&&sameTexture;
    }

    // Managed provenance only. Never load/unload an asset to inspect it, mutate
    // batch keys, or touch native batch/culling containers. Unknowns stay intact.
    internal sealed class VanillaSurfaceOwnership
    {
        private sealed class Source{internal bool Builtin,Custom;internal SurfaceAsset Surface;}
        private sealed class AreaSource{internal AreaPrefab Prefab;internal TextureAsset TextureAsset;internal Material OriginalMaterial;internal Texture OriginalTexture;internal bool HasTextureReference,Ambiguous;}
        private readonly EmbeddedPavementTextures embedded=new EmbeddedPavementTextures(GlobalSurfaceMaterialSystem.NotifyRenderActivity);
        private readonly Dictionary<string,AreaSource> areas=new Dictionary<string,AreaSource>(StringComparer.Ordinal);
        private readonly Dictionary<Material,AreaSource> areaMaterials=new Dictionary<Material,AreaSource>();
        private Material defaultAreaMaterial;
        private Dictionary<Texture,TextureAsset> loadedBuiltinTextures;
        private readonly Dictionary<string,Source> sources=new Dictionary<string,Source>(StringComparer.Ordinal);
        private readonly Dictionary<Material,ManagedBatchSystem.MaterialKey> batches=new Dictionary<Material,ManagedBatchSystem.MaterialKey>();
        // The shipped API exposes isObjectLoaded but no non-loading getter.
        // Read only the managed Texture reference, with a fail-closed type guard.
        private static readonly FieldInfo loadedTexture=typeof(TextureAsset).GetField("m_Instance",BindingFlags.Instance|BindingFlags.NonPublic);
        internal void RefreshCatalog()
        {
            sources.Clear();
            areas.Clear();
            areaMaterials.Clear();
            var database=AssetDatabase.global;
            if(database==null||database.resources==null)return;
            foreach(var asset in database.GetAssets<SurfaceAsset>())
            {
                if(asset==null||!asset.isValid||string.IsNullOrEmpty(asset.name))continue;
                Add(asset.name,asset.isBuiltin,asset);
            }
            foreach(var item in database.resources.unityObjectsMap)
                if(item.Value is Material material&&material!=null)Add(material.name,true,null);
        }
        private void Add(string name,bool builtin,SurfaceAsset asset)
        {
            if(!sources.TryGetValue(name,out var value))sources[name]=value=new Source();
            if(builtin){value.Builtin=true;if(asset!=null)value.Surface=asset;}else value.Custom=true;
        }
        internal void AddAreaPrefab(AreaPrefab prefab)
        {
            // Catalog older replacement targets for recovery; AllowsArea still
            // limits NEW replacements to the two approved vanilla pavements.
            if(prefab==null||string.IsNullOrEmpty(prefab.name)||!prefab.TryGet<RenderedArea>(out var rendered)||rendered==null)return;
            // Match native AreaBatchSystem: a null template uses the global
            // area template and enables the prefab's texture overrides.
            var original=rendered.m_Material!=null?rendered.m_Material:defaultAreaMaterial;
            bool usesTexture=(rendered.m_Material==null||rendered.m_Version==RenderedArea.Version.OverrideMaterial)&&rendered.m_BaseColorMap!=null;
            var originalTexture=original!=null&&original.HasProperty("_BaseColorMap")?original.GetTexture("_BaseColorMap"):null;
            // Embedded native targets have exact prefab/template identities and
            // cached content verification. Do not enumerate the full texture
            // asset database again for each render-material discovery batch.
            bool embeddedNative=!usesTexture&&EmbeddedPavementTextures.NativeContext(prefab,original);
            var source=new AreaSource{Prefab=prefab,TextureAsset=usesTexture?(TextureAsset)rendered.m_BaseColorMap:embeddedNative?null:FindBuiltinTexture(originalTexture),HasTextureReference=usesTexture,
                OriginalMaterial=original,OriginalTexture=originalTexture};
            AddAreaName(VanillaSurfacePolicy.AreaBatchName(prefab.name),source);
        }
        internal void BeginAreas(Material fallback)
        {areas.Clear();areaMaterials.Clear();defaultAreaMaterial=fallback;loadedBuiltinTextures=null;}
        private TextureAsset FindBuiltinTexture(Texture texture)
        {
            if(texture==null||loadedTexture==null||loadedTexture.FieldType!=typeof(Texture))return null;
            if(loadedBuiltinTextures==null)
            {
                loadedBuiltinTextures=new Dictionary<Texture,TextureAsset>();
                foreach(var asset in AssetDatabase.global.GetAssets<TextureAsset>())
                    if(asset!=null&&asset.isValid&&asset.isBuiltin&&asset.isObjectLoaded
                        &&loadedTexture.GetValue(asset) is Texture instance&&instance!=null)
                        loadedBuiltinTextures[instance]=asset;
            }
            return loadedBuiltinTextures.TryGetValue(texture,out var result)?result:null;
        }
        internal void BindAreaMaterial(AreaPrefab prefab,Material material)
        {
            if(prefab==null||material==null||material.shader==null
                ||!areas.TryGetValue(VanillaSurfacePolicy.AreaBatchName(prefab.name),out var source)
                ||source.Prefab!=prefab
                ||!NativeAreaMaterialPolicy.Admits(prefab.name,material.name,material.shader.name,
                    AreaBuiltin(prefab),source.Ambiguous,true,material==source.OriginalMaterial))return;
            if(areaMaterials.TryGetValue(material,out var previous)&&previous.Prefab!=prefab)
            {previous.Ambiguous=true;source.Ambiguous=true;return;}
            areaMaterials[material]=source;
        }
        internal IEnumerable<Material> AreaMaterials=>areaMaterials.Keys;
        internal bool IsTargetAreaMaterial(Material material)=>material!=null
            &&areaMaterials.TryGetValue(material,out var source)&&source.Prefab!=null
            &&NativeAreaMaterialPolicy.Admits(source.Prefab.name,material.name,material.shader?.name,
                AreaBuiltin(source.Prefab),source.Ambiguous,true,material==source.OriginalMaterial);
        private void AddAreaName(string name,AreaSource source)
        {
            if(areas.TryGetValue(name,out var previous))
            {
                // A name collision must never grant custom areas permission.
                if(!AreaBuiltin(previous.Prefab)||!AreaBuiltin(source.Prefab)
                    ||previous.OriginalMaterial!=source.OriginalMaterial
                    ||previous.HasTextureReference!=source.HasTextureReference||previous.OriginalTexture!=source.OriginalTexture
                    ||previous.TextureAsset!=source.TextureAsset)previous.Ambiguous=true;
            }
            else areas[name]=source;
        }
        private static bool AreaBuiltin(AreaPrefab prefab)=>prefab!=null
            &&(prefab.asset!=null?prefab.asset.isValid&&prefab.asset.isBuiltin:prefab.isBuiltin);
        private bool AllowsArea(AreaSource source,Material material,string property,Texture current)
        {
            if(property!="_BaseColorMap"||!NativeAreaMaterialPolicy.Admits(source.Prefab.name,
                material.name,material.shader?.name,AreaBuiltin(source.Prefab),source.Ambiguous,
                areaMaterials.TryGetValue(material,out var bound)&&ReferenceEquals(bound,source),
                material==source.OriginalMaterial))return false;
            if(source.HasTextureReference)return source.TextureAsset!=null&&SameLoadedNativeTexture(source.TextureAsset,current);
            var original=source.OriginalMaterial;var expected=source.OriginalTexture;
            if(original==null||expected==null)return false;
            // A registered Unity object alone is not proof of a vanilla texture.
            // Embedded originals also need exact native context and content.
            return current==expected&&(source.TextureAsset!=null&&SameLoadedNativeTexture(source.TextureAsset,current)
                ||embedded.Allows(source.Prefab,original,current));
        }
        private static bool SameLoadedNativeTexture(TextureAsset asset,Texture current)
        {
            if(!asset.isValid||!asset.isBuiltin||!asset.isObjectLoaded||loadedTexture==null||loadedTexture.FieldType!=typeof(Texture))return false;
            var expected=loadedTexture.GetValue(asset) as Texture;
            return expected!=null&&current==expected;
        }
        internal bool TryGetAreaOriginal(Material material,out Texture texture)
        {
            texture=null;
            if(material==null||material.shader==null||material.shader.name.IndexOf("AreaDecalShader",StringComparison.Ordinal)<0
                ||!areas.TryGetValue(material.name,out var source)||source.Ambiguous||source.Prefab==null)return false;
            // Custom areas recover their own original, never a guessed vanilla
            // substitute. No asset loading, template mutation or native access.
            if(source.HasTextureReference)
            {
                var asset=source.TextureAsset;
                if(asset==null||!asset.isValid||!asset.isObjectLoaded||loadedTexture==null||loadedTexture.FieldType!=typeof(Texture))return false;
                texture=loadedTexture.GetValue(asset) as Texture;
            }
            else texture=source.OriginalTexture;
            return texture!=null&&!SurfaceRestorePolicy.IsCurbsideTexture(texture.name);
        }
        internal void Begin(ManagedBatchSystem system)
        {
            batches.Clear();if(system==null)return;
            foreach(var entry in system.materials)if(entry.Value!=null)batches[entry.Value]=entry.Key;
        }
        internal bool HasBatchMaterials=>batches.Count>0;
        internal IEnumerable<Material> BatchMaterials=>batches.Keys;
        internal void End()=>batches.Clear();
        internal void Clear(){embedded.Clear();End();sources.Clear();areas.Clear();areaMaterials.Clear();defaultAreaMaterial=null;loadedBuiltinTextures=null;}
        internal bool Allows(Material material,string property,Texture current)
        {
            if(material==null||current==null)return false;
            // Only rendered area-batch copies are edited. Never modify their
            // shared prefab material templates, which other packs could reuse.
            if(material.shader!=null&&material.shader.name.IndexOf("AreaDecalShader",StringComparison.Ordinal)>=0)
                return areaMaterials.TryGetValue(material,out var area)&&AllowsArea(area,material,property,current);
            var name=material.name;
            if(name.StartsWith("Batch (",StringComparison.Ordinal)&&name.EndsWith(")",StringComparison.Ordinal))name=name.Substring(7,name.Length-8);
            if(!sources.TryGetValue(name,out var source)||!source.Builtin||source.Custom)return false;
            // The Unity object map is writable at runtime, so membership alone
            // cannot certify that an asset/surface came from the base game.
            if(material.shader!=null&&material.shader.name.IndexOf("AreaDecalShader",StringComparison.Ordinal)>=0
                &&(source.Surface==null||!source.Surface.isValid||!source.Surface.isBuiltin))return false;
            object origin=null;
            if(batches.TryGetValue(material,out var key))key.textures.TryGetValue(Shader.PropertyToID(property),out origin);
            else if(source.Surface!=null&&source.Surface.isValid&&source.Surface.isDataLoaded&&source.Surface.textures!=null)
                {if(source.Surface.textures.TryGetValue(property,out var asset))origin=asset;}
            else if(AssetDatabase.global.resources.unityObjectsMap.ContainsObject(material))origin=current;
            if(origin is TextureAsset textureAsset)
            {
                if(!textureAsset.isValid||!textureAsset.isBuiltin||!textureAsset.isObjectLoaded||loadedTexture==null||loadedTexture.FieldType!=typeof(Texture))return false;
                var expected=loadedTexture.GetValue(textureAsset) as Texture;
                return VanillaSurfacePolicy.Allows(true,false,true,expected!=null&&current==expected);
            }
            if(origin is Texture texture&&texture!=null)
            {
                // Only the engine's exact neutral network placeholder is known
                // without TextureAsset metadata. Registered custom Texture objects
                // must not become eligible merely by entering the Unity map.
                bool builtin=property=="_WorldspaceAlbedo"&&material.shader!=null&&material.shader.name=="BH/NetCompositionMeshLitShader"&&texture==Texture2D.grayTexture;
                return VanillaSurfacePolicy.Allows(true,false,builtin,current==texture);
            }
            return false;
        }
    }
}
