using Game.Settings;
using MedianGrassReplacement.Systems;
namespace MedianGrassReplacement
{
    public sealed partial class Setting
    {
        public const string kCompatibilityGroup = "Compatibility", kHighwayControlGroup = "HighwayControls";
        private bool _disableAutomaticMarkings, _disableAutomaticHighwayMarkings, _disableAutomaticLineAdjustments, _disableTextureReplacement;
        private bool _preserveVanillaJunctionLines;
        [SettingsUIHidden]
        public bool PreserveVanillaJunctionLines { get=>_preserveVanillaJunctionLines; set {if(value==_preserveVanillaJunctionLines)return;_preserveVanillaJunctionLines=value;AutomaticChanged();} }
        [Colossal.Json.Exclude][SettingsUIHidden]
        public string JunctionMarkingStatus => Localization.LocalizationService.Text(
            !DisableAutomaticMarkings?"Off: existing automatic marking behavior is retained.":
            DisableAutomaticMarkings?"Vanilla road layout retained. Recoloring is blocked by Disable automatic road lines (Advanced). Manual upgrades still apply.":
            DisableAutomaticLineAdjustments?"Vanilla road layout retained. Recoloring is blocked by Disable automatic line adjustments (Advanced). Manual upgrades still apply.":
            DisableAutomaticHighwayMarkings?"Vanilla road layout retained. Ordinary-road colors remain active; highway recoloring is blocked by Disable automatic highway lines (Highway markings). Manual upgrades still apply.":
            "Vanilla road layout retained; eligible markings follow the current color theme. Manual upgrades still apply.");
        [SettingsUIHidden]
        public bool DisableAutomaticMarkings { get=>_disableAutomaticMarkings||_preserveVanillaJunctionLines; set {if(value==_disableAutomaticMarkings)return;_disableAutomaticMarkings=value;AutomaticChanged();} }
        [Colossal.Json.Exclude][SettingsUISection(kMainTab,kAppearanceGroup)]
        public bool UseVanillaAutomaticMarkings
        {
            get=>DisableAutomaticMarkings;
            set{bool changed=value!=DisableAutomaticMarkings;_preserveVanillaJunctionLines=false;_disableAutomaticMarkings=value;if(changed)AutomaticChanged();}
        }
        [SettingsUIDisableByCondition(typeof(Setting),nameof(AutomaticMarkingControlsDisabled))]
        [SettingsUISection(kHighwaysTab,kHighwayControlGroup)]
        public bool DisableAutomaticHighwayMarkings { get=>_disableAutomaticHighwayMarkings; set {if(value==_disableAutomaticHighwayMarkings)return;_disableAutomaticHighwayMarkings=value;AutomaticChanged();} }
        [SettingsUIDisableByCondition(typeof(Setting),nameof(AutomaticMarkingControlsDisabled))]
        [SettingsUISection(kAdvancedTab,kCompatibilityGroup)]
        public bool DisableAutomaticLineAdjustments { get=>_disableAutomaticLineAdjustments; set {if(value==_disableAutomaticLineAdjustments)return;_disableAutomaticLineAdjustments=value;AutomaticChanged();} }
        [SettingsUISection(kTexturesTab,kCompatibilityGroup)]
        public bool DisableTextureReplacement { get=>_disableTextureReplacement; set {if(value==_disableTextureReplacement)return;_disableTextureReplacement=value;if(_runtimeEventsEnabled){ApplyAndSave();MedianMaterialSystem.RequestRefresh("surface replacements toggled");GlobalSurfaceMaterialSystem.RequestRefresh();}} }
        public bool AutomaticMarkingControlsDisabled()=>UseVanillaAutomaticMarkings;
        private void AutomaticChanged(){if(!_runtimeEventsEnabled)return;ApplyAndSave();CurbLineEmissionSystem.RequestRoadRefresh();IntersectionPaintSystem.RequestRefresh();JunctionMarkingSystem.RequestRefresh();}
    }
}
