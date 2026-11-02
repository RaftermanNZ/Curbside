param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $Dll))
$script:checks=0
function Check($ok,$message){if(!$ok){throw $message};$script:checks++}
function Method($type,$name){($assembly.MainModule.Types|Where-Object Name -eq $type).Methods|Where-Object Name -eq $name}
function Calls($method,$name){@($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name})}
try {
 $signal=Method SignalPreviewSystem UpdatePerformanceCore
 Check (@($signal.Body.Instructions|Where-Object {$_.OpCode.Code.ToString() -eq 'Isinst' -and $_.Operand.Name -eq 'NetToolSystem'}).Count -eq 0) 'Signal previews still depend on a particular placement tool class'
 foreach($role in @('TrafficLightData','StreetLightData')){
  Check (@($signal.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -eq 'HasComponent' -and $_.Operand.GenericArguments[0].Name -eq $role}).Count -gt 0) "Preview does not filter initialized prefab role: $role"
 }
 $visual=($assembly.MainModule.Types|Where-Object Name -eq CrosswalkVisualSystem)
 foreach($method in $visual.Methods|Where-Object HasBody){foreach($i in $method.Body.Instructions){
  if($i.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $i.Operand.Name -in @('AddComponent','RemoveComponent')){
   Check ($i.Operand.GenericArguments[0].Name -ne 'Hidden') 'Crosswalk preview mutates routed-lane Hidden state'
  }
 }}
 $create=Method CrosswalkVisualSystem OnCreate
  $tempExcluded=$false
 for($j=0;$j -lt $create.Body.Instructions.Count;$j++){
  $i=$create.Body.Instructions[$j]
  if($i.OpCode.Code.ToString() -eq 'Stfld' -and $i.Operand.Name -eq 'None'){
   for($k=[Math]::Max(0,$j-10);$k -lt $j;$k++){
    $operand=$create.Body.Instructions[$k].Operand
    if($operand -is [Mono.Cecil.GenericInstanceMethod] -and $operand.GenericArguments.Count -gt 0 -and $operand.GenericArguments[0].Name -eq 'Temp'){$tempExcluded=$true}
   }
  }
 }
 Check $tempExcluded 'Temporary network previews still trigger broad native crossing restoration' 
 $inherit=Method CrosswalkVisualSystem InheritPreviewPaint
 foreach($name in @('Suppress','RestoreSource','Contains')){Check ((Calls $inherit $name).Count -gt 0) "Crossing preview lacks lifecycle/ownership check: $name"}
 foreach($type in @('MGRReplacedPaint','CarLane','ParkingLane','TrackLane')){
  Check (@($inherit.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -eq 'HasComponent' -and $_.Operand.GenericArguments[0].Name -eq $type}).Count -gt 0) "Crossing inheritance scope missing $type"
 }
 Check ((Calls (Method CrosswalkPreviewRestoreSystem OnUpdate) RestoreInheritedPreviews).Count -eq 1) 'Inherited crossing previews are not restored before commit'
 Check ((Calls (Method CrosswalkPreviewSystem OnUpdate) Complete).Count -eq 1) 'Unexpected unconditional crosswalk completion (only overlay writer wait is expected)'
 $paths=Method CurbLineEmissionSystem IndividualDividerPaths
 foreach($name in @('GetRoadStopRange','LaneForward','TrimRange','HighwayLinePaths')){Check ((Calls $paths $name).Count -eq 1) "Individual divider stop-bar path missing $name"}
 Check ($paths.Body.Instructions.IndexOf((Calls $paths TrimRange)[0]) -lt $paths.Body.Instructions.IndexOf((Calls $paths HighwayLinePaths)[0])) 'Manual divider is dashed/offset before clipping at its stop bar'
 foreach($method in @('IndividualDividerChoices','ApplyIndividualDividers')){Check ((Calls (Method CurbLineEmissionSystem $method) IndividualDividerPaths).Count -eq 1) "Preview/output do not share stop-bar clipping: $method"}
 foreach($field in ($assembly.MainModule.Types|Where-Object {$_.Name -in @('SignalPreviewSystem','CrosswalkVisualSystem')}).Fields|Where-Object {!$_.IsStatic}){
  Check (!$field.FieldType.IsPointer -and $field.FieldType.FullName -notmatch 'NativeArray|DynamicBuffer|NativeList') "Borrowed/native preview state retained: $($field.Name)"
 }
 "$checks Test156 compiled preview scope, crosswalk routing, commit restoration, stop-bar clipping and managed-lifetime checks passed. Rendering/FPS remain unverified."
}finally{$assembly.Dispose()}
