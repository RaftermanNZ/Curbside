using System;

using System.Collections.Generic;

using Game;

using UnityEngine;



namespace MedianGrassReplacement.Systems

{

    /// <summary>

    /// Event-driven network-grass material controller.

    ///

    /// Material discovery follows committed road-composition changes:

    /// - no timed Resources.FindObjectsOfTypeAll polling

    /// - one initial RoadGrass probe per city/editor load

    /// - committed BatchesUpdated activity only arms/resets a short quiet-window debounce

    /// - an entire renderer refresh burst is coalesced into one material lookup

    /// - unsuccessful lookups are silent and do not loop on a timer

    /// - successful RoadGrass references are cached and reused directly

    /// - menu texture changes probe once only when no cached material exists

    /// - live UV changes only touch already-cached RoadGrass materials

    /// </summary>

    public partial class MedianMaterialSystem : GameSystemBase

    {

        private const string SourceMaterialName = "RoadGrass_50332d58933f19f4484c33141c83d019";

        private const string SourceShaderName = "BH/NetCompositionMeshLitShader";



        private const string AlbedoProperty = "_WorldspaceAlbedo";

        private const string NormalProperty = "_WorldspaceNormalMap";

        private const string MaskProperty = "_WorldspaceMaskMap";

        private const string BaseColorProperty = "_BaseColor";

        private const string UvScaleProperty = "_WorldspaceUVScale";



        private static MedianMaterialSystem s_activeSystem;

        private static bool s_refreshRequested;

        private static bool s_restoreRequested;

        private static bool s_uvScaleRequested;

        private const int CompositionQuietFrames = 30;



        private static bool s_compositionRenderProbePending;

        private static int s_compositionQuietFramesRemaining;

        private static int s_compositionSignalsSinceProbe;

        private static float s_pendingUvScale = 0.20f;

        private static string s_reason = "initial";

        private static string s_compositionProbeReason = "road composition batch refresh";



        private bool _waitingForRoadGrass;

        private int _compositionProbeCount;

        private int _compositionSignalCount;



        private readonly List<MaterialSnapshot> _materials = new();



        private Texture2D _customAlbedo;

        private Texture2D _customNormal;

        private Texture2D _customMask;



        internal static bool NeedsDiscovery =>

            s_activeSystem != null && s_activeSystem._waitingForRoadGrass;



        public static void RequestRefresh(string reason)

        {

            s_reason = reason ?? "refresh";

            s_refreshRequested = true;

            s_restoreRequested = false;



            if (s_activeSystem != null)

            {

                s_activeSystem.Enabled = true;

            }

        }

        internal static void RestoreNow()

        {

            s_activeSystem?.RestoreVanilla(clearSnapshots: true);

            s_refreshRequested = false; s_restoreRequested = false; s_uvScaleRequested = false;

            s_compositionRenderProbePending = false;

            if (s_activeSystem != null) s_activeSystem.Enabled = false;

        }



        public static void NotifyCompositionRenderActivity(string reason)

        {

            s_compositionProbeReason = string.IsNullOrWhiteSpace(reason)

                ? "road composition batch refresh"

                : reason;



            // BatchesUpdated can remain present for many rendering frames during

            // one user operation. Record activity only; do not perform a material

            // lookup here. The controller waits for a short quiet window and then

            // performs one coalesced lookup.

            s_compositionQuietFramesRemaining = CompositionQuietFrames;

            s_compositionSignalsSinceProbe++;

            s_compositionRenderProbePending = true;



            if (s_activeSystem != null)

            {

                s_activeSystem.Enabled = true;

            }

        }



        public static void RequestUvScale(float value)

        {

            s_pendingUvScale = Mathf.Clamp(value, 0.05f, 0.50f);

            s_uvScaleRequested = true;



            if (s_activeSystem != null)

            {

                s_activeSystem.Enabled = true;

            }

        }



        public static void RequestRestore(string reason)

        {

            s_reason = reason ?? "restore";

            s_restoreRequested = true;

            s_refreshRequested = false;



            if (s_activeSystem != null)

            {

                s_activeSystem.Enabled = true;

            }

        }



        protected override void OnCreate()

        {

            base.OnCreate();

            s_activeSystem = this;

            Enabled = true;

        }



        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)

