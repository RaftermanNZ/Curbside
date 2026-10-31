param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Dll).Path)
$script:checks=0
function Check($condition,[string]$reason){if(-not $condition){throw $reason};$script:checks++}
function Calls($method,[string]$name){@($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name})}
try {
 $types=$assembly.MainModule.Types
 $mod=$types | Where-Object Name -eq Mod
 $load=$mod.Methods | Where-Object Name -eq OnLoad
 $registration=@($load.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -eq 'UpdateBefore'})
 Check (@($registration | Where-Object {$_.Operand.GenericArguments[0].Name -eq 'SignalPreviewRestoreSystem' -and $_.Operand.GenericArguments[1].Name -eq 'ApplyNetSystem'}).Count -eq 1) 'Preview does not restore native references before commit'
 $preview=$types | Where-Object Name -eq SignalPreviewSystem
 $update=$preview.Methods | Where-Object Name -eq OnUpdate
 foreach($forbidden in @('AddPrefab','CreateEntity','DestroyEntity','Spawn')){Check ((Calls $update $forbidden).Count -eq 0) "Preview creates persistent work: $forbidden"}
 Check ((Calls ($preview.Methods | Where-Object Name -eq PreSerialize) 'Restore').Count -eq 1) 'Preview references survive save preparation'
 $wire=$types | Where-Object Name -eq SignalWireSystem
 $create=$wire.Methods | Where-Object Name -eq RegisterPrefab
 Check ((Calls $create 'Clone').Count -eq 1) 'Cable does not use its own native prefab copy'
 Check ((Calls $create 'PackPresent').Count -eq 1) 'Cable initializes when optional models are absent'
 foreach($field in @('m_UtilityType','m_LocalConnectionLane','m_LocalConnectionLane2','m_NodeObject','m_VisualCapacity','m_Hanging','m_PathfindPrefab')){
  Check (@($create.Body.Instructions | Where-Object {$_.OpCode.Code.ToString() -eq 'Stfld' -and $_.Operand.Name -eq $field}).Count -eq 1) "Cable simulation or wind field not explicitly cleared: $field"
 }
 foreach($name in @('GeneratedPaintOwnerCleanupSystem','GeneratedVisualSaveSystem','GeneratedVisualRetirementSystem')){
  $method=($types | Where-Object Name -eq $name).Methods | Where-Object Name -eq OnCreate
  Check (@($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and @($_.Operand.GenericArguments | Where-Object Name -eq MGRSignalWire).Count -gt 0}).Count -gt 0) "Cable missing lifecycle protection: $name"
 }
 $names=$types | Where-Object Name -eq SignalStreetNameSystem
 Check ((Calls ($names.Methods | Where-Object Name -eq UpdateNames) 'BuildLetterGeometry').Count -eq 1) 'Street names still upload opaque rectangular letter quads'
 Check ((Calls ($names.Methods | Where-Object Name -eq EnsureMaterial) 'ValidateMaterial').Count -eq 1) 'Street-name lit material is not validated'
 Write-Output "$checks compiled Test76 preview, native cable, save and sign contracts passed. Native rendering and road-edit reproduction remain required."
}finally{$assembly.Dispose()}
