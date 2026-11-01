using System;using System.Collections.Generic;using System.IO;using Colossal.Serialization.Entities;using Game;using UnityEngine;
namespace MedianGrassReplacement.Systems
{
    internal static class GlobalSurfacePolicy
    {
        internal static string LegacyChoice(int mode,string first,string second,string fallback)=>mode==0?"":string.IsNullOrWhiteSpace(mode==1?first:second)?fallback:mode==1?first:second;
        internal static float RoadUv(float value)=>float.IsNaN(value)||float.IsInfinity(value)?.16f:Math.Max(.01f,Math.Min(1f,value));
        internal static float SidewalkTiling(float value)=>float.IsNaN(value)||float.IsInfinity(value)?1f:Math.Max(.1f,Math.Min(10f,value));
        internal static int AssetKind(string name,string shader)
        {
            if(string.IsNullOrEmpty(shader)||shader.IndexOf("AreaDecalShader",StringComparison.Ordinal)<0)return 0;
            // Exact renderer copies only; no substring-based surface expansion.
            if(name==VanillaSurfacePolicy.AreaBatchName("Pavement Surface 01"))return 4;
            if(name==VanillaSurfacePolicy.AreaBatchName("Pavement Surface 02"))return 2;
            return 0;
        }
        internal static int Kind(string name,string shader)
        {
            if(shader!="BH/NetCompositionMeshLitShader")return 0;
            string key=(name??"").ToLowerInvariant();
            foreach(var excluded in new[]{"grass","curb","kerb","marking","paint","decal","rail","gravel","dirt"})if(key.Contains(excluded))return 0;
            if(key.StartsWith("batch (road_",StringComparison.Ordinal))return 3; // Native shared road/sidewalk material observed in the game log.
            if(key.Contains("sidewalk")||key.Contains("pavement")||key.Contains("paver")||key.Contains("footpath"))return 2;
            return key.Contains("asphalt")||key.Contains("tarmac")||key.Contains("road surface")?1:0;
        }
    }
    // Runtime material edits only: no prefab, composition, lane or saved network mutation.
    public partial class GlobalSurfaceMaterialSystem:GameSystemBase
    {
        private sealed class Patch {internal Material Material;internal string Property;internal int Role;internal Texture Before,Applied;}
        private readonly Dictionary<(Material,string),Patch> _patches=new Dictionary<(Material,string),Patch>();
        private readonly VanillaSurfaceOwnership _vanilla=new VanillaSurfaceOwnership();
        private Unity.Entities.EntityQuery _areaPrefabs;
        protected override void OnCreate()
        {
            base.OnCreate();
            _areaPrefabs=GetEntityQuery(Unity.Entities.ComponentType.ReadOnly<Game.Prefabs.PrefabData>(),Unity.Entities.ComponentType.ReadOnly<Game.Prefabs.RenderedAreaData>());
        }
        private void RefreshAreaCatalog()
        {
            var prefabs=World.GetExistingSystemManaged<Game.Prefabs.PrefabSystem>();
            if(prefabs==null)return;
            // Read values on explicit refresh only. No area renderer buffers,
            // native batch access, prefab mutations or asset loads are needed.
            using(var entities=_areaPrefabs.ToEntityArray(Unity.Collections.Allocator.Temp))
                foreach(var entity in entities)if(prefabs.TryGetPrefab<Game.Prefabs.AreaPrefab>(entity,out var prefab))_vanilla.AddAreaPrefab(prefab);
        }
        private sealed class UvPatch {internal Material Material;internal string Property;internal bool Scalar,Road;internal float BeforeFloat,AppliedFloat;internal Vector2 BeforeScale,AppliedScale;}
        private readonly Dictionary<(Material,string),UvPatch> _uvPatches=new Dictionary<(Material,string),UvPatch>();
        private Texture2D _roadTexture,_walkTexture;
        private readonly List<string> _loadErrors=new List<string>();
        private readonly Dictionary<string,Texture2D> _textures=new Dictionary<string,Texture2D>(StringComparer.OrdinalIgnoreCase);
        private long _bytes;private bool _active;private static bool _refresh=true,_discover,_uvRefresh;private static float _quietUntil;
        internal static string Status="Global surfaces: Vanilla.";
        internal static void RequestRefresh(){_refresh=true;}
        internal static void RequestUvRefresh(){_uvRefresh=true;}
        internal static void NotifyRenderActivity(){if(Mod.Settings==null||Mod.Settings.DisableTextureReplacement||(string.IsNullOrWhiteSpace(Mod.Settings.GlobalRoadTexture)&&string.IsNullOrWhiteSpace(Mod.Settings.GlobalSidewalkTexture)))return;_discover=true;_quietUntil=UnityEngine.Time.realtimeSinceStartup+.35f;}
        private Texture2D Load(string path)
        {
            path=SurfaceTextureCatalog.Resolve(path??"");if(string.IsNullOrWhiteSpace(path))return null;
            var file=new FileInfo(Path.GetFullPath(path.Trim().Trim('"')));string key=file.FullName+"|"+file.Length+"|"+file.LastWriteTimeUtc.Ticks;
            if(_textures.TryGetValue(key,out var cached))return cached;
            string error;var texture=FileTextureLoader.LoadPng(file.FullName,false,out error,false,128L*1024*1024,4096);
            if(texture==null)throw new InvalidOperationException(error);
            if(!SurfaceUpgradePolicy.FitsBudget(texture.width,texture.height,_bytes)){UnityEngine.Object.Destroy(texture);throw new InvalidOperationException("Global texture cache budget reached; other live materials still retain cached textures. Disable competing replacements or choose smaller images.");}
            _bytes+=(long)texture.width*texture.height*16/3;texture.wrapMode=TextureWrapMode.Repeat;_textures[key]=texture;return texture;
        }
        internal void Restore(bool dispose)
        {
            foreach(var patch in _uvPatches.Values)if(patch.Material!=null&&patch.Material.HasProperty(patch.Property))
            {
                if(patch.Scalar){if(patch.Material.GetFloat(patch.Property)==patch.AppliedFloat)patch.Material.SetFloat(patch.Property,patch.BeforeFloat);}
                else if(patch.Material.GetTextureScale(patch.Property)==patch.AppliedScale)patch.Material.SetTextureScale(patch.Property,patch.BeforeScale);
            }
            _uvPatches.Clear();
            foreach(var patch in _patches.Values)if(patch.Material!=null&&patch.Material.HasProperty(patch.Property)&&MaterialOwnership.CanRestore(true,patch.Material.GetTexture(patch.Property),patch.Applied))patch.Material.SetTexture(patch.Property,patch.Before);
            _patches.Clear();if(dispose)
            {
                // A copied material or a later third-party edit may still hold
                // one of our textures. Never destroy it while it is referenced.
                var retained=ReferencedTextures();var released=new List<string>();
                foreach(var pair in _textures)if(pair.Value==null||!retained.Contains(pair.Value))
                {if(pair.Value!=null)UnityEngine.Object.Destroy(pair.Value);released.Add(pair.Key);}
                foreach(var key in released)_textures.Remove(key);
                _bytes=0;foreach(var texture in _textures.Values)if(texture!=null)_bytes+=(long)texture.width*texture.height*16/3;
                _roadTexture=_walkTexture=null;_loadErrors.Clear();
            }
        }
        private void RecoverUntrackedAreas()
        {
            var map=Colossal.IO.AssetDatabase.AssetDatabase.global?.resources?.unityObjectsMap;
            if(map==null)return;
            int restored=0,unresolved=0;
            foreach(var material in Resources.FindObjectsOfTypeAll<Material>())
            {
                if(material==null||material.shader==null||!material.name.StartsWith("Area batch (",StringComparison.Ordinal)
                    ||material.shader.name.IndexOf("AreaDecalShader",StringComparison.Ordinal)<0||!material.HasProperty("_BaseColorMap"))continue;
                var current=material.GetTexture("_BaseColorMap");
                if(current==null||!SurfaceRestorePolicy.IsCurbsideTexture(current.name)||_patches.ContainsKey((material,"_BaseColorMap")))continue;
                bool hasOriginal=_vanilla.TryGetAreaOriginal(material,out var original);
                if(!SurfaceRestorePolicy.CanRecover(true,map.ContainsObject(material),map.ContainsObject(current),false,current.name,hasOriginal,current==original))
                {if(!hasOriginal)unresolved++;continue;}
                material.SetTexture("_BaseColorMap",original);restored++;
            }
            // One summary per explicit refresh, not automatic per-surface tracing.
            if(restored>0)Mod.Log.Info("Surface restoration recovered "+restored+" untracked Curbside area textures from prefab originals.");
            if(unresolved>0)Mod.Log.Warn("Surface restoration: "+unresolved+" Curbside area textures have no unambiguous loaded original; left intact rather than guessing.");
        }
        private bool ApplyProperty(Material material,string property,Texture2D replacement,int role)
        {
            if(!material.HasProperty(property))return false;
            bool found=_patches.TryGetValue((material,property),out var patch);
            if(found)return material.GetTexture(property)==patch.Applied;
            var original=material.GetTexture(property);
            if(!_vanilla.Allows(material,property,original))return false;
            material.SetTexture(property,replacement);
            if(Mod.Settings.VerboseLogging)Mod.Log.Info("Global surface target: "+material.name+" property="+property+" texture="+replacement.width+"x"+replacement.height);
            _patches[(material,property)]=new Patch{Material=material,Property=property,Role=role,Before=original,Applied=replacement};return true;
        }
        private void ReleaseUnusedTextures()
        {
            // Explicit refresh only, after owned material properties were restored.
            // Keep any cached image still referenced by a live material (including another mod).
            var selected=new HashSet<string>();
            foreach(var value in new[]{Mod.Settings.GlobalRoadTexture,Mod.Settings.GlobalSidewalkTexture})try{string path=SurfaceTextureCatalog.Resolve(value??"");if(string.IsNullOrWhiteSpace(path))continue;var file=new FileInfo(Path.GetFullPath(path.Trim().Trim('"')));if(file.Exists)selected.Add(file.FullName+"|"+file.Length+"|"+file.LastWriteTimeUtc.Ticks);}catch(Exception){/* Load reports the invalid selection below. */}
            var retained=ReferencedTextures();
            var stale=new List<string>();foreach(var pair in _textures)if(pair.Value==null||!selected.Contains(pair.Key)&&!retained.Contains(pair.Value))stale.Add(pair.Key);
            foreach(var key in stale){var texture=_textures[key];if(texture!=null){_bytes-=(long)texture.width*texture.height*16/3;UnityEngine.Object.Destroy(texture);}_textures.Remove(key);}
            _bytes=0;foreach(var texture in _textures.Values)if(texture!=null)_bytes+=(long)texture.width*texture.height*16/3;
        }
        private static HashSet<Texture> ReferencedTextures()
        {
            var retained=new HashSet<Texture>();
            foreach(var material in Resources.FindObjectsOfTypeAll<Material>())if(material!=null&&material.shader!=null)
                foreach(var property in material.GetTexturePropertyNames()){var texture=material.GetTexture(property);if(texture!=null)retained.Add(texture);}
            return retained;
        }
        private void RefreshTextures()
        {
            var settings=Mod.Settings;_roadTexture=_walkTexture=null;_loadErrors.Clear();ReleaseUnusedTextures();
            Mod.Log.Info("Global surface selection: road="+(string.IsNullOrWhiteSpace(settings.GlobalRoadTexture)?"Vanilla":Path.GetFileName(settings.GlobalRoadTexture))+" sidewalk="+(string.IsNullOrWhiteSpace(settings.GlobalSidewalkTexture)?"Vanilla":Path.GetFileName(settings.GlobalSidewalkTexture)));
            try{_roadTexture=Load(settings.GlobalRoadTexture);}catch(Exception e){_loadErrors.Add("Road: "+e.Message);}
            try{_walkTexture=Load(settings.GlobalSidewalkTexture);}catch(Exception e){_loadErrors.Add("Sidewalk: "+e.Message);}
        }
        private void SetUv(Material material,string property,bool scalar,bool road)
        {
            if(!material.HasProperty(property))return;
            bool found=_uvPatches.TryGetValue((material,property),out var patch);
            if(!found){patch=new UvPatch{Material=material,Property=property,Scalar=scalar,Road=road};if(scalar)patch.BeforeFloat=material.GetFloat(property);else patch.BeforeScale=material.GetTextureScale(property);_uvPatches[(material,property)]=patch;}
            else if(scalar?material.GetFloat(property)!=patch.AppliedFloat:material.GetTextureScale(property)!=patch.AppliedScale)return; // Preserve a later third-party UV edit.
            if(scalar){patch.AppliedFloat=Mod.Settings.RoadSurfaceUv;if(material.GetFloat(property)!=patch.AppliedFloat)material.SetFloat(property,patch.AppliedFloat);}
            else{patch.AppliedScale=patch.BeforeScale*(patch.Road?Mod.Settings.RoadSurfaceUv/.16f:Mod.Settings.SidewalkSurfaceTiling);if(material.GetTextureScale(property)!=patch.AppliedScale)material.SetTextureScale(property,patch.AppliedScale);}
        }
        private void PruneDestroyed()
        {
            List<(Material,string)> stale=null;
            foreach(var pair in _patches)if(pair.Value.Material==null){if(stale==null)stale=new List<(Material,string)>();stale.Add(pair.Key);}
            if(stale!=null){foreach(var key in stale)_patches.Remove(key);stale.Clear();}
            foreach(var pair in _uvPatches)if(pair.Value.Material==null){if(stale==null)stale=new List<(Material,string)>();stale.Add(pair.Key);}
            if(stale!=null)foreach(var key in stale)_uvPatches.Remove(key);
        }
        private void ApplyUv()
        {
            PruneDestroyed();
            foreach(var patch in _patches.Values)
            {
                if(patch.Material.GetTexture(patch.Property)!=patch.Applied)continue;
                if(patch.Property=="_WorldspaceAlbedo")SetUv(patch.Material,"_WorldspaceUVScale",true,true);
                else foreach(var property in new[]{"_BaseColorMap","_NormalMap","_MaskMap"})SetUv(patch.Material,property,false,patch.Role==1);
            }
        }
        private string _lastReport;
        private void Apply()
        {
            var road=_roadTexture;var walk=_walkTexture;var errors=_loadErrors;
            if(road==null&&walk==null){Status="Global surfaces: Vanilla. "+string.Join(" ",errors);Mod.Log.Info(Status);return;}
            int roads=0,walks=0;var candidates=new List<string>();
            _vanilla.Begin(World.GetExistingSystemManaged<Game.Rendering.ManagedBatchSystem>());
            try{
            foreach(var material in Resources.FindObjectsOfTypeAll<Material>())
            {
                if(material==null||material.shader==null)continue;
                int kind=material.name.StartsWith("Batch (",StringComparison.Ordinal)?GlobalSurfacePolicy.Kind(material.name,material.shader.name):0;
                if(kind==0&&Mod.Settings.SurfaceTexturesOnAssets)kind=GlobalSurfacePolicy.AssetKind(material.name,material.shader.name);
                if(kind==0)continue;
                bool roadApplied=false,walkApplied=false;
                if((kind==1||kind==3)&&road!=null)roadApplied=ApplyProperty(material,"_WorldspaceAlbedo",road,1);
                if(kind==4&&road!=null)roadApplied=ApplyProperty(material,"_BaseColorMap",road,1);
                if((kind==2||kind==3)&&walk!=null)walkApplied=ApplyProperty(material,"_BaseColorMap",walk,2);
                if(roadApplied)roads++;if(walkApplied)walks++;
                if(!roadApplied&&!walkApplied&&candidates.Count<12)candidates.Add(material.name);
            }
            }finally{_vanilla.End();}
            ApplyUv();
            Status="Global surfaces: "+roads+" road and "+walks+" sidewalk material(s). Cache "+_textures.Count+" textures, "+(_bytes/1048576)+" MiB estimate (GPU/native overhead extra). "+string.Join(" ",errors);if(Status==_lastReport)return;_lastReport=Status;Mod.Log.Info(Status);
            if((road!=null&&roads==0||walk!=null&&walks==0)&&candidates.Count>0)Mod.Log.Warn("Global surface unmatched network materials: "+string.Join(", ",candidates));
        }
        protected override void OnGamePreload(Purpose purpose,GameMode mode){Restore(true);_vanilla.Clear();_active=false;_disabledRestored=false;_refresh=true;_discover=false;base.OnGamePreload(purpose,mode);}
        protected override void OnGameLoadingComplete(Purpose purpose,GameMode mode){base.OnGameLoadingComplete(purpose,mode);_active=(mode&GameMode.GameOrEditor)!=GameMode.None;_refresh=true;}
        protected override void OnUpdate()
        {
            long started=ModDiagnostics.RuntimeCosts.Begin();
            try{UpdateMeasured();}finally{ModDiagnostics.RuntimeCosts.End("GlobalSurfaceMaterialSystem",started);}
        }
        private bool _disabledRestored;
        private void UpdateMeasured()
        {
            if(!_active||Mod.Settings==null||Mod.Saving)return;
            if(Mod.Settings.DisableTextureReplacement)
            {
                if(!_disabledRestored){_vanilla.RefreshCatalog();RefreshAreaCatalog();RecoverUntrackedAreas();Restore(true);_disabledRestored=true;}
                _refresh=true;_discover=false;_uvRefresh=false;return;
            }
            _disabledRestored=false;
            if(_refresh)
            {
                _refresh=false;_discover=false;_uvRefresh=false;
                _vanilla.RefreshCatalog();RefreshAreaCatalog();RecoverUntrackedAreas();Restore(false);RefreshTextures();Apply();
            }
            else if(_uvRefresh){_uvRefresh=false;ApplyUv();}
            else if(_discover&&UnityEngine.Time.realtimeSinceStartup>=_quietUntil){_discover=false;Apply();}
        }
        protected override void OnDestroy(){Restore(true);_vanilla.Clear();base.OnDestroy();}
    }
}