        {

            base.OnGameLoadingComplete(purpose, mode);



            if ((mode & GameMode.GameOrEditor) == GameMode.None)

            {

                return;

            }



            // A new world may own entirely new Unity Material instances. Never

            // carry cached references across city/editor loads.

            RestoreVanilla(clearSnapshots: true);

            _compositionProbeCount = 0;

            _compositionSignalCount = 0;

            s_compositionSignalsSinceProbe = 0;

            s_compositionQuietFramesRemaining = 0;

            s_compositionRenderProbePending = false;



            string selected = Mod.Settings?.DisableTextureReplacement==true ? TextureCatalog.VanillaValue : Mod.Settings?.SelectedTexture ?? TextureCatalog.VanillaValue;

            _waitingForRoadGrass = !string.IsNullOrWhiteSpace(selected)

                && selected != TextureCatalog.VanillaValue;



            if (!_waitingForRoadGrass)

            {

                Mod.Debug($"Game loading complete ({mode}); Vanilla network grass selected. No RoadGrass probe required.");

                return;

            }



            Mod.Debug(

                $"Game loading complete ({mode}); probing RoadGrass once. " +

                "If unavailable, the mod will wait for a committed road-composition batch refresh instead of polling.");



            if (ProbeAndApplySelection("initial post-load probe"))

            {

                _waitingForRoadGrass = false;

            }

            else

            {

                Mod.Debug(

                    "RoadGrass was not available during the initial post-load probe; " +

                    "waiting for a committed road-composition renderer-refresh burst. That burst will be debounced into one material lookup; no timed scanning is active.");

            }

        }



        protected override void OnUpdate()

        {

            if (Mod.Saving) return;

            if (s_uvScaleRequested)

            {

                s_uvScaleRequested = false;

                ApplyUvScaleLive(s_pendingUvScale);

            }



            if (s_restoreRequested)

            {

                s_restoreRequested = false;

                _waitingForRoadGrass = false;

                RestoreVanilla(clearSnapshots: true);

                return;

            }



            if (s_refreshRequested)

            {

                s_refreshRequested = false;

                // A committed road rebuild can destroy the cached instances.
                // Decide whether to probe using live entries, rather than a stale
                // Count that would skip the only menu-triggered discovery.
                PruneDestroyedSnapshots();



                string selected = Mod.Settings?.DisableTextureReplacement==true ? TextureCatalog.VanillaValue : Mod.Settings?.SelectedTexture ?? TextureCatalog.VanillaValue;

                if (string.IsNullOrWhiteSpace(selected) || selected == TextureCatalog.VanillaValue)

                {

                    _waitingForRoadGrass = false;

                    RestoreVanilla(clearSnapshots: false);

                    Mod.Debug("Vanilla network grass selected; restored tracked RoadGrass material state.");

                }

                else if (_materials.Count > 0)

                {

                    // Normal steady-state path: menu changes reuse the cached

                    // exact RoadGrass references and do not perform any discovery.

                    if (ApplySelectionToTrackedMaterials())

                    {

                        _waitingForRoadGrass = false;

                        Mod.Debug($"Applied network-grass selection from menu using cached RoadGrass material reference(s). Reason='{s_reason}'.");

                    }

                }

                else if (ProbeAndApplySelection("menu selection probe"))

                {

                    _waitingForRoadGrass = false;

                }

                else

                {

                    _waitingForRoadGrass = true;

                    Mod.Debug(

                        "The selected texture is saved, but RoadGrass is not instantiated yet; " +

                        "waiting for a committed road-composition renderer-refresh burst.");

                }

            }



            if (!_waitingForRoadGrass)

            {

                s_compositionRenderProbePending = false;

                s_compositionSignalsSinceProbe = 0;

                s_compositionQuietFramesRemaining = 0;

                return;

            }



            if (!s_compositionRenderProbePending)

            {

                return;

            }



            // Debounce renderer activity. Every BatchesUpdated frame moves the

            // boundary forward; no global Unity material lookup happens until

            // the composition-refresh burst has been quiet for this window.

            if (s_compositionQuietFramesRemaining > 0)

            {

                s_compositionQuietFramesRemaining--;

                return;

            }



            s_compositionRenderProbePending = false;

            int coalescedSignals = s_compositionSignalsSinceProbe;

            s_compositionSignalsSinceProbe = 0;

            _compositionSignalCount += coalescedSignals;

            _compositionProbeCount++;



            if (ProbeAndApplySelection($"debounced composition render probe after {s_compositionProbeReason}"))

            {

                _waitingForRoadGrass = false;

                Mod.Debug(

                    $"RoadGrass became available after {_compositionProbeCount} debounced material lookup(s); " +

                    $"{_compositionSignalCount} composition-render signal(s) were coalesced. " +

                    "Cached material references will now be reused directly.");

            }

            else

            {

                // A miss is expected if the completed composition burst did not

                // instantiate RoadGrass. Wait for a future composition burst.

                // No retry loop and no disk log are generated for the miss.

            }

        }



