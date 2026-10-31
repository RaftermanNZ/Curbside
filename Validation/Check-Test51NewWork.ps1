param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll);$n=0
function Check($ok,$why){if(-not $ok){throw $why};$script:n++}
function MT($name){$a.MainModule.Types|Where-Object Name -eq $name}
function M($type,$name){(MT $type).Methods|Where-Object Name -eq $name}
function Calls($method){@($method.Body.Instructions|ForEach-Object Operand|Where-Object {$_ -is [Mono.Cecil.MethodReference]}|ForEach-Object FullName)}
try{
 $stop=M 'NodeSplitTool' 'OnStopRunning';$calls=Calls $stop
 Check (@($calls|Where-Object {$_ -like '*::ClearDefinitions*' -or $_ -like '*::CreateCommandBuffer*'}).Count -eq 0) 'Tool-stop still records outside allowed barrier phase'
 Check (@($calls|Where-Object {$_ -like '*EntityManager::DestroyEntity(Unity.Entities.EntityQuery)*'}).Count -eq 1) 'Owned definitions not cleaned on tool exit'
 Check (@($calls|Where-Object {$_ -like '*ToolBaseSystem::OnStopRunning*'}).Count -eq 1) 'Native input cleanup missing'
 Check (@($stop.Body.ExceptionHandlers|Where-Object HandlerType -eq 'Finally').Count -eq 1) 'Native action cleanup not protected by finally'
 $calls=Calls (M 'Mod' 'OnLoad')
 Check (@($calls|Where-Object {$_ -like '*::GetManifestResourceNames*'}).Count -eq 1) 'New icons can drift out of registration again'
 Check (@($calls|Where-Object {$_ -like '*BundledAssets::Install*'}).Count -eq 1) 'Embedded icons are not exposed to UI host'
 foreach($entry in @(@('CurbLineEmissionSystem','AddParkingMarks'),@('CurbLineEmissionSystem','ParkingUpgradePreview'))){
  $calls=Calls (M $entry[0] $entry[1])
  Check (@($calls|Where-Object {$_ -like '*ParkingMarkPlacement::BayMarkers*'}).Count -eq 1) 'Renderer/preview disagree on bay run terminals'
  Check (@($calls|Where-Object {$_ -like '*ParkingMarkPlacement::BuildBayStyle*'}).Count -eq 1) 'L marker geometry not wired into rendering/preview'
 }
 $calls=Calls (M 'CurbLineEmissionSystem' 'RebuildJoins')
 Check (@($calls|Where-Object {$_ -like '*::TryCenterTransitions*'}).Count -eq 1) 'General ordered stripe planner not used'
 Check (@($calls|Where-Object {$_ -like '*::AddCenterMedianTaper*'}).Count -eq 0) 'Count-specific taper still duplicates new plan'
 $join=(MT 'CurbLineEmissionSystem').NestedTypes|Where-Object Name -eq 'JoinCap'
 Check (@($join.Fields|Where-Object Name -eq Boundary).Count -eq 1) 'Center versus median-side semantic grouping lost'
 Check (@(Calls (M 'CurbLineEmissionSystem' 'RebuildJoins')|Where-Object {$_ -like '*IntersectionPaintSystem::RequestSource*'}).Count -eq 1) 'New joins do not invalidate affected native node paint'
 Check (@(Calls (M 'JunctionMarkingSystem' 'Reveal')|Where-Object {$_ -like '*HasComponent<*MGRReplacedPaint*'}).Count -eq 1) 'Junction release clears another paint system hiding claim'
 $edge=MT 'MGREdgePaint'
 Check (@($edge.Fields|Where-Object {$_.FieldType.FullName -ne 'System.Byte'}).Count -eq 0) 'Edge intent must not serialize private asset/entity references'
 Check (@($edge.Fields).Count -eq 2) 'Two independent side choices required'
 Check (@(Calls (M 'DecalUpgradeTool' 'OnUpdate')|Where-Object {$_ -like '*::ApplyEdge*'}).Count -eq 2) 'Same edge tool must route section and node clicks'
 Check (@(Calls (M 'DecalUpgradeTool' 'OnUpdate')|Where-Object {$_ -like '*::ApplyNodeDividers*'}).Count -eq 1) 'Same divider tool must route node clicks'
 Check (@(Calls (M 'CurbLineEmissionSystem' 'AddParkingMarks')|Where-Object {$_ -like '*::ParkingBlocked*'}).Count -eq 0) 'Building entrances must not fragment parking paint'
 Check (@(Calls (M 'CurbLineEmissionSystem' 'AddParkingMarks')|Where-Object {$_ -like '*::ParkingClosesAt*'}).Count -eq 2) 'Both parking paint ends must use intersection scope'
 Check (@(Calls (M 'NodeSplitTool' 'OnUpdate')|Where-Object {$_ -like '*NodeSplitPolicy::get_Available*'}).Count -eq 1) 'Unsafe split must remain gated'
 foreach($setter in @('set_ThemeAssetEdgeColors','set_FollowCityMarkingTheme')){Check (@(Calls (M 'Setting' $setter)|Where-Object {$_ -like '*::RefreshTheme*'}).Count -eq 1) 'Theme toggle must request live refresh'}
 Check (@(Calls (M 'CurbLineEmissionSystem' 'RecordPaintSnapshot')|Where-Object {$_ -like '*::Exists*'}).Count -ge 1) 'Retained diagnostic target must be validated before reading'
 Check (@(Calls (M 'DecalUpgradeTool' 'OnUpdate')|Where-Object {$_ -like '*::ApplyNodeCenter*'}).Count -eq 1) 'Center upgrade must route node clicks without changing sections'
 Check (@(Calls (M 'DecalUpgradeTool' 'OnUpdate')|Where-Object {$_ -like '*SegmentUpgradeSystem::ApplyNode*'}).Count -eq 1) 'Visibility/reset upgrade must route node clicks'
 Check (@(Calls (M 'CurbLineEmissionSystem' 'NodeCenterPlan')|Where-Object {$_ -like '*::HighwayRoute*'}).Count -eq 2) 'Two-road center node plan must check actual native routes in either direction'
 Write-Output "$n compiled tool-exit, served-icon, shared L geometry and transition-planner contracts passed."
}finally{$a.Dispose()}
