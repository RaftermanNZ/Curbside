using Game.Settings;
using Game.UI;
using Game.UI.Widgets;
namespace MedianGrassReplacement
{
    public sealed partial class Setting
    {
        private bool _alleyWhiteEdges;
        private float _alleyLineOffset=.30f,_alleyLineStopGap=.05f;
        private float _alleyDecalOffset=-.95f,_highwayDecalOffset=-.95f;
        [SettingsUISection(kMainTab,kAlleyGroup)]
        public bool AlleyWhiteEdges {get=>_alleyWhiteEdges;set{if(value==_alleyWhiteEdges)return;_alleyWhiteEdges=value;if(_runtimeEventsEnabled){ApplyAndSave();Systems.CurbLineEmissionSystem.RequestRoadRefresh();}}}
        // Legacy serialized values are accepted, but placement uses the shared edge controls.
        [SettingsUIHidden] public float AlleyLineOffset {get=>_alleyLineOffset;set=>_alleyLineOffset=Clamp(value,.30f,.05f,1.5f);}
        [SettingsUIHidden] public float AlleyLineStopGap {get=>_alleyLineStopGap;set=>_alleyLineStopGap=Clamp(value,.05f,0,5);}
        [SettingsUISection(kTexturesTab,kDecalGroup)]
        [SettingsUISlider(min=-50,max=50,step=.05f,scalarMultiplier=1,unit=Unit.kFloatTwoFractions)]
        public float AlleyDecalOffset {get=>_alleyDecalOffset;set{var next=Clamp(value,-.95f,-50,50);if(next==_alleyDecalOffset)return;_alleyDecalOffset=next;DecalChanged();}}
        [SettingsUISection(kTexturesTab,kDecalGroup)]
        [SettingsUISlider(min=-50,max=50,step=.05f,scalarMultiplier=1,unit=Unit.kFloatTwoFractions)]
        public float HighwayDecalOffset {get=>_highwayDecalOffset;set{var next=Clamp(value,-.95f,-50,50);if(next==_highwayDecalOffset)return;_highwayDecalOffset=next;DecalChanged();}}
    }
}