        private bool ProbeAndApplySelection(string trigger)

        {

            if (!DiscoverRoadGrassMaterialsOnce(trigger))

            {

                return false;

            }



            return ApplySelectionToTrackedMaterials();

        }



        private bool DiscoverRoadGrassMaterialsOnce(string trigger)

        {

            PruneDestroyedSnapshots();



            Material[] materials = Resources.FindObjectsOfTypeAll<Material>();

            int matchingNames = 0;

            int newlyCaptured = 0;



            foreach (Material material in materials)

            {

                if (material == null || !IsRoadGrassMaterialName(material.name))

                {

                    continue;

                }



                matchingNames++;



                if (!ValidateRoadGrassMaterial(material))

                {

                    continue;

                }



                if (FindSnapshot(material) != null)

                {

                    continue;

                }



                _materials.Add(new MaterialSnapshot(

                    material,

                    material.GetTexture(AlbedoProperty),

                    material.GetTexture(NormalProperty),

                    material.GetTexture(MaskProperty),

                    material.HasProperty(BaseColorProperty),

                    material.HasProperty(BaseColorProperty) ? material.GetColor(BaseColorProperty) : Color.white,

                    material.HasProperty(UvScaleProperty),

                    material.HasProperty(UvScaleProperty) ? material.GetFloat(UvScaleProperty) : 0.20f));



                newlyCaptured++;

            }



            if (_materials.Count == 0)

            {

                if (matchingNames > 0)

                {

                    Mod.Log.Warn(

                        $"RoadGrass probe '{trigger}' found {matchingNames} name match(es), but none passed exact shader/property validation.");

                }



                return false;

            }



            if (newlyCaptured > 0)

            {

                Mod.Debug(

                    $"RoadGrass probe '{trigger}' captured {newlyCaptured} validated material instance(s); " +

                    $"{_materials.Count} total instance(s) are cached.");

            }



            return true;

        }



        private bool ApplySelectionToTrackedMaterials()

        {

            PruneDestroyedSnapshots();



            string selected = Mod.Settings?.DisableTextureReplacement==true ? TextureCatalog.VanillaValue : Mod.Settings?.SelectedTexture ?? TextureCatalog.VanillaValue;



            if (string.IsNullOrWhiteSpace(selected) || selected == TextureCatalog.VanillaValue)

            {

                RestoreVanilla(clearSnapshots: false);

                return true;

            }



            if (_materials.Count == 0)

            {

                return false;

            }



            if (!LoadSelectedTextures(selected))

            {

                RestoreVanilla(clearSnapshots: false);

                return true;

            }



            int applied = ApplySelectedTexturesToRoadGrass();

            if (applied <= 0)

            {

                RestoreVanilla(clearSnapshots: false);

                return false;

            }



            Mod.Debug(

                $"Applied network grass texture '{selected}' to {applied} cached RoadGrass material instance(s) " +

                "(grassy medians and grassy sidewalks).");



            return true;

        }



        private static bool IsRoadGrassMaterialName(string materialName)

        {

            return !string.IsNullOrEmpty(materialName)

                && materialName.IndexOf(SourceMaterialName, StringComparison.OrdinalIgnoreCase) >= 0;

        }



        private static bool ValidateRoadGrassMaterial(Material material)

        {

            return material != null

                && material.shader != null

                && string.Equals(material.shader.name, SourceShaderName, StringComparison.Ordinal)

                && material.HasProperty(AlbedoProperty)

                && material.HasProperty(NormalProperty)

                && material.HasProperty(MaskProperty);

        }



        private bool LoadSelectedTextures(string selected)

