using System;

using Colossal;

using Colossal.IO.AssetDatabase;

using Colossal.Localization;

using Colossal.Logging;

using Game;

using Game.Modding;

using Game.SceneFlow;

using Game.Rendering;

using MedianGrassReplacement.Localization;

using MedianGrassReplacement.Systems;



namespace MedianGrassReplacement

{

    public sealed class Mod : IMod

    {

        public const string ModId = "MedianGrassReplacement";

        public const string DisplayName = "The Curbside Mod";

        [System.Diagnostics.Conditional("DEBUG")]

        internal static void Debug(string message) => Log.Info(message);



        internal static readonly ILog Log =

            LogManager.GetLogger(ModId).SetShowsErrorsInUI(false);



        internal static bool Saving { get; private set; }

        internal static void ResetSaveGuard() { SignalSaveAnimationGuard.End(); Saving = false; }

        private void SaveState(string name, string preview, bool start, bool success)
        {
            if(start)SettingsSaveSystem.Flush();
            Saving = start;
            var world=Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if(start)
            {
                if(world!=null&&world.IsCreated)world.GetExistingSystemManaged<SaveRecoverySystem>()?.Observe();
                if(world!=null&&world.IsCreated)world.GetExistingSystemManaged<TemporaryPaintVisibilitySystem>()?.RestoreAll();
                SignalSaveAnimationGuard.Begin(world);
                if(world!=null&&world.IsCreated)world.GetOrCreateSystemManaged<GeneratedVisualSaveSystem>().PrepareBeforeSaveFrame();
            }
            else
            {
                SignalSaveAnimationGuard.End();
                if(world!=null&&world.IsCreated)world.GetExistingSystemManaged<SaveRecoverySystem>()?.Reset();
                if(world!=null&&world.IsCreated)world.GetExistingSystemManaged<GeneratedVisualSaveSystem>()?.EndSaveWindow();
            }
            Log.Info("Save/load state: start="+start+" success="+success);
            if(ModDiagnostics.TraceBuffer.Enabled)ModDiagnostics.TraceBuffer.Record("save.state","start="+start+" success="+success);
        }

        internal static Setting Settings { get; private set; }



        private static int DiagnosticFrameNumber() => UnityEngine.Time.frameCount;

        public void OnLoad(UpdateSystem updateSystem)

        {

            AppDomain.CurrentDomain.SetData("Curbside.Active.v1",true);Log.Info("Loading The Curbside Mod "+BuildIdentity.Label+".");

            CurbsideReadPhase.Initialize(updateSystem.World);
            if (NativeSignalProjectionSystem.Supported)
            {
                updateSystem.World.GetOrCreateSystemManaged<NativeCrosswalkDragSystem>();
                updateSystem.World.GetOrCreateSystemManaged<NativeSignalProjectionSystem>();
                updateSystem.World.GetOrCreateSystemManaged<NativePaintProjectionSystem>();
                updateSystem.UpdateBefore<NativeProjectionBoundarySystem, Game.Tools.ToolApplySystem>(SystemUpdatePhase.ApplyTool);
                updateSystem.UpdateBefore<NativeProjectionBoundarySystem, Game.Tools.ToolClearSystem>(SystemUpdatePhase.ClearTool);
                updateSystem.UpdateBefore<NativeApproachStopPreviewSystem, Game.Objects.SecondaryObjectSystem>(SystemUpdatePhase.Modification4B);
                updateSystem.UpdateAfter<NativeApproachStopPreviewRestoreSystem, Game.Net.SecondaryLaneSystem>(SystemUpdatePhase.Modification4B);
            }
            Log.Info("Native preview preservation: Vanilla previews are the default; native changed-actor signal and paint projection. Explicit saved preview preferences are retained. Legacy cosmetic consumers remain disabled.");



            updateSystem.UpdateBefore<SignalMidMipCacheRepairSystem, Colossal.IO.AssetDatabase.VirtualTexturing.TextureStreamingSystem>(SystemUpdatePhase.PrefabUpdate);
            BundledAssets.Initialize(this);
            GardenCitySignals.Mod.Initialize(updateSystem);
            updateSystem.UpdateBefore<VisualLifecycleSystem,Game.Common.ModificationSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateBefore<SignalLightResetSystem,Game.Rendering.InitializeLightsSystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateAfter<VisualLifecycleCompletionSystem,Game.Effects.CompleteEnabledSystem>(SystemUpdatePhase.Cleanup);
            updateSystem.UpdateBefore<SignalSaveCoordinatorSystem,Game.Serialization.BeginPrefabSerializationSystem>(SystemUpdatePhase.Serialize);
            updateSystem.UpdateAfter<SignalSaveNativeCompletionSystem,Game.Serialization.BeginPrefabSerializationSystem>(SystemUpdatePhase.Serialize);
            updateSystem.UpdateBefore<SignalSaveSerializerEntrySystem,Game.Serialization.SerializerSystem>(SystemUpdatePhase.Serialize);
            updateSystem.UpdateAfter<SignalSaveSerializerExitSystem,Game.Serialization.SerializerSystem>(SystemUpdatePhase.Serialize);
            updateSystem.UpdateAfter<SignalSaveWriterScheduledSystem,Game.Serialization.WriteSystem>(SystemUpdatePhase.Serialize);
            updateSystem.UpdateAt<SignalAssetSaveSystem>(SystemUpdatePhase.Modification5);

            TextureCatalog.EnsureDefaultFolder();

            TextureCatalog.InstallBundled();
            try { SurfaceTextureCatalog.InstallBundled(); }
            catch (System.IO.IOException error) { Log.Warn("Included surface textures unavailable; road upgrades remain enabled: " + error.Message); }

            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(TextureCatalog.DefaultFolder, "Decals"));

