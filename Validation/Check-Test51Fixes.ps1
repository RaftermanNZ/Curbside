param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:\Users\Trevor\AppData\Local\Programs\ILSpy\Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll);$n=0
function Check($value,$message){if(-not $value){throw $message};$script:n++}
function Method($type,$name){($a.MainModule.Types|Where-Object Name -eq $type).Methods|Where-Object Name -eq $name}
function Calls($method){@($method.Body.Instructions|ForEach-Object Operand|Where-Object {$_ -is [Mono.Cecil.MethodReference]}|ForEach-Object FullName)}
try {
 $calls=Calls (Method 'YellowPaintDefinitionSystem' 'OnUpdate')
 Check (@($calls|Where-Object {$_ -like '*::get_isBuiltin*'}).Count -eq 0) 'Paint discovery still excludes runtime-loaded native assets'
 $calls=Calls (Method 'CurbLineEmissionSystem' 'BuildCurves')
 Check (@($calls|Where-Object {$_ -like '*YellowPaintDefinitionSystem::Explicit*'}).Count -eq 0) 'Optional center paint still blocks the road rebuild'
 $calls=Calls (Method 'ParkingUpgradeTool' 'OnUpdate')
 foreach($name in @('ParkingBanPolicy::ClearSide','RoadAttachmentTransferSystem::Forget','SectionUpgradeTargeting::TryRoad','SegmentUpgradePolicy::QueuePaint')){Check (@($calls|Where-Object {$_ -like ('*'+$name+'*')}).Count -gt 0) "Parking reconciliation missing $name"}
 $calls=Calls (Method 'DecalUpgradeTool' 'OnUpdate')
 Check (@($calls|Where-Object {$_ -like '*RoadAttachmentTransferSystem::Forget*'}).Count -ge 2) 'Ban edits can restore stale transfer state'
 $calls=Calls (Method 'IntersectionPaintSystem' 'Reconcile')
 foreach($name in @('NodeLinePolicy::IsLineGeometry','CurbLineEmissionSystem::IsOuterEdgePaint','RoadSurfacePolicy::IsAlleyForEdges')){Check (@($calls|Where-Object {$_ -like ('*'+$name+'*')}).Count -gt 0) "Native alley edge handling missing $name"}
 $calls=Calls (Method 'CurbLineEmissionSystem' 'AddJunctionEdges')
 Check (@($calls|Where-Object {$_ -match 'EntityManager::(Add|Set|Remove|Create|Destroy)'}).Count -eq 0) 'Corner geometry collection mutates road data'
 Check (@($calls|Where-Object {$_ -like '*JunctionEdgePolicy::TryCorner*'}).Count -eq 1) 'Safe corner geometry is disconnected'
 $marker=$a.MainModule.Types|Where-Object Name -eq 'MGRJunctionEdgeLines'
 Check (@($marker.Interfaces|Where-Object {$_.InterfaceType.Name -eq 'IEmptySerializable'}).Count -eq 1) 'Junction upgrade not saved as intent'
 $calls=Calls (Method 'SegmentUpgradeSystem' 'Apply')
 Check (@($calls|Where-Object {$_ -like '*Remove<*MGRJunctionEdgeLines*'}).Count -eq 1) 'Master reset misses new junction upgrade'
 $strings=@((Method 'RoadFeatureInspector' 'Describe').Body.Instructions|Where-Object {$_.OpCode.Code -eq 'Ldstr'}|ForEach-Object Operand)
 Check (@($strings|Where-Object {$_ -match 'Global marking theme|waiting to render|1 arrows|Inspector shows'}).Count -eq 0) 'Inspector still contains diagnostics/global configuration'
 $calls=Calls (Method 'Setting' 'set_MarkingToolColor')
 Check (@($calls|Where-Object {$_ -like '*IntersectionPaintSystem::RequestRefresh*'}).Count -eq 1) 'Colour theme does not refresh native paint'
 Write-Output "$n compiled parking, optional-asset, alley, junction and inspector checks passed."
}finally{$a.Dispose()}