        {
            // Detach owned material references before releasing their textures.
            RestoreVanilla(clearSnapshots: false);



            string folder = Mod.Settings?.TextureFolder ?? TextureCatalog.DefaultFolder;

            if (!TextureCatalog.TryResolveSelection(folder, selected, out TextureSetPaths paths, out string resolveError))

            {

                Mod.Log.Warn($"Network grass texture selection was not applied: {resolveError}");

                return false;

            }



            _customAlbedo = FileTextureLoader.LoadPng(paths.Albedo, linear: false, out string albedoError);

            if (_customAlbedo == null)

            {

                Mod.Log.Warn($"Could not load network grass albedo '{paths.Albedo}': {albedoError}");

                return false;

            }



            if (!string.IsNullOrWhiteSpace(paths.Normal))

            {

                _customNormal = FileTextureLoader.LoadPng(paths.Normal, linear: true, out string normalError);

                if (_customNormal == null)

                {

                    Mod.Log.Warn($"Normal map '{paths.Normal}' was ignored: {normalError}");

                }

                else if (!FileTextureLoader.SameDimensions(_customAlbedo, _customNormal))

                {

                    Mod.Log.Warn($"Normal map '{paths.Normal}' was ignored because its dimensions do not match the albedo.");

                    UnityEngine.Object.Destroy(_customNormal);

                    _customNormal = null;

                }

            }



            if (!string.IsNullOrWhiteSpace(paths.Mask))

            {

                _customMask = FileTextureLoader.LoadPng(paths.Mask, linear: true, out string maskError);

                if (_customMask == null)

                {

                    Mod.Log.Warn($"Mask map '{paths.Mask}' was ignored: {maskError}");

                }

                else if (!FileTextureLoader.SameDimensions(_customAlbedo, _customMask))

                {

                    Mod.Log.Warn($"Mask map '{paths.Mask}' was ignored because its dimensions do not match the albedo.");

                    UnityEngine.Object.Destroy(_customMask);

                    _customMask = null;

                }

            }



            return true;

        }



        private int ApplySelectedTexturesToRoadGrass()

        {

            int applied = 0;



            foreach (MaterialSnapshot snapshot in _materials)

            {

                Material material = snapshot.Material;

                if (!ValidateRoadGrassMaterial(material))

                {

                    continue;

                }



                snapshot.CaptureBaseline();

                material.SetTexture(AlbedoProperty, _customAlbedo ?? snapshot.Albedo);

                material.SetTexture(NormalProperty, _customNormal ?? snapshot.Normal);

                material.SetTexture(MaskProperty, _customMask ?? snapshot.Mask);

                snapshot.AppliedAlbedo = _customAlbedo ?? snapshot.Albedo;

                snapshot.AppliedNormal = _customNormal ?? snapshot.Normal;

                snapshot.AppliedMask = _customMask ?? snapshot.Mask;

                snapshot.OwnTextures = true;



                if (snapshot.HasBaseColor && material.HasProperty(BaseColorProperty))

                {

                    material.SetColor(BaseColorProperty, Color.white);

                    snapshot.AppliedBaseColor = Color.white; snapshot.OwnBaseColor = true;

                }



                if (snapshot.HasUvScale && material.HasProperty(UvScaleProperty))

                {

                    float uvScale = Mathf.Clamp(Mod.Settings?.UvScale ?? 0.20f, 0.05f, 0.50f);

                    material.SetFloat(UvScaleProperty, uvScale);

                    snapshot.AppliedUvScale = uvScale; snapshot.OwnUvScale = true;

                }



                applied++;

            }



            return applied;

        }



        private void ApplyUvScaleLive(float value)

        {

            string selected = Mod.Settings?.DisableTextureReplacement==true ? TextureCatalog.VanillaValue : Mod.Settings?.SelectedTexture ?? TextureCatalog.VanillaValue;

            if (string.IsNullOrWhiteSpace(selected) || selected == TextureCatalog.VanillaValue)

            {

                return;

            }



            float clamped = Mathf.Clamp(value, 0.05f, 0.50f);

            int changed = 0;



            foreach (MaterialSnapshot snapshot in _materials)

            {

                Material material = snapshot.Material;

                if (!ValidateRoadGrassMaterial(material))

                {

                    continue;

                }



                if (snapshot.HasUvScale && material.HasProperty(UvScaleProperty))

                {

                    snapshot.CaptureUvBaseline();

                    material.SetFloat(UvScaleProperty, clamped);

                    snapshot.AppliedUvScale = clamped; snapshot.OwnUvScale = true;

                    changed++;

                }

            }



            // UV changes only touch cached material references.

            LogVerbose($"Live RoadGrass UV scale set to {clamped:0.00} on {changed} cached material instance(s).");

        }



        private void RestoreVanilla(bool clearSnapshots)

