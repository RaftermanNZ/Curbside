using Game.Settings;

using Game.UI;

using Game.UI.Widgets;

using MedianGrassReplacement.Systems;

namespace MedianGrassReplacement

{

 public sealed partial class Setting

 {

  private float _medianLineOffset=.30f, _medianLineSetback=.05f;

  private string _medianLineStyle="";

  private string _centerLineStyle="";

  [Colossal.Json.Exclude][SettingsUIHidden] public bool YellowCenterLines {get=>EuropeanMarkings||CenterLineStyle!="Vanilla";set{if(!value)_centerLineStyle="Vanilla";else if(_centerLineStyle=="Vanilla")_centerLineStyle="";}}

  [SettingsUISection(kMainTab,kMedianGroup)]

  [SettingsUIDropdown(typeof(Setting),nameof(GetCenterLineChoices))]

  [SettingsUIValueVersion(typeof(Setting),nameof(GetCurbLineListVersion))]

  public string CenterLineStyle {get=>_centerLineStyle;set{var next=value??"";if(next==_centerLineStyle)return;_centerLineStyle=next;if(_runtimeEventsEnabled)ApplyAndSave();}}

  public DropdownItem<string>[] GetCenterLineChoices(){var choices=CurbLineEmissionSystem.GetLineChoices(CenterLineStyle);var result=new System.Collections.Generic.List<DropdownItem<string>>{new DropdownItem<string>{value="",displayName=MedianGrassReplacement.Localization.LocalizationService.Localized("Automatic (marking theme)")},new DropdownItem<string>{value="Vanilla",displayName=MedianGrassReplacement.Localization.LocalizationService.Localized("Vanilla")}};foreach(var choice in choices)if(choice.value!="Vanilla")result.Add(choice);return result.ToArray();}

  [SettingsUISection(kMainTab,kMedianGroup)] public bool AlignMedianStopBar {get;set;}=true;

  [SettingsUISection(kMainTab,kMedianGroup)] public bool MedianEdgeLines {get;set;}=true;

  [SettingsUISection(kMainTab,kMedianGroup)]

  [SettingsUISlider(min=.05f,max=1.5f,step=.05f,scalarMultiplier=1,unit=Unit.kFloatTwoFractions)]

  public float MedianLineOffset {get=>_medianLineOffset;set{var next=Clamp(value,.30f,.05f,1.5f);if(next==_medianLineOffset)return;_medianLineOffset=next;if(_runtimeEventsEnabled)ApplyAndSave();}}

  [SettingsUISection(kMainTab,kMedianGroup)]

  [SettingsUISlider(min=0,max=5,step=.05f,scalarMultiplier=1,unit=Unit.kFloatTwoFractions)]

  public float MedianLineSetback {get=>_medianLineSetback;set{var next=Clamp(value,.05f,0,5);if(next==_medianLineSetback)return;_medianLineSetback=next;if(_runtimeEventsEnabled)ApplyAndSave();}}

  [SettingsUISection(kMainTab,kMedianGroup)]

  [SettingsUIDropdown(typeof(Setting),nameof(GetMedianLineChoices))]

  [SettingsUIValueVersion(typeof(Setting),nameof(GetCurbLineListVersion))]

  public string MedianLineStyle {get=>_medianLineStyle;set{var next=value??"";if(next==_medianLineStyle)return;_medianLineStyle=next;if(_runtimeEventsEnabled)ApplyAndSave();}}

  public DropdownItem<string>[] GetMedianLineChoices(){var choices=CurbLineEmissionSystem.GetLineChoices(MedianLineStyle);var result=new DropdownItem<string>[choices.Length+1];result[0]=new DropdownItem<string>{value="",displayName=MedianGrassReplacement.Localization.LocalizationService.Localized("Automatic (marking theme)")};System.Array.Copy(choices,0,result,1,choices.Length);return result;}

 }

}
