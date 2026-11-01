using Colossal.Json;using Game.Settings;using Game.UI.Widgets;using Game.UI;using MedianGrassReplacement.Systems;
namespace MedianGrassReplacement
{
    public sealed partial class Setting
    {
        public const string kSurfaceGroup="SurfaceSlots";
        [SettingsUIHidden]public int SurfaceDefaultVersion{get;set;}
        [SettingsUIHidden]public int GlobalTextureSelectionVersion{get;set;}
        // Read legacy build preferences once; none of these fields are local upgrade controls.
        [SettingsUIHidden]public string RoadSurfaceFolder{get;set;}
        [SettingsUIHidden]public string SidewalkSurfaceFolder{get;set;}
        [SettingsUIHidden]public string RoadTextureA{get;set;}=SurfaceTextureCatalog.BundledRoad;
        [SettingsUIHidden]public string RoadTextureB{get;set;}="";
        [SettingsUIHidden]public string SidewalkTextureA{get;set;}=SurfaceTextureCatalog.BundledSidewalk;
        [SettingsUIHidden]public string SidewalkTextureB{get;set;}="";
        [SettingsUIHidden]public int GlobalRoadSurface{get;set;}=1;
        [SettingsUIHidden]public int GlobalSidewalkSurface{get;set;}=1;
        private string _globalRoadTexture=SurfaceTextureCatalog.BundledRoad,_globalSidewalkTexture=SurfaceTextureCatalog.BundledSidewalk;

