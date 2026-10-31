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
        private readonly Dictionary<string,AreaSource> areas=new Dictionary<string,AreaSource>(StringComparer.Ordinal);
        private readonly Dictionary<string,Source> sources=new Dictionary<string,Source>(StringComparer.Ordinal);
        private readonly Dictionary<Material,ManagedBatchSystem.MaterialKey> batches=new Dictionary<Material,ManagedBatchSystem.MaterialKey>();
        // The shipped API exposes isObjectLoaded but no non-loading getter.
        // Read only the managed Texture reference, with a fail-closed type guard.
        private static readonly FieldInfo loadedTexture=typeof(TextureAsset).GetField("m_Instance",BindingFlags.Instance|BindingFlags.NonPublic);
        internal void RefreshCatalog()
        {
            sources.Clear();
            areas.Clear();
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
            if(prefab==null||!VanillaSurfacePolicy.TargetArea(prefab.name)||!prefab.TryGet<RenderedArea>(out var rendered)||rendered==null)return;
            var original=rendered.m_Material;
            bool usesTexture=rendered.m_Version==RenderedArea.Version.OverrideMaterial&&rendered.m_BaseColorMap!=null;
            var source=new AreaSource{Prefab=prefab,TextureAsset=usesTexture?(TextureAsset)rendered.m_BaseColorMap:null,HasTextureReference=usesTexture,
                OriginalMaterial=original,OriginalTexture=original!=null&&original.HasProperty("_BaseColorMap")?original.GetTexture("_BaseColorMap"):null};
            AddAreaName(VanillaSurfacePolicy.AreaBatchName(prefab.name),source);
        }
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
            if(source.Ambiguous||!AreaBuiltin(source.Prefab)||!VanillaSurfacePolicy.TargetArea(source.Prefab.name)||property!="_BaseColorMap")return false;
            if(source.HasTextureReference)return source.TextureAsset!=null&&SameLoadedNativeTexture(source.TextureAsset,current);
            var original=source.OriginalMaterial;var expected=source.OriginalTexture;
            if(original==null||expected==null)return false;
            var map=AssetDatabase.global.resources.unityObjectsMap;
            return VanillaSurfacePolicy.Allows(true,false,expected!=null&&map.ContainsObject(original)&&map.ContainsObject(expected),current==expected);
        }
        private static bool SameLoadedNativeTexture(TextureAsset asset,Texture current)
        {
            if(!asset.isValid||!asset.isBuiltin||!asset.isObjectLoaded||loadedTexture==null||loadedTexture.FieldType!=typeof(Texture))return false;
            var expected=loadedTexture.GetValue(asset) as Texture;
            return expected!=null&&current==expected;
        }
        internal void Begin(ManagedBatchSystem system)
        {
            batches.Clear();if(system==null)return;
            foreach(var entry in system.materials)if(entry.Value!=null)batches[entry.Value]=entry.Key;
        }
        internal void End()=>batches.Clear();
        internal void Clear(){End();sources.Clear();areas.Clear();}
        internal bool Allows(Material material,string property,Texture current)
        {
            if(material==null||current==null)return false;
            // Only rendered area-batch copies are edited. Never modify their
            // shared prefab material templates, which other packs could reuse.
            if(material.shader!=null&&material.shader.name.IndexOf("AreaDecalShader",StringComparison.Ordinal)>=0)
                return areas.TryGetValue(material.name,out var area)&&AllowsArea(area,material,property,current);
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
