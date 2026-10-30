using Game.Settings;
using MedianGrassReplacement.Systems;
namespace MedianGrassReplacement
{
    public sealed partial class Setting
    {
        private string _markingToolColor="Yellow";
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
        [Colossal.Json.Exclude][SettingsUISection(kMainTab,kMedianGroup)]
        public bool EuropeanMarkings {get=>FollowCityMarkingTheme?MarkingThemeSystem.CityEuropean:MarkingToolColor=="White";set{if(_runtimeEventsEnabled)FollowCityMarkingTheme=false;MarkingToolColor=value?"White":"Yellow";}}
        private bool _followCityMarkingTheme=true;
        [SettingsUISection(kMainTab,kMedianGroup)]
        public bool FollowCityMarkingTheme {get=>_followCityMarkingTheme;set{if(value==_followCityMarkingTheme)return;_followCityMarkingTheme=value;RefreshTheme();}}
        private bool _themeAssetEdgeColors=true;
        [SettingsUISection(kMainTab,kMedianGroup)]
        public bool ThemeAssetEdgeColors {get=>_themeAssetEdgeColors;set{if(value==_themeAssetEdgeColors)return;_themeAssetEdgeColors=value;RefreshTheme();}}
        private void RefreshTheme(){if(!_runtimeEventsEnabled)return;ApplyAndSave();CurbLineEmissionSystem.RequestRoadRefresh();IntersectionPaintSystem.RequestRefresh();JunctionMarkingSystem.RequestNodeRefresh();}
        [Colossal.Json.Exclude][SettingsUIHidden] public bool YellowToolPaint=>!EuropeanMarkings;
    }
}
