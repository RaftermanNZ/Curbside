using System;

using Colossal.IO.AssetDatabase;

using Game.Settings;

using Game.UI;

using Game.UI.Widgets;

using MedianGrassReplacement.Systems;

namespace MedianGrassReplacement

{

    public sealed partial class Setting

    {

        public const string kDecalGroup = "CurbsideDecals";

        private bool _decalsEnabled;

        private string _decalFolder, _decalTexture;

        private float _decalRotation=-90f;

        private bool _orientDecalsToCurb = true;

        [SettingsUISection(kTexturesTab,kDecalGroup)] public bool DecalsBothSides { get; set; } = false;

        private int _decalLimit = 512;

        private bool _unlimitedDecals;

        private float _decalOffset = -.95f, _decalSetback = .25f, _decalOverlap = 50f, _decalWidth = 13.25f, _decalLength = 2.6f;

        private void DecalChanged() { if (_runtimeEventsEnabled) { ApplyAndSave(); CurbDecalSystem.RequestRefresh(); } }

        [SettingsUISection(kTexturesTab, kDecalGroup)] public bool CurbDecalsEnabled { get => _decalsEnabled; set { if (_decalsEnabled == value) return; _decalsEnabled = value; DecalChanged(); } }

        [SettingsUIDirectoryPicker][SettingsUISection(kTexturesTab, kDecalGroup)] public string CurbDecalFolder { get => string.IsNullOrWhiteSpace(_decalFolder) ? System.IO.Path.Combine(TextureCatalog.DefaultFolder, "Decals") : _decalFolder; set { string next = TextureCatalog.NormalizeFolder(value); if (next == _decalFolder) return; _decalFolder = next; DecalCatalog.Invalidate(); DecalChanged(); } }

        [SettingsUIDropdown(typeof(Setting), nameof(GetDecalChoices))][SettingsUIValueVersion(typeof(Setting), nameof(GetDecalVersion))][SettingsUISection(kTexturesTab, kDecalGroup)] public string CurbDecalTexture { get => _decalTexture ?? DecalCatalog.BundledValue; set { if (_decalTexture == value) return; _decalTexture = value; DecalChanged(); } }

        public DropdownItem<string>[] GetDecalChoices() => DecalCatalog.Items(CurbDecalFolder);

        public int GetDecalVersion() => DecalCatalog.Revision;

        [SettingsUIButton][SettingsUISection(kTexturesTab, kDecalGroup)] public bool RefreshDecals { set { if (value) { DecalCatalog.Invalidate(); CurbDecalSystem.RequestRefresh(); } } }

        [SettingsUISlider(min = -50f, max = 50f, step = .05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)][SettingsUISection(kTexturesTab, kDecalGroup)] public float CurbDecalOffset { get => _decalOffset; set { float n = Clamp(value, -.95f, -50, 50); if (n == _decalOffset) return; _decalOffset = n; DecalChanged(); } }

        [SettingsUISlider(min = 0f, max = 15f, step = .25f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)][SettingsUISection(kTexturesTab, kDecalGroup)] public float CurbDecalSetback { get => _decalSetback; set { float n = Clamp(value, .25f, 0, 15); if (n == _decalSetback) return; _decalSetback = n; DecalChanged(); } }

        [SettingsUISlider(min = 0f, max = 50f, step = 5f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)][SettingsUISection(kTexturesTab, kDecalGroup)] public float CurbDecalOverlap { get => _decalOverlap; set { float n = Clamp(value, 50, 0, 50); if (n == _decalOverlap) return; _decalOverlap = n; DecalChanged(); } }

        [SettingsUISlider(min = .1f, max = 100f, step = .05f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)][SettingsUISection(kTexturesTab, kDecalGroup)] public float CurbDecalWidth { get => _decalWidth; set { float n = Clamp(value, 13.25f, .1f, 100); if (n == _decalWidth) return; _decalWidth = n; DecalChanged(); } }

        [SettingsUISlider(min = .1f, max = 100f, step = .5f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)][SettingsUISection(kTexturesTab, kDecalGroup)] public float CurbDecalLength { get => _decalLength; set { float n = Clamp(value, 2.6f, .1f, 100); if (n == _decalLength) return; _decalLength = n; DecalChanged(); } }

        [SettingsUISlider(min = -180f, max = 180f, step = 1f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)][SettingsUISection(kTexturesTab, kDecalGroup)] public float CurbDecalRotation { get => _decalRotation; set { float n = Clamp(value, -90, -180, 180); if (n == _decalRotation) return; _decalRotation = n; DecalChanged(); } }

        [SettingsUISection(kTexturesTab,kDecalGroup)]

        public bool UnlimitedDecals { get => _unlimitedDecals; set { if (_unlimitedDecals == value) return; _unlimitedDecals=value; DecalChanged(); } }

        public bool IsUnlimitedDecalMode() => UnlimitedDecals;

        public bool IsLimitedDecalMode() => !UnlimitedDecals;

        [Colossal.Json.Exclude][SettingsUIHideByCondition(typeof(Setting),nameof(IsLimitedDecalMode))][SettingsUISection(kTexturesTab,kDecalGroup)]

        public string UnlimitedDecalWarning => Localization.LocalizationService.Text("Unlimited coverage increases rendering and memory use. Very large coverage may exceed available memory. Per-road and per-frame limits still apply.");

        [SettingsUIDisableByCondition(typeof(Setting),nameof(IsUnlimitedDecalMode))]

        [SettingsUISlider(min = 16, max = 2048, step = 16, scalarMultiplier = 1)][SettingsUISection(kTexturesTab, kDecalGroup)] public int CurbDecalLimit { get => _decalLimit; set { int n = System.Math.Max(16, System.Math.Min(2048, value)); if (n == _decalLimit) return; _decalLimit = n; DecalChanged(); } }

        [SettingsUISection(kTexturesTab, kDecalGroup)] public bool OrientDecalsToCurb { get => _orientDecalsToCurb; set { if (_orientDecalsToCurb == value) return; _orientDecalsToCurb = value; DecalChanged(); } }

        [SettingsUISection(kTexturesTab, kDecalGroup)] public string CurbDecalStatus => Localization.LocalizationService.Text(_decalValueError ?? CurbDecalSystem.Status);

        private void SetDecalDefaults() { _alleyDecalOffset=-.95f; _highwayDecalOffset=-.95f; _decalsEnabled = true; _decalFolder = null; _decalTexture = DecalCatalog.BundledValue; _decalOffset = -.95f; _decalSetback = .25f; _decalOverlap = 50; _decalWidth = 13.25f; _decalLength = 2.6f; _decalRotation = -90; _decalLimit = 2048; _unlimitedDecals=true; _orientDecalsToCurb = true; DecalsBothSides = true; ShowExactDecalValues = false; _decalValueError = null; ClearDecalDrafts(); }

    }

}


