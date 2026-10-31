param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll);$n=0
function Check($ok,$why){if(-not $ok){throw $why};$script:n++}
function MT($name){$a.MainModule.Types|Where-Object Name -eq $name}
function M($type,$name){(MT $type).Methods|Where-Object Name -eq $name}
function Calls($method){@($method.Body.Instructions|ForEach-Object Operand|Where-Object {$_ -is [Mono.Cecil.MethodReference]}|ForEach-Object FullName)}
try {
 foreach($name in @('MGRSectionEdges','MGRCenterlineStyle')){
  $type=MT $name
  Check (@($type.Interfaces|Where-Object {$_.InterfaceType.Name -eq 'ISerializable'}).Count -eq 1) "$name loses saved intent"
  Check (@($type.Fields|Where-Object {-not $_.IsStatic -and $_.FieldType.FullName -ne 'System.Byte'}).Count -eq 0) "$name saves an asset/entity dependency"
 }
 $collector=(MT 'CurbLineEmissionSystem').Methods|Where-Object {$_.HasBody -and @(Calls $_|Where-Object {$_ -like '*::AddCenterUpgrade*'}).Count -gt 0}
 Check (@($collector).Count -eq 1) 'No unique road marking collector'
 Check (@(Calls $collector|Where-Object {$_ -like '*SegmentUpgradePolicy::Hides*'}).Count -eq 0) 'Native visibility still erases Curbside additions'
 Check (@(Calls (M 'CurbLineEmissionSystem' 'AddCenterUpgrade')|Where-Object {$_ -like '*CenterlineVisibility::Hidden*'}).Count -eq 0) 'Native center hiding erases explicit median/center upgrade'
 foreach($name in @('DecalUpgradeTool','ParkingUpgradeTool','NodeLineUpgradeTool','FarSideLightUpgradeTool','ApproachStopTool','NodeSplitTool')){
  Check (@(Calls (M $name 'OnUpdate')|Where-Object {$_ -like '*UpgradeInputPolicy::CancelPressed*'}).Count -eq 1) "$name does not distinguish Escape from right-click"
 }
 Check (@(Calls (M 'Setting' 'get_YellowToolPaint')|Where-Object {$_ -like '*::get_EuropeanMarkings*'}).Count -eq 1) 'Explicit paint ignores resolved theme'
 Check (@(Calls (M 'UpgradePreviewSystem' 'OnUpdate')|Where-Object {$_ -like '*::get_MarkingToolColor*'}).Count -eq 0) 'Preview uses a different theme source'
 Check (@(Calls (M 'MarkingThemeSystem' 'OnUpdate')|Where-Object {$_ -like '*CityConfigurationSystem::get_defaultTheme*'}).Count -eq 1) 'Theme is not sourced from native city configuration'
 Check (@(Calls (M 'NodeSplitTool' 'ValidSplitPreview')|Where-Object {$_ -like '*NodeSplitPolicy::IsSplitNode*'}).Count -eq 1) 'Native replacement node classification missing'
 Check (@(Calls (M 'NodeSplitTool' 'ValidSplitPreview')|Where-Object {$_ -like '*::GetAllowApply*'}).Count -eq 1) 'Split bypasses native placement checks'
 foreach($entry in @(@('FixedParkingSystem','RestoreAndDestroy'),@('IntersectionPaintSystem','RestoreAll'),@('VisualPowerlineSystem','RestorePoleDefinitions'),@('StreetLightingSystem','Restore'))){
  Check (@(Calls (M $entry[0] 'PreSerialize')|Where-Object {$_ -like ('*::'+$entry[1]+'*')}).Count -gt 0) ('Save cleanup missing '+$entry[0])
 }
 $calls=@((MT 'Mod').Methods|Where-Object HasBody|ForEach-Object {Calls $_})
 foreach($type in @('FixedParkingSystem','IntersectionPaintSystem','VisualPowerlineSystem','GeneratedVisualSaveSystem','StreetLightingSystem')){
  Check (@($calls|Where-Object {$_ -like ('*PreSerialize*'+$type+'>*BeginPrefabSerializationSystem*')}).Count -eq 1) ("Cleanup not ordered before prefab serialization: "+$type)
 }
 Check (@(Calls (M 'RoadAttachmentTransferSystem' 'ApplySelection')|Where-Object {$_ -like '*SetComponentData<*MGRSectionEdges*'}).Count -eq 1) 'Section edge intent lost on road replacement'
 Check (@(Calls (M 'SegmentUpgradeSystem' 'Apply')|Where-Object {$_ -like '*Remove<*MGRSectionEdges*'}).Count -eq 1) 'Restore tools miss section edge choice'
 Write-Output "$n unified visibility/theme/input and saved-intent/lifecycle contracts passed. Disable-and-reload must still be tested in-game."
}finally{$a.Dispose()}
