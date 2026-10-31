using System;

using Colossal.IO.AssetDatabase;

using Game.Modding;

using Game.Settings;

using Game.UI;

using Game.UI.Widgets;

using MedianGrassReplacement.Systems;

namespace MedianGrassReplacement

{

    [FileLocation("ModsSettings/MedianGrassReplacement/MedianGrassReplacement")]

    [SettingsUITabOrder(kTexturesTab, kMainTab, kHighwaysTab, kAdvancedTab, kAboutTab)]

    [SettingsUIGroupOrder(kLibraryGroup, kDecalGroup, kSurfaceGroup, kAppearanceGroup, kAlleyGroup, kMedianGroup, kParkingGroup, kCrosswalkGroup, kStopGroup, kMaintenanceGroup, kAboutGroup)]

    [SettingsUIShowGroupName(kLibraryGroup, kDecalGroup, kSurfaceGroup, kAppearanceGroup, kAlleyGroup, kMedianGroup, kParkingGroup, kCrosswalkGroup, kStopGroup, kMaintenanceGroup, kAboutGroup)]

    public sealed partial class Setting : ModSetting

    {

        public const string kMainTab = "Main", kTexturesTab = "Textures", kAdvancedTab = "Advanced", kAboutTab = "About", kHighwaysTab = "Highways";

        public const string kLibraryGroup = "TextureLibrary", kAppearanceGroup = "Appearance", kAlleyGroup="AlleyEdges", kMedianGroup = "MedianLines", kParkingGroup = "Parking", kCrosswalkGroup = "Crosswalks", kStopGroup = "StopLines", kMaintenanceGroup = "Maintenance", kAboutGroup = "About";

        private bool _runtimeEventsEnabled;

        private string _textureFolder, _selectedTexture, _curbLineStyle = "EU Highway Edge Line";

        private float _uvScale = .20f, _curbLineOffset = .30f, _fixedParkingBayLength = 7.5f, _parkingIntersectionSetback = 5f, _stopLineCurbGap = .15f, _curbLineStopGap = .05f, _crosswalkUpgradeWidth = 3f;

        public Setting(IMod mod) : base(mod) { SetDefaults(); }

        public override void ApplyAndSave() { Apply(); if (_runtimeEventsEnabled) SettingsSaveSystem.Request(this); }

        internal void EnableRuntimeEvents() { _runtimeEventsEnabled = true; }