        [SettingsUIDropdown(typeof(Setting),nameof(GetGlobalTextureChoices))][SettingsUIValueVersion(typeof(Setting),nameof(GetSurfaceVersion))][SettingsUISection(kTexturesTab,kSurfaceGroup)]public string GlobalRoadTexture{get=>_globalRoadTexture;set{string next=value??"";if(next==_globalRoadTexture)return;_globalRoadTexture=next;GlobalSurfaceChanged();}}
        [SettingsUIDropdown(typeof(Setting),nameof(GetGlobalTextureChoices))][SettingsUIValueVersion(typeof(Setting),nameof(GetSurfaceVersion))][SettingsUISection(kTexturesTab,kSurfaceGroup)]public string GlobalSidewalkTexture{get=>_globalSidewalkTexture;set{string next=value??"";if(next==_globalSidewalkTexture)return;_globalSidewalkTexture=next;GlobalSurfaceChanged();}}
        private bool _surfaceAssets=false;
        [SettingsUISection(kTexturesTab,kSurfaceGroup)]public bool SurfaceTexturesOnAssets{get=>_surfaceAssets;set{if(value==_surfaceAssets)return;_surfaceAssets=value;GlobalSurfaceChanged();}}
        [Exclude][SettingsUIButton][SettingsUISection(kTexturesTab,kLibraryGroup)]public bool OpenTextureFolder{set{if(value){try{string folder=System.IO.Path.GetFullPath(TextureFolder);System.IO.Directory.CreateDirectory(folder);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo{FileName=folder,UseShellExecute=true});}catch(System.Exception error){Mod.Log.Warn("Cannot open texture folder: "+error.Message);}}}}
        public DropdownItem<string>[] GetGlobalTextureChoices()=>SurfaceTextureCatalog.WithCurrent(SurfaceTextureCatalog.Items(TextureFolder),GlobalRoadTexture,GlobalSidewalkTexture);
        public int GetSurfaceVersion()=>SurfaceTextureCatalog.Revision;
        internal void MigrateSurfaceDefaults()
        {
            if(SurfaceDefaultVersion<1){if(string.IsNullOrWhiteSpace(RoadTextureA))RoadTextureA=SurfaceTextureCatalog.BundledRoad;if(string.IsNullOrWhiteSpace(SidewalkTextureA))SidewalkTextureA=SurfaceTextureCatalog.BundledSidewalk;SurfaceDefaultVersion=1;}
            // Require a fresh experimental opt-in when upgrading older settings.
            // Subsequent choices persist normally; this is not a per-load reset.
            if(SurfaceDefaultVersion<2){_surfaceAssets=false;SurfaceDefaultVersion=2;}
            if(GlobalTextureSelectionVersion>=1)return;
            _globalRoadTexture=GlobalSurfacePolicy.LegacyChoice(GlobalRoadSurface,RoadTextureA,RoadTextureB,SurfaceTextureCatalog.BundledRoad);
            _globalSidewalkTexture=GlobalSurfacePolicy.LegacyChoice(GlobalSidewalkSurface,SidewalkTextureA,SidewalkTextureB,SurfaceTextureCatalog.BundledSidewalk);
            GlobalTextureSelectionVersion=1;
        }
        private float _roadSurfaceUv=.16f,_sidewalkSurfaceTiling=1f;
        [SettingsUISlider(min=.01f,max=1f,step=.01f,scalarMultiplier=1,unit=Unit.kFloatTwoFractions)][SettingsUISection(kTexturesTab,kSurfaceGroup)]public float RoadSurfaceUv{get=>_roadSurfaceUv;set{float next=GlobalSurfacePolicy.RoadUv(value);if(next==_roadSurfaceUv)return;_roadSurfaceUv=next;SurfaceUvChanged();}}
        [SettingsUISlider(min=.1f,max=10f,step=.1f,scalarMultiplier=1,unit=Unit.kFloatTwoFractions)][SettingsUISection(kTexturesTab,kSurfaceGroup)]public float SidewalkSurfaceTiling{get=>_sidewalkSurfaceTiling;set{float next=GlobalSurfacePolicy.SidewalkTiling(value);if(next==_sidewalkSurfaceTiling)return;_sidewalkSurfaceTiling=next;SurfaceUvChanged();}}
        private void GlobalSurfaceChanged(){if(_runtimeEventsEnabled){ApplyAndSave();GlobalSurfaceMaterialSystem.RequestRefresh();}}
        private void SurfaceUvChanged(){if(_runtimeEventsEnabled){ApplyAndSave();GlobalSurfaceMaterialSystem.RequestUvRefresh();}}
        [Exclude][SettingsUIButton][SettingsUISection(kTexturesTab,kSurfaceGroup)]public bool ApplySurfaceTextures{set{if(value){SurfaceTextureCatalog.Invalidate();TextureCatalog.Invalidate();ApplyAndSave();GlobalSurfaceMaterialSystem.RequestRefresh();}}}
        [Exclude][SettingsUIMultilineText][SettingsUISection(kTexturesTab,kSurfaceGroup)]public string SurfaceTextureStatus=>Localization.LocalizationService.Text(GlobalSurfaceMaterialSystem.Status);
        [SettingsUISection(kMainTab,kParkingGroup)]public bool HatchAtBuildingEntrances{get=>_hatchAtAccess;set{if(value==_hatchAtAccess)return;_hatchAtAccess=value;ParkingPaintChanged();}}
        [SettingsUISection(kMainTab,kParkingGroup)]public bool CloseParkingAtBuildingEntrances{get=>_closeAtAccess;set{if(value==_closeAtAccess)return;_closeAtAccess=value;ParkingPaintChanged();}}
        private bool _hatchAtAccess,_closeAtAccess;
        private void ParkingPaintChanged(){if(_runtimeEventsEnabled){ApplyAndSave();CurbLineEmissionSystem.RequestRoadRefresh();}}
        private void SetSurfaceDefaults(){_globalRoadTexture=SurfaceTextureCatalog.BundledRoad;_globalSidewalkTexture=SurfaceTextureCatalog.BundledSidewalk;_roadSurfaceUv=.16f;_sidewalkSurfaceTiling=1f;_surfaceAssets=false;GlobalTextureSelectionVersion=_runtimeEventsEnabled?1:0;SurfaceDefaultVersion=2;_hatchAtAccess=true;_closeAtAccess=false;if(_runtimeEventsEnabled){SurfaceTextureCatalog.Invalidate();GlobalSurfaceMaterialSystem.RequestRefresh();CurbLineEmissionSystem.RequestRoadRefresh();}}
    }
}
