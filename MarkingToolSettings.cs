using Game.Settings;
using MedianGrassReplacement.Systems;
namespace MedianGrassReplacement
{
    public sealed partial class Setting
    {
        private string _markingToolColor="Yellow";
        [Colossal.Json.Exclude][SettingsUISection(kMainTab,kAppearanceGroup)]
        [SettingsUIDropdown(typeof(Setting),nameof(GetAutomaticThemeChoices))]
        [SettingsUIDisableByCondition(typeof(Setting),nameof(AutomaticMarkingControlsDisabled))]
        public string AutomaticMarkingTheme
        {
            get=>FollowCityMarkingTheme?"City":MarkingToolColor=="White"?"EU":"NA";
            set{if(value=="City")FollowCityMarkingTheme=true;else if(value=="EU"||value=="NA"){FollowCityMarkingTheme=false;MarkingToolColor=value=="EU"?"White":"Yellow";}}
        }
        public Game.UI.Widgets.DropdownItem<string>[] GetAutomaticThemeChoices()=>new[]{
            new Game.UI.Widgets.DropdownItem<string>{value="City",displayName=Localization.LocalizationService.Localized("Map theme (default)")},
            new Game.UI.Widgets.DropdownItem<string>{value="EU",displayName=Localization.LocalizationService.Localized("European")},
            new Game.UI.Widgets.DropdownItem<string>{value="NA",displayName=Localization.LocalizationService.Localized("North American")}};

        // Serialized colour is independent of the per-section single/double choice.
        [SettingsUIHidden]
        public string MarkingToolColor
        {
            get=>_markingToolColor;
            set
            {
                string next=value=="White"?"White":"Yellow";
                if(next==_markingToolColor)return;
                _markingToolColor=next;
                if(_runtimeEventsEnabled){ApplyAndSave();CurbLineEmissionSystem.RequestRoadRefresh();IntersectionPaintSystem.RequestRefresh();}
            }
        }
        [Colossal.Json.Exclude][SettingsUIHidden]
        public bool EuropeanMarkings {get=>FollowCityMarkingTheme?MarkingThemeSystem.CityEuropean:MarkingToolColor=="White";set{if(_runtimeEventsEnabled)FollowCityMarkingTheme=false;MarkingToolColor=value?"White":"Yellow";}}
        private bool _followCityMarkingTheme=true;
        [SettingsUIHidden]
        public bool FollowCityMarkingTheme {get=>_followCityMarkingTheme;set{if(value==_followCityMarkingTheme)return;_followCityMarkingTheme=value;RefreshTheme();}}
        private bool _themeAssetEdgeColors=true;
        // Following the city governs all automatic paint. Preserve the stored
        // manual edge preference for when the player opts out of city theming.
        [Colossal.Json.Exclude] internal bool AutomaticThemeEdges=>FollowCityMarkingTheme||ThemeAssetEdgeColors;
        [SettingsUIHidden]
        public bool ThemeAssetEdgeColors {get=>_themeAssetEdgeColors;set{if(value==_themeAssetEdgeColors)return;_themeAssetEdgeColors=value;RefreshTheme();}}
        private void RefreshTheme(){if(!_runtimeEventsEnabled)return;ApplyAndSave();CurbLineEmissionSystem.RequestRoadRefresh();IntersectionPaintSystem.RequestRefresh();JunctionMarkingSystem.RequestNodeRefresh();}
        [Colossal.Json.Exclude][SettingsUIHidden] public bool YellowToolPaint=>!EuropeanMarkings;
    }
}
