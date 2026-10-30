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
            Saving = start;
            var world=Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if(start)
            {
                if(world!=null&&world.IsCreated)world.GetExistingSystemManaged<SaveRecoverySystem>()?.Observe();
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



        public void OnLoad(UpdateSystem updateSystem)

        {

            AppDomain.CurrentDomain.SetData("Curbside.Active.v1",true);Log.Info("Loading The Curbside Mod "+BuildIdentity.Label+". Audit hardening candidate: native asset discovery, owned restoration, bounded diagnostics and save-queue failure recovery. Existing reader guards retained; live validation pending.");



            GardenCitySignals.Mod.Initialize(updateSystem);
            updateSystem.UpdateBefore<SignalSaveCoordinatorSystem,Game.Serialization.BeginPrefabSerializationSystem>(SystemUpdatePhase.Serialize);
            updateSystem.UpdateAfter<SignalSaveNativeCompletionSystem,Game.Serialization.BeginPrefabSerializationSystem>(SystemUpdatePhase.Serialize);
            updateSystem.UpdateBefore<SignalSaveSerializerEntrySystem,Game.Serialization.SerializerSystem>(SystemUpdatePhase.Serialize);
            updateSystem.UpdateAfter<SignalSaveSerializerExitSystem,Game.Serialization.SerializerSystem>(SystemUpdatePhase.Serialize);
            updateSystem.UpdateAfter<SignalSaveWriterScheduledSystem,Game.Serialization.WriteSystem>(SystemUpdatePhase.Serialize);
            updateSystem.UpdateAt<SignalAssetSaveSystem>(SystemUpdatePhase.Modification5);

            TextureCatalog.EnsureDefaultFolder();

            TextureCatalog.InstallBundled();
            SurfaceTextureCatalog.InstallBundled();

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

            updateSystem.UpdateBefore<DecalVisibilitySystem, Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);


            updateSystem.UpdateBefore<SignalPrefabRegistrationSystem, Game.Prefabs.PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateAt<FarSideLightSystem>(SystemUpdatePhase.Modification5);
            updateSystem.UpdateAfter<SignalWireSystem,FarSideLightSystem>(SystemUpdatePhase.Modification5);
            updateSystem.UpdateAt<SignalStreetNameSystem>(SystemUpdatePhase.Modification5);


            

            updateSystem.UpdateBefore<CurbDecalSystem, Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);

            updateSystem.UpdateBefore<RoadAttachmentTransferSystem,Game.Tools.ApplyNetSystem>(SystemUpdatePhase.ApplyTool);

            updateSystem.UpdateBefore<VisualPowerlineGuardSystem,Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);

            updateSystem.UpdateBefore<RoadAttachmentCommitSystem,Game.Net.SecondaryLaneSystem>(SystemUpdatePhase.Modification4B);

            updateSystem.UpdateAfter<VisualPowerlineSystem,Game.Net.SecondaryLaneSystem>(SystemUpdatePhase.Modification4B);

            updateSystem.UpdateBefore<GeneratedVisualRegistrationSystem,Game.Objects.SearchSystem>(SystemUpdatePhase.Modification5);


            WorldToolSetup(updateSystem);

            updateSystem.UpdateAfter<RoadGrassCompositionTriggerSystem, BatchRendererSystem>(SystemUpdatePhase.Rendering);

            updateSystem.UpdateAfter<GlobalSurfaceMaterialSystem, RoadGrassCompositionTriggerSystem>(SystemUpdatePhase.Rendering);

            updateSystem.UpdateAfter<MedianMaterialSystem, RoadGrassCompositionTriggerSystem>(SystemUpdatePhase.Rendering);

            updateSystem.UpdateBefore<CrosswalkPreviewRestoreSystem,Game.Tools.ApplyNetSystem>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateBefore<SignalPreviewRestoreSystem,Game.Tools.ApplyNetSystem>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateBefore<SignalPreviewSystem,Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);
            updateSystem.UpdateAfter<CrosswalkPreviewSystem,IntersectionPaintSystem>(SystemUpdatePhase.PreCulling);
            
            updateSystem.UpdateBefore<CrosswalkVisualRestoreSystem, Game.Net.LaneSystem>(SystemUpdatePhase.Modification4);
            updateSystem.UpdateBefore<FixedParkingRestoreSystem, Game.Net.LaneSystem>(SystemUpdatePhase.Modification4);

            updateSystem.UpdateAfter<FixedParkingSystem, Game.Net.SecondaryLaneReferencesSystem>(SystemUpdatePhase.Modification5);

            updateSystem.UpdateAfter<CurbLineEmissionSystem, FixedParkingSystem>(SystemUpdatePhase.Modification5);

            updateSystem.UpdateBefore<StreetLightingSystem,Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);
            updateSystem.UpdateBefore<JunctionMarkingSystem, Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);

            updateSystem.UpdateAfter<IntersectionPaintSystem, JunctionMarkingSystem>(SystemUpdatePhase.PreCulling);

            







            // Settings have now finished loading. From this point forward, an

            // actual dropdown selection change may trigger a one-shot apply.

            updateSystem.UpdateAt<SaveRecoverySystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<SettingsSaveSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<CurbsidePanelSystem>(SystemUpdatePhase.UIUpdate);

            Settings.EnableRuntimeEvents();

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
            updateSystem.UpdateAfter<UpgradePreviewSystem,DecalUpgradeTool>(SystemUpdatePhase.ToolUpdate);
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

            updateSystem.UpdateAfter<NativeCrosswalkResetSystem, Game.Tools.NetToolSystem>(SystemUpdatePhase.ToolUpdate);

        }



        public void OnDispose()

        {
            AppDomain.CurrentDomain.SetData("Curbside.Active.v1",false);

            if(GameManager.instance != null) GameManager.instance.onGameSaveLoad -= SaveState;

            SignalSaveAnimationGuard.End();Saving = false;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;

            if (world != null && !world.IsCreated) world = null;

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

            Cleanup(() => world?.GetExistingSystemManaged<SignalPreviewSystem>()?.Restore());
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
            AppDomain.CurrentDomain.SetData("Curbside.GardenCitySignals.ActiveModels.v1",false);
            Cleanup(() => world?.GetExistingSystemManaged<SignalStreetNameSystem>()?.Clear());

            Cleanup(() => world?.GetExistingSystemManaged<GlobalSurfaceMaterialSystem>()?.Restore(true));
            Cleanup(() => MedianMaterialSystem.RestoreNow());

            SettingsSaveSystem.Flush();

            Cleanup(() => Settings?.UnregisterInOptionsUI());

            Cleanup(LocalizationService.Dispose);
            Settings = null;

            Log.Info("Disposed The Curbside Mod.");

        }

        private static void Cleanup(Action action)

        {

            try { action(); } catch (Exception error) { Log.Warn("Cleanup could not complete: " + error.Message); }

        }



    }

}






