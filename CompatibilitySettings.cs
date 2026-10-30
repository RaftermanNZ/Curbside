using Game.Settings;
using MedianGrassReplacement.Systems;
namespace MedianGrassReplacement
{
    public sealed partial class Setting
    {
        public const string kCompatibilityGroup = "Compatibility", kHighwayControlGroup = "HighwayControls";
        private bool _disableAutomaticMarkings, _disableAutomaticHighwayMarkings, _disableAutomaticLineAdjustments, _disableTextureReplacement;
        [SettingsUISection(kAdvancedTab,kCompatibilityGroup)]
        public bool DisableAutomaticMarkings { get=>_disableAutomaticMarkings; set {if(value==_disableAutomaticMarkings)return;_disableAutomaticMarkings=value;AutomaticChanged();} }
        [SettingsUISection(kHighwaysTab,kHighwayControlGroup)]
        public bool DisableAutomaticHighwayMarkings { get=>_disableAutomaticHighwayMarkings; set {if(value==_disableAutomaticHighwayMarkings)return;_disableAutomaticHighwayMarkings=value;AutomaticChanged();} }
        [SettingsUISection(kAdvancedTab,kCompatibilityGroup)]
        public bool DisableAutomaticLineAdjustments { get=>_disableAutomaticLineAdjustments; set {if(value==_disableAutomaticLineAdjustments)return;_disableAutomaticLineAdjustments=value;AutomaticChanged();} }
        [SettingsUISection(kTexturesTab,kCompatibilityGroup)]
        public bool DisableTextureReplacement { get=>_disableTextureReplacement; set {if(value==_disableTextureReplacement)return;_disableTextureReplacement=value;if(_runtimeEventsEnabled){ApplyAndSave();MedianMaterialSystem.RequestRefresh("surface replacements toggled");GlobalSurfaceMaterialSystem.RequestRefresh();}} }
        private void AutomaticChanged(){if(!_runtimeEventsEnabled)return;ApplyAndSave();CurbLineEmissionSystem.RequestRoadRefresh();IntersectionPaintSystem.RequestRefresh();JunctionMarkingSystem.RequestRefresh();}
    }
}