        private static float Clamp(float value, float fallback, float min, float max) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));

        [SettingsUISection(kMainTab, kAppearanceGroup)] public bool CurbWhiteLines { get; set; }

        [SettingsUISection(kMainTab, kAppearanceGroup)] public bool CurbLinesBesideParking { get; set; } = true;

        [SettingsUISlider(min = .05f, max = 1.50f, step = .05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]

        [SettingsUISection(kMainTab, kAppearanceGroup)]

        public float CurbLineOffset { get => _curbLineOffset; set { float next = Clamp(value, .30f, .05f, 1.5f); if (next == _curbLineOffset) return; _curbLineOffset = next; if (_runtimeEventsEnabled) { ApplyAndSave(); CurbLineEmissionSystem.RequestRoadRefresh(); } } }

        [SettingsUIDropdown(typeof(Setting), nameof(GetCurbLineChoices))]

        [SettingsUIValueVersion(typeof(Setting), nameof(GetCurbLineListVersion))]

        [SettingsUISection(kMainTab, kAppearanceGroup)]

        public string CurbLineStyle { get => _curbLineStyle; set { string next = string.IsNullOrWhiteSpace(value) ? "EU Highway Edge Line" : value; if (next == _curbLineStyle) return; _curbLineStyle = next; if (_runtimeEventsEnabled) ApplyAndSave(); } }

        public DropdownItem<string>[] GetCurbLineChoices() => CurbLineEmissionSystem.GetLineChoices(CurbLineStyle);

        public int GetCurbLineListVersion() => CurbLineEmissionSystem.CatalogRevision;

        [SettingsUISlider(min = 7f, max = 8f, step = .5f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]

        [SettingsUISection(kMainTab, kParkingGroup)]

        public float FixedParkingBayLength { get => _fixedParkingBayLength; set { float next = ParkingBayPolicy.Length(value); if (next == _fixedParkingBayLength) return; _fixedParkingBayLength = next; if (_runtimeEventsEnabled) ApplyAndSave(); } }

        [SettingsUISlider(min = 0f, max = 15f, step = .5f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]

        [SettingsUISection(kMainTab, kParkingGroup)]

        public float ParkingIntersectionSetback { get => _parkingIntersectionSetback; set { float next = Clamp(value, 5f, 0f, 15f); if (next == _parkingIntersectionSetback) return; _parkingIntersectionSetback = next; if (_runtimeEventsEnabled) ApplyAndSave(); } }

        [SettingsUISlider(min = 1f, max = 6f, step = .25f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]

        [SettingsUISection(kMainTab, kCrosswalkGroup)]

        public float CrosswalkUpgradeWidth { get => _crosswalkUpgradeWidth; set { float next = Clamp(value, 3f, 1f, 6f); if (next == _crosswalkUpgradeWidth) return; _crosswalkUpgradeWidth = next; if (_runtimeEventsEnabled) { ApplyAndSave(); IntersectionPaintSystem.RequestRefresh(); } } }

        [SettingsUISection(kAdvancedTab, kStopGroup)] public bool LockCurbAndStopLines { get; set; }

        [SettingsUISlider(min = 0f, max = 1.50f, step = .05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]

        [SettingsUISection(kAdvancedTab, kStopGroup)]

        public float StopLineCurbGap { get => _stopLineCurbGap; set { float next = Clamp(value, .15f, 0f, 1.5f); if (next == _stopLineCurbGap) return; _stopLineCurbGap = next; if (_runtimeEventsEnabled) ApplyAndSave(); } }

        [SettingsUISlider(min = 0f, max = 1.50f, step = .05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]

        [SettingsUISection(kAdvancedTab, kStopGroup)]

        public float CurbLineStopGap { get => _curbLineStopGap; set { float next = Clamp(value, .05f, 0f, 1.5f); if (next == _curbLineStopGap) return; _curbLineStopGap = next; if (_runtimeEventsEnabled) ApplyAndSave(); } }

        [SettingsUIDirectoryPicker]

        [SettingsUISection(kTexturesTab, kLibraryGroup)]

        public string TextureFolder { get => string.IsNullOrWhiteSpace(_textureFolder) ? TextureCatalog.DefaultFolder : _textureFolder; set { var next = TextureCatalog.NormalizeFolder(value); if (string.Equals(next, _textureFolder, StringComparison.OrdinalIgnoreCase)) return; _textureFolder = next; TextureCatalog.Invalidate(); SurfaceTextureCatalog.Invalidate(); if (_runtimeEventsEnabled) { ApplyAndSave(); MedianMaterialSystem.RequestRefresh("texture folder changed"); } } }

        [SettingsUIDropdown(typeof(Setting), nameof(GetTextureChoices))]

        [SettingsUIValueVersion(typeof(Setting), nameof(GetTextureListVersion))]

        [SettingsUISection(kTexturesTab, kLibraryGroup)]

        public string SelectedTexture { get => string.IsNullOrWhiteSpace(_selectedTexture) ? TextureCatalog.VanillaValue : _selectedTexture; set { var next = string.IsNullOrWhiteSpace(value) ? TextureCatalog.VanillaValue : value; if (next == _selectedTexture) return; _selectedTexture = next; if (_runtimeEventsEnabled) { ApplyAndSave(); MedianMaterialSystem.RequestRefresh("texture selection changed"); } } }

        public DropdownItem<string>[] GetTextureChoices() => TextureCatalog.GetItems(TextureFolder);

        public int GetTextureListVersion() => TextureCatalog.Revision;

        [SettingsUIButton][SettingsUISection(kTexturesTab, kLibraryGroup)] public bool RefreshTextureList { set { if (value) {TextureCatalog.Invalidate();SurfaceTextureCatalog.Invalidate();} } }

        [SettingsUISlider(min = .05f, max = .50f, step = .01f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]

        [SettingsUISetter(typeof(Setting), nameof(OnUvScaleChanged))]

        [SettingsUISection(kTexturesTab, kLibraryGroup)]

        public float UvScale { get => Clamp(_uvScale, .20f, .05f, .50f); set => _uvScale = Clamp(value, .20f, .05f, .50f); }

        private void OnUvScaleChanged(float value) { if (!_runtimeEventsEnabled) return; _uvScale = Clamp(value, .20f, .05f, .50f); ApplyAndSave(); MedianMaterialSystem.RequestUvScale(_uvScale); }

        [SettingsUIButton]

        [SettingsUISection(kAdvancedTab, kMaintenanceGroup)]

        public bool RefreshRoadLines { set { if (!value) return; IntersectionPaintSystem.RequestRefresh(); FixedParkingSystem.RequestScan(); CurbLineEmissionSystem.RequestManualRefresh(); JunctionMarkingSystem.RequestRefresh(); } }

        [SettingsUIButton][SettingsUISection(kAdvancedTab, kMaintenanceGroup)] public bool RefreshCurbLineStyles { set { if (value) CurbLineEmissionSystem.RequestCatalogRefresh(); } }

        [SettingsUISlider(min=15f,max=80f,step=1f,scalarMultiplier=1,unit=Unit.kFloatTwoFractions)]

        [SettingsUISection(kAdvancedTab,kMaintenanceGroup)] public float PowerlinePoleSpacing {get=>_powerlinePoleSpacing;set=>_powerlinePoleSpacing=Clamp(value,35f,15f,80f);}
        private float _powerlinePoleSpacing=35f;

        [SettingsUISlider(min=4f,max=20f,step=1f,scalarMultiplier=1,unit=Unit.kFloatTwoFractions)]

        [SettingsUISection(kAdvancedTab,kMaintenanceGroup)] public float PowerlineMinimumGap {get=>_powerlineMinimumGap;set=>_powerlineMinimumGap=Clamp(value,10f,4f,20f);}
        private float _powerlineMinimumGap=10f;

        [SettingsUISlider(min=2f,max=15f,step=.5f,scalarMultiplier=1,unit=Unit.kFloatTwoFractions)]

        [SettingsUISection(kAdvancedTab,kMaintenanceGroup)] public float PowerlineCornerSetback {get=>_powerlineCornerSetback;set=>_powerlineCornerSetback=Clamp(value,6f,2f,15f);}
        private float _powerlineCornerSetback=6f;

        [Colossal.Json.Exclude][SettingsUISection(kAdvancedTab, kMaintenanceGroup)] public string PowerlineStatus => Localization.LocalizationService.Text(VisualPowerlineSystem.Status);

        [SettingsUISection(kAboutTab, kAboutGroup)] public string Version => BuildIdentity.Version;

        [SettingsUISection(kAboutTab, kAboutGroup)] public string Function => Localization.LocalizationService.Text("Road markings, crosswalks, parking, textures, roadside decoration and optional traffic signals.");

        [SettingsUISlider(min=5f,max=100f,step=.5f,scalarMultiplier=1,unit=Unit.kFloatTwoFractions)]

        [SettingsUISection(kAdvancedTab,kMaintenanceGroup)] public float PowerlineHeight {get=>_powerlineHeight;set{float next=PoleHeightPolicy.Requested(value);if(next==_powerlineHeight)return;_powerlineHeight=next;if(_runtimeEventsEnabled)ApplyAndSave();}}
        private float _powerlineHeight=10f;

        private void SetDefaultValues(string textureFolder)
        {
            _preserveVanillaJunctionLines=false;
            _disableAutomaticMarkings=false; _disableAutomaticHighwayMarkings=false; _disableAutomaticLineAdjustments=false; _disableTextureReplacement=false; _markingToolColor="Yellow"; FollowCityMarkingTheme=false;ThemeAssetEdgeColors=true; _centerLineStyle=""; AlignMedianStopBar=true; MedianEdgeLines=true; _medianLineOffset=.35f; _medianLineSetback=.05f; _medianLineStyle=""; PowerlinePoleSpacing=35f;PowerlineMinimumGap=10f;PowerlineCornerSetback=6f;PowerlineHeight=10f; SetDecalDefaults(); SetSurfaceDefaults(); _textureFolder = textureFolder; _selectedTexture = TextureCatalog.BundledValue; _uvScale = .10f; AlleyWhiteEdges=false; AlleyLineOffset=.20f; AlleyLineStopGap=0f; CurbWhiteLines = true; CurbLinesBesideParking = false; _curbLineOffset = .25f; _curbLineStyle = "NA Parking Edge Line"; _fixedParkingBayLength = 7f; _parkingIntersectionSetback = 6.5f; _stopLineCurbGap = .15f; _curbLineStopGap = .05f; _crosswalkUpgradeWidth = 2.5f; LockCurbAndStopLines = true;
        }

        public override void SetDefaults()
        {
            // Reset is one settings transaction. Constructors/deserialization must
            // not queue renderer work, and live reset must use the final values.
            bool live = _runtimeEventsEnabled;
            _runtimeEventsEnabled = false;
            try
            {
                SetDefaultValues(TextureCatalog.DefaultFolder);
                if (live) GlobalTextureSelectionVersion = 1;
            }
            finally { _runtimeEventsEnabled = live; }
            if (!live) return;
            TextureCatalog.Invalidate();
            SurfaceTextureCatalog.Invalidate();
            DecalCatalog.Invalidate();
            ApplyAndSave();
            MedianMaterialSystem.RequestRefresh("settings reset");
            GlobalSurfaceMaterialSystem.RequestRefresh();
            CurbDecalSystem.RequestRefresh();
            CurbLineEmissionSystem.RequestRoadRefresh();
            FixedParkingSystem.RequestScan();
            IntersectionPaintSystem.RequestRefresh();
            JunctionMarkingSystem.RequestRefresh();
        }

    }

}