            var icons = System.IO.Path.Combine(TextureCatalog.DefaultFolder, "UI"); System.IO.Directory.CreateDirectory(icons);

            const string iconPrefix="MedianGrassReplacement.";
            foreach(var resource in typeof(Mod).Assembly.GetManifestResourceNames())
            {
                if(!resource.StartsWith(iconPrefix,StringComparison.Ordinal)||!resource.EndsWith(".svg",StringComparison.OrdinalIgnoreCase))continue;
                string name=resource.Substring(iconPrefix.Length);
                BundledAssets.Install(resource,System.IO.Path.Combine(icons,name));
            }

            Colossal.UI.UIManager.defaultUISystem.AddHostLocation("mgr-icons", icons, shouldWatch: false);

            var defaults = new Setting(this);

            Settings = new Setting(this);



            // Settings object must exist before locale IDs are generated.

            // Translation sources are registered after persisted language settings are loaded.



            AssetDatabase.global.LoadSettings(ModId, Settings, defaults);
            Settings.MigrateSurfaceDefaults();
            LocalizationService.Register(GameManager.instance?.localizationManager, Settings);

            Settings.RegisterInOptionsUI();



            // Discovery is tied to the renderer's network-composition refresh path:

            // 1) after BatchRendererSystem, observe only committed road edges whose

            //    composition has reached the transient BatchesUpdated state;

            // 2) coalesce the entire renderer-refresh burst behind a short quiet window;

            // 3) perform one RoadGrass material lookup after the burst settles.

            // No retry timer, filesystem watcher, or continuous material scan.

            // PrefabUpdate is demand-driven and can precede city preload. Resolve
            // the visual palette in the live road pipeline, before emission.
            updateSystem.UpdateBefore<MarkingThemeSystem, YellowPaintDefinitionSystem>(SystemUpdatePhase.Modification5);
            updateSystem.UpdateBefore<YellowPaintDefinitionSystem,CurbLineEmissionSystem>(SystemUpdatePhase.Modification5);

            updateSystem.UpdateBefore<GeneratedPaintOwnerCleanupSystem, Game.Simulation.GroundHeightSystem>(SystemUpdatePhase.Modification2);

            updateSystem.UpdateAt<CrosswalkUpgradeSystem>(SystemUpdatePhase.PrefabUpdate);

            updateSystem.UpdateAt<UpgradeAssetsSystem>(SystemUpdatePhase.PrefabUpdate);

            updateSystem.UpdateBefore<DedicatedSurfaceSystem, Game.Prefabs.PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateAt<DedicatedSurfaceMenuSystem>(SystemUpdatePhase.UIUpdate);

