param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop';Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll);$n=0
function T($name){$a.MainModule.Types|Where-Object Name -eq $name}
function M($type,$name){(T $type).Methods|Where-Object Name -eq $name}
function Calls($method){@($method.Body.Instructions|ForEach-Object Operand|Where-Object {$_ -is [Mono.Cecil.MethodReference]}|ForEach-Object FullName)}
function Check($ok,$why){if(-not $ok){throw $why};$script:n++}
try{
 $defaults=M 'Setting' 'SetDefaults';$setback=@($defaults.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq '_parkingIntersectionSetback'});Check ($setback.Count -eq 1 -and $setback[0].Previous.Operand -eq 6.5) 'Captured 6.5 m parking setback default missing'

 foreach($p in @('GlobalRoadTexture','GlobalSidewalkTexture')){$property=(T 'Setting').Properties|Where-Object Name -eq $p;Check (@($property.CustomAttributes|Where-Object {$_.AttributeType.Name -eq 'SettingsUIDropdownAttribute'}).Count -eq 1) 'Missing direct global texture selector'}
 foreach($p in @('RoadSurfaceFolder','SidewalkSurfaceFolder','RoadTextureA','RoadTextureB','SidewalkTextureA','SidewalkTextureB')){$property=(T 'Setting').Properties|Where-Object Name -eq $p;Check (@($property.CustomAttributes|Where-Object {$_.AttributeType.Name -eq 'SettingsUIHiddenAttribute'}).Count -eq 1) 'Legacy surface control must be hidden'}
 foreach($pair in @(@('SegmentUpgradeSystem','Apply'),@('SegmentUpgradeSystem','ApplyNode'))){Check (@(Calls (M $pair[0] $pair[1])|Where-Object {$_ -like '*::ClearPlacedLines*'}).Count -eq 1) 'Visibility does not clear matching placed intent'}
 foreach($name in @('AddParkingMarks','ParkingUpgradePreview')){Check (@(Calls (M 'CurbLineEmissionSystem' $name)|Where-Object {$_ -like '*::ParkingPaintAvailable*'}).Count -eq 1) 'Parking preview/render ignores setback availability'}
 Check (@(Calls (M 'CurbLineEmissionSystem' 'ParkingHatchPlan')|Where-Object {$_ -like '*ParkingSetback::TryLimits*'}).Count -eq 1) 'Hatches do not share native setback limits'
 Check (@(Calls (M 'CurbLineEmissionSystem' 'BuildingAccess')|Where-Object {$_ -like '*HasComponent<Game.Buildings.CommercialProperty>*'}).Count -eq 1) 'Entrance hatching not restricted to commercial'
 Check (@(Calls (M 'CurbLineEmissionSystem' 'HighwayEdgePlan')|Where-Object {$_ -like '*::HighwayRoute*'}).Count -eq 2) 'Highway transition has no native route guard'
 Check (@(Calls (M 'CurbLineEmissionSystem' 'ApplyEdge')|Where-Object {$_ -like '*::JunctionEdgePreview(Unity.Entities.Entity,System.Byte)*'}).Count -eq 1) 'Reapply must validate requested edge style rather than previous removal state'
 Check (@(Calls (M 'CurbLineEmissionSystem' 'AddNodeDividers')|Where-Object {$_ -like '*::NodeDividerPreview*'}).Count -eq 1) 'Node divider style ignored'
 $setAlley=@((M 'Setting' 'SetDefaults').Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'set_AlleyWhiteEdges'});Check ($setAlley.Count -eq 1 -and $setAlley[0].Previous.OpCode.Name -eq 'ldc.i4.0') 'Automatic alley edges must default off'
 $hover=Calls (M 'DecalUpgradeTool' 'HandleHoverLine');foreach($name in @('Pick','ApplyEdge','ApplyNodeCenter','TryCenterUpgradePlan')){Check (@($hover|Where-Object {$_ -like ('*::'+$name+'*')}).Count -gt 0) ('Hover selection route missing: '+$name)}
 Check (@((Calls (M 'DecalUpgradeTool' 'OnUpdate'))|Where-Object {$_ -like '*::HandleHoverLine*'}).Count -eq 1) 'Hover preview/application must share one input route'
 Check (@((Calls (M 'IntersectionPaintSystem' 'TryPreviewLines'))|Where-Object {$_ -like '*::JoinInnerCrosswalk*'}).Count -eq 2) 'Crosswalk preview must use inner edge join'
 Check (@((Calls (M 'IntersectionPaintSystem' 'Reconcile'))|Where-Object {$_ -like '*::JoinInnerCrosswalk*'}).Count -eq 1) 'Crosswalk paint must use same inner edge join'
 foreach($method in @('ApplyUv','Apply','SetUv')){Check (@((Calls (M 'GlobalSurfaceMaterialSystem' $method))|Where-Object {$_ -like '*::ReleaseUnusedTextures*'}).Count -eq 0) 'Retired texture scan is only permitted on explicit refresh'}
 Check (@((Calls (M 'GlobalSurfaceMaterialSystem' 'RefreshTextures'))|Where-Object {$_ -like '*::ReleaseUnusedTextures*'}).Count -eq 1) 'Explicit refresh must reclaim unreferenced cached textures'
 Check (@((Calls (M 'Setting' 'set_OpenTextureFolder'))|Where-Object {$_ -like '*Process::Start*'}).Count -eq 1) 'Folder opener missing'
 foreach($name in @('CarLane','PedestrianLane','ParkingLane','TrackLane')){Check ((M 'CurbLineEmissionSystem' 'NativeNodePaintPaths').Body.Instructions.Operand.FullName -match $name) ('Native node source must protect routing lane '+$name)}
 Check (@((M 'CurbLineEmissionSystem' 'CanonicalCenterPairs')).Count -eq 1) 'Automatic pair normalization missing'
 Write-Output "$n compiled selector, visibility precedence, setback, commercial access and highway reapply contracts passed. Native visual checks remain required."
}finally{$a.Dispose()}
