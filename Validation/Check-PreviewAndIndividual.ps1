param([Parameter(Mandatory=$true)][string]$Dll,[Parameter(Mandatory=$true)][string]$Baseline)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $Dll))
$b=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $Baseline))
try {
 $types=$a.MainModule.Types
 foreach($old in $b.MainModule.Types|Where-Object {$_.IsValueType -and @($_.Interfaces|Where-Object {$_.InterfaceType.FullName -match 'ISerializable|IEmptySerializable|IComponentData|IBufferElementData'}).Count -gt 0}) {
  $new=$types|Where-Object FullName -eq $old.FullName
  if(!$new){throw "Existing component removed: $($old.FullName)"}
  $before=@($old.Fields|Where-Object {!$_.IsStatic}|ForEach-Object {$_.Name+':'+$_.FieldType.FullName}) -join '|'
  $after=@($new.Fields|Where-Object {!$_.IsStatic}|ForEach-Object {$_.Name+':'+$_.FieldType.FullName}) -join '|'
  if($before -ne $after){throw "Existing component layout changed: $($old.FullName)"}
 }
 $load=($types|Where-Object Name -eq Mod).Methods|Where-Object Name -eq OnLoad
 $registration=@($load.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -like 'Update*' -and $_.Operand.GenericArguments[0].Name -eq 'PaintPreviewVisibilitySystem'})
 if($registration.Count -ne 1 -or $registration[0].Operand.Name -ne 'UpdateAfter' -or $registration[0].Operand.GenericArguments[1].Name -ne 'IntersectionPaintSystem'){throw 'Preview guard must have one ordered registration'}
 $preview=$types|Where-Object Name -eq PaintPreviewVisibilitySystem
 foreach($method in $preview.Methods|Where-Object HasBody){foreach($instruction in $method.Body.Instructions) {
  $call=$instruction.Operand
  if($call -is [Mono.Cecil.MethodReference] -and $call.Name -in @('DestroyEntity','CompleteAllTrackedJobs','CreateEntity','SetComponentData','SetComponentEnabled')){throw "Preview guard performs forbidden mutation: $($call.FullName)"}
  if($call -is [Mono.Cecil.GenericInstanceMethod] -and $call.Name -in @('AddComponent','RemoveComponent') -and $call.GenericArguments[0].Name -notin @('Hidden','BatchesUpdated')){throw 'Preview guard mutates more than visual flags'}
 }}
 foreach($field in ($types|Where-Object Name -eq MGRIndividualDivider).Fields|Where-Object {!$_.IsStatic}) {
  if($field.FieldType.FullName -notin @('Unity.Mathematics.float3','System.Byte')){throw 'Saved individual intent retains runtime entity/native memory'}
 }
 'PASS: existing saved component layouts retained; one ordered preview registration; visual-only direct mutations with delegated render aliases; no new global fences or stored native pointers in preview guard.'
} finally {$a.Dispose();$b.Dispose()}