            updateSystem.UpdateBefore<DecalVisibilitySystem, Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);


            updateSystem.UpdateBefore<SignalPrefabRegistrationSystem, Game.Prefabs.PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateBefore<FarSideLightSystem,GeneratedVisualRegistrationSystem>(SystemUpdatePhase.Modification5);
            updateSystem.UpdateAfter<SignalWireSystem,FarSideLightSystem>(SystemUpdatePhase.Modification5);
            updateSystem.UpdateAt<SignalStreetNameSystem>(SystemUpdatePhase.Modification5);


            

            updateSystem.UpdateBefore<CurbDecalSystem, Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);

            updateSystem.UpdateBefore<RoadAttachmentTransferSystem,Game.Tools.ApplyNetSystem>(SystemUpdatePhase.ApplyTool);

            updateSystem.UpdateBefore<VisualPowerlineGuardSystem,Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);

            updateSystem.UpdateBefore<RoadAttachmentCommitSystem,Game.Net.SecondaryLaneSystem>(SystemUpdatePhase.Modification4B);

            // Replacement secondary producers may run after the disabled native
            // anchor or as a separate UpdateAt entry. Consume their completed
            // phase work once, before its barrier, rather than as a sibling.
            updateSystem.UpdateBefore<VisualPowerlineSystem,Game.Common.ModificationBarrier4B>(SystemUpdatePhase.Modification4B);

            updateSystem.UpdateBefore<GeneratedVisualRegistrationSystem,Game.Objects.SearchSystem>(SystemUpdatePhase.Modification5);
            updateSystem.UpdateBefore<GeneratedVisualRegistrationSystem,CurbLineEmissionSystem>(SystemUpdatePhase.Modification5);


            WorldToolSetup(updateSystem);

            updateSystem.UpdateAfter<RoadGrassCompositionTriggerSystem, BatchRendererSystem>(SystemUpdatePhase.Rendering);

            updateSystem.UpdateAfter<GlobalSurfaceMaterialSystem, RoadGrassCompositionTriggerSystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateAfter<QuayBikeMaterialSystem,GlobalSurfaceMaterialSystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateAfter<QuayBikeMaterialSystem,Game.Rendering.ManagedBatchSystem>(SystemUpdatePhase.Rendering);

            updateSystem.UpdateAfter<MedianMaterialSystem, RoadGrassCompositionTriggerSystem>(SystemUpdatePhase.Rendering);

            // Object placement commits before ApplyNetSystem too. Restore all
            // temporary cosmetic refs once, before the native apply phase starts.
            
            // Restore matching keys after native geometry, before components/
            // lane production. Another mod's before-Lane sibling otherwise has
            // no guaranteed order against our own before-Lane sibling.
            updateSystem.UpdateBefore<CrosswalkVisualRestoreSystem, Game.Net.NetComponentsSystem>(SystemUpdatePhase.Modification4);
            updateSystem.UpdateBefore<FixedParkingRestoreSystem, Game.Net.NetComponentsSystem>(SystemUpdatePhase.Modification4);

            updateSystem.UpdateAfter<FixedParkingSystem, Game.Net.SecondaryLaneReferencesSystem>(SystemUpdatePhase.Modification5);

            updateSystem.UpdateAfter<CurbLineEmissionSystem, FixedParkingSystem>(SystemUpdatePhase.Modification5);

            updateSystem.UpdateBefore<StreetLightingSystem,Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);
            updateSystem.UpdateBefore<JunctionMarkingSystem, Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);

            updateSystem.UpdateAfter<IntersectionPaintSystem, JunctionMarkingSystem>(SystemUpdatePhase.PreCulling);
            updateSystem.UpdateBefore<VisualMutationBoundarySystem,Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);

            







            // Settings have now finished loading. From this point forward, an

            // actual dropdown selection change may trigger a one-shot apply.

            updateSystem.UpdateAt<SaveRecoverySystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<SettingsSaveSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<CurbsidePanelSystem>(SystemUpdatePhase.UIUpdate);

            Settings.EnableRuntimeEvents();
            ModDiagnostics.RuntimeCosts.FrameNumber = DiagnosticFrameNumber;

            NativeLaneVisibilityOwnership.Install(updateSystem.World);

            GameManager.instance.onGameSaveLoad += SaveState;

        }



        private static void WorldToolSetup(UpdateSystem updateSystem)

        {

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;

            var tools = world.GetOrCreateSystemManaged<Game.Tools.ToolSystem>().tools;

            var parking = world.GetOrCreateSystemManaged<ParkingUpgradeTool>(); tools.Remove(parking); tools.Insert(0, parking);

            updateSystem.UpdateAt<ParkingUpgradeTool>(SystemUpdatePhase.ToolUpdate);
            var splitter=world.GetOrCreateSystemManaged<NodeSplitTool>();tools.Remove(splitter);tools.Insert(0,splitter);
            updateSystem.UpdateAt<NodeSplitTool>(SystemUpdatePhase.ToolUpdate);

            var decals = world.GetOrCreateSystemManaged<DecalUpgradeTool>(); tools.Remove(decals); tools.Insert(0, decals);

            updateSystem.UpdateAt<DecalUpgradeTool>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<ApproachStopTool>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateBefore<GeneratedVisualRetirementSystem,Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);
            updateSystem.UpdateBefore<ApproachStopSystem,Game.Net.LaneOverlapSystem>(SystemUpdatePhase.Modification4B);

            var nodes = world.GetOrCreateSystemManaged<NodeLineUpgradeTool>(); tools.Remove(nodes); tools.Insert(0, nodes);

            updateSystem.UpdateAt<NodeLineUpgradeTool>(SystemUpdatePhase.ToolUpdate);

            var farSide = world.GetOrCreateSystemManaged<FarSideLightUpgradeTool>(); tools.Remove(farSide); tools.Insert(0, farSide);

            updateSystem.UpdateAt<FarSideLightUpgradeTool>(SystemUpdatePhase.ToolUpdate);

            var crossing = world.GetOrCreateSystemManaged<LineCrosswalkLaunchTool>(); tools.Remove(crossing); tools.Insert(0, crossing);

            updateSystem.UpdateAt<LineCrosswalkLaunchTool>(SystemUpdatePhase.ToolUpdate);

            updateSystem.UpdateBefore<InvisibleCrosswalkInputSystem,Game.Tools.NetToolSystem>(SystemUpdatePhase.ToolUpdate);

            updateSystem.UpdateAfter<NativeCrosswalkDragSystem, Game.Tools.ToolOutputBarrier>(SystemUpdatePhase.ToolUpdate);
            try
            {
                var drag = world.GetOrCreateSystemManaged<NativeCrosswalkDragSystem>();
                if (!NativeCrosswalkDragHooks.Install(drag)) Log.Warn("Native Curbside crosswalk dragging unavailable on this runtime.");
            }
            catch (Exception error) { Log.Warn("Native drag hooks not loaded: " + error.Message); }

        }



        public void OnDispose()

        {
            SettingsSaveSystem.Flush();
            AppDomain.CurrentDomain.SetData("Curbside.Active.v1",false);

            if(GameManager.instance != null) GameManager.instance.onGameSaveLoad -= SaveState;

            SignalSaveAnimationGuard.End();Saving = false;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;

            if (world != null && !world.IsCreated) world = null;
            try { NativeCrosswalkDragHooks.Uninstall(); }
            catch (Exception error) { Log.Error(error, "Could not release native crosswalk handoff."); }

            // A disposal during an exceptional save must release temporary
            // serializer exclusion before normal ownership queries run.
            try { world?.GetExistingSystemManaged<GeneratedVisualSaveSystem>()?.EndSaveWindow(); }
            catch (Exception error) { Log.Error(error, "Could not release the marking serialization scope during disposal."); }

            Cleanup(() => world?.GetExistingSystemManaged<NativeCrosswalkResetSystem>()?.ReleaseInput());

            Cleanup(() => world?.GetExistingSystemManaged<CrosswalkUpgradeSystem>()?.DetachInput());

            Cleanup(() =>

            {

                if (world == null) return;

                var tools = world.GetExistingSystemManaged<Game.Tools.ToolSystem>();

                if (tools != null) for (int i = tools.tools.Count - 1; i >= 0; i--)

                    {

                        var tool = tools.tools[i]; if (tool.GetType().Assembly != typeof(Mod).Assembly) continue;

                        if (tools.activeTool == tool) tools.activeTool = world.GetOrCreateSystemManaged<Game.Tools.DefaultToolSystem>();

                        tools.tools.RemoveAt(i);

                    }

                foreach (var system in world.Systems) if (system.GetType().Assembly == typeof(Mod).Assembly) system.Enabled = false;

            });

            Cleanup(() => world?.GetExistingSystemManaged<QuayBikeMaterialSystem>()?.Restore());
            Cleanup(() => world?.GetExistingSystemManaged<MarkingPreviewSystem>()?.Restore());
            Cleanup(() => world?.GetExistingSystemManaged<SignalPreviewSystem>()?.Restore());
            Cleanup(() => world?.GetExistingSystemManaged<PreviewPaintInheritanceSystem>()?.Restore());
            Cleanup(() => world?.GetExistingSystemManaged<CrosswalkAppearancePreviewSystem>()?.Restore());
            try { world?.GetExistingSystemManaged<TemporaryPaintVisibilitySystem>()?.RestoreAll(); }
            catch (Exception error) { Log.Warn("Temporary paint visibility cleanup could not complete: " + error.Message); }
            Cleanup(() => world?.GetExistingSystemManaged<SignalWireSystem>()?.Clear());
            Cleanup(() => world?.GetExistingSystemManaged<DecalVisibilitySystem>()?.Restore());

            Cleanup(() => world?.GetExistingSystemManaged<FixedParkingSystem>()?.RestoreAndDestroy(true));

            Cleanup(() => world?.GetExistingSystemManaged<CurbLineEmissionSystem>()?.ClearOwned());

            Cleanup(() => world?.GetExistingSystemManaged<JunctionMarkingSystem>()?.Restore());

            Cleanup(() => world?.GetExistingSystemManaged<StreetLightingSystem>()?.Restore());
            Cleanup(() => world?.GetExistingSystemManaged<IntersectionPaintSystem>()?.Restore());
            Cleanup(() => world?.GetExistingSystemManaged<CrosswalkVisualSystem>()?.RestoreAll());

            Cleanup(() => world?.GetExistingSystemManaged<CurbDecalSystem>()?.Clear());

            Cleanup(() => world?.GetExistingSystemManaged<VisualPowerlineSystem>()?.DisposeResources());

            Cleanup(() => world?.GetExistingSystemManaged<FarSideLightSystem>()?.Restore());
            Cleanup(() => world?.GetExistingSystemManaged<SignalAssetSaveSystem>()?.RevertToVanilla());
            Cleanup(() => world?.GetExistingSystemManaged<VisualLifecycleSystem>()?.PrepareForSerialization());
            Cleanup(NativeLaneVisibilityOwnership.Restore);
            try { if (world != null) CurbsideChangePipeline.Reset(world); }
            catch (Exception error) { Log.Error(error, "Could not clear shared road-change state during disposal."); }
            AppDomain.CurrentDomain.SetData("Curbside.GardenCitySignals.ActiveModels.v1",false);
            Cleanup(() => world?.GetExistingSystemManaged<SignalStreetNameSystem>()?.Clear());

            Cleanup(() => world?.GetExistingSystemManaged<GlobalSurfaceMaterialSystem>()?.Restore(true));
            Cleanup(() => MedianMaterialSystem.RestoreNow());

            SettingsSaveSystem.Flush();

            Cleanup(() => Settings?.UnregisterInOptionsUI());

            Cleanup(LocalizationService.Dispose);
            Cleanup(() => world?.GetExistingSystemManaged<NativeSignalProjectionSystem>()?.ReleaseAndReset());
            Cleanup(() => world?.GetExistingSystemManaged<NativePaintProjectionSystem>()?.ReleaseAndReset());
            Settings = null;

            Log.Info("Disposed The Curbside Mod.");

        }

        private static void Cleanup(Action action)

        {

            try { action(); } catch (Exception error) { Log.Warn("Cleanup could not complete: " + error.Message); }

        }



    }

}