        {

            foreach (MaterialSnapshot snapshot in _materials)

            {

                Material material = snapshot.Material;

                if (material == null)

                {

                    continue;

                }



                // Leave a later texture mod's writes intact. Restore only values

                // that still equal the last values applied by this controller.

                if (material.HasProperty(AlbedoProperty) && MaterialOwnership.CanRestore(snapshot.OwnTextures, material.GetTexture(AlbedoProperty), snapshot.AppliedAlbedo)) material.SetTexture(AlbedoProperty, snapshot.Albedo);

                if (material.HasProperty(NormalProperty) && MaterialOwnership.CanRestore(snapshot.OwnTextures, material.GetTexture(NormalProperty), snapshot.AppliedNormal)) material.SetTexture(NormalProperty, snapshot.Normal);

                if (material.HasProperty(MaskProperty) && MaterialOwnership.CanRestore(snapshot.OwnTextures, material.GetTexture(MaskProperty), snapshot.AppliedMask)) material.SetTexture(MaskProperty, snapshot.Mask);

                if (snapshot.HasBaseColor && material.HasProperty(BaseColorProperty) && MaterialOwnership.CanRestore(snapshot.OwnBaseColor, material.GetColor(BaseColorProperty), snapshot.AppliedBaseColor)) material.SetColor(BaseColorProperty, snapshot.BaseColor);

                if (snapshot.HasUvScale && material.HasProperty(UvScaleProperty) && MaterialOwnership.CanRestore(snapshot.OwnUvScale, material.GetFloat(UvScaleProperty), snapshot.AppliedUvScale)) material.SetFloat(UvScaleProperty, snapshot.UvScale);

                snapshot.OwnTextures = false; snapshot.OwnBaseColor = false; snapshot.OwnUvScale = false;

            }



            DestroyCustomTextures();



            if (clearSnapshots)

            {

                _materials.Clear();

            }

        }



        private MaterialSnapshot FindSnapshot(Material material)

        {

            foreach (MaterialSnapshot snapshot in _materials)

            {

                if (ReferenceEquals(snapshot.Material, material))

                {

                    return snapshot;

                }

            }



            return null;

        }



        private void PruneDestroyedSnapshots()

        {

            for (int i = _materials.Count - 1; i >= 0; i--)

            {

                if (_materials[i].Material == null)

                {

                    _materials.RemoveAt(i);

                }

            }

        }



        private void DestroyCustomTextures()

        {

            if (_customAlbedo != null) UnityEngine.Object.Destroy(_customAlbedo);

            if (_customNormal != null) UnityEngine.Object.Destroy(_customNormal);

            if (_customMask != null) UnityEngine.Object.Destroy(_customMask);



            _customAlbedo = null;

            _customNormal = null;

            _customMask = null;

        }



        [System.Diagnostics.Conditional("DEBUG")]

        private static void LogVerbose(string message)

        {

            if (Mod.Settings?.VerboseLogging == true)

            {

                Mod.Debug(message);

            }

        }



        protected override void OnDestroy()

        {

            RestoreVanilla(clearSnapshots: true);



            if (ReferenceEquals(s_activeSystem, this))

            {

                s_activeSystem = null;

            }



            base.OnDestroy();

        }



        private sealed class MaterialSnapshot

        {

            internal Texture AppliedAlbedo, AppliedNormal, AppliedMask;

            internal Color AppliedBaseColor;

            internal float AppliedUvScale;

            internal bool OwnTextures, OwnBaseColor, OwnUvScale;

            internal MaterialSnapshot(

                Material material,

                Texture albedo,

                Texture normal,

                Texture mask,

                bool hasBaseColor,

                Color baseColor,

                bool hasUvScale,

                float uvScale)

            {

                Material = material;

                Albedo = albedo;

                Normal = normal;

                Mask = mask;

                HasBaseColor = hasBaseColor;

                BaseColor = baseColor;

                HasUvScale = hasUvScale;

                UvScale = uvScale;

            }



            internal Material Material { get; }

            internal Texture Albedo { get; private set; }

            internal Texture Normal { get; private set; }

            internal Texture Mask { get; private set; }

            internal bool HasBaseColor { get; }

            internal Color BaseColor { get; private set; }

            internal bool HasUvScale { get; }

            internal float UvScale { get; private set; }

            internal void CaptureBaseline()
            {
                Albedo = MaterialOwnership.Baseline(OwnTextures, Material.GetTexture(AlbedoProperty), AppliedAlbedo, Albedo);
                Normal = MaterialOwnership.Baseline(OwnTextures, Material.GetTexture(NormalProperty), AppliedNormal, Normal);
                Mask = MaterialOwnership.Baseline(OwnTextures, Material.GetTexture(MaskProperty), AppliedMask, Mask);
                if (HasBaseColor && Material.HasProperty(BaseColorProperty))
                    BaseColor = MaterialOwnership.Baseline(OwnBaseColor, Material.GetColor(BaseColorProperty), AppliedBaseColor, BaseColor);
                CaptureUvBaseline();
            }

            internal void CaptureUvBaseline()
            {
                if (HasUvScale && Material.HasProperty(UvScaleProperty))
                    UvScale = MaterialOwnership.Baseline(OwnUvScale, Material.GetFloat(UvScaleProperty), AppliedUvScale, UvScale);
            }

        }

    }

}

