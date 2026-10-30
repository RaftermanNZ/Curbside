param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
if(!$env:CURBSIDE_CECIL){throw 'Set CURBSIDE_CECIL to your installed Mono.Cecil.dll (for example from ILSpy).'}
Add-Type -Path $env:CURBSIDE_CECIL
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $Dll))
$g=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:/Program Files (x86)/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Managed/Game.dll')
try {
 $types=$a.MainModule.Types
 $load=($types|Where-Object Name -eq Mod).Methods|Where-Object Name -eq OnLoad
 $phase=(($g.MainModule.Types|Where-Object FullName -eq Game.SystemUpdatePhase).Fields|Where-Object Name -eq Serialize).Constant
 $registrations=@($load.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -like 'Update*' -and $_.Previous.Operand -eq $phase})
 if($registrations.Count -ne 5){throw "Expected exactly five save registrations, got $($registrations.Count)"}
 $expected=@{
  SignalSaveCoordinatorSystem=@('UpdateBefore','BeginPrefabSerializationSystem')
  SignalSaveNativeCompletionSystem=@('UpdateAfter','BeginPrefabSerializationSystem')
  SignalSaveSerializerEntrySystem=@('UpdateBefore','SerializerSystem')
  SignalSaveSerializerExitSystem=@('UpdateAfter','SerializerSystem')
  SignalSaveWriterScheduledSystem=@('UpdateAfter','WriteSystem')
 }
 foreach($name in $expected.Keys){
  $r=@($registrations|Where-Object {$_.Operand.GenericArguments[0].Name -eq $name})
  if($r.Count -ne 1 -or $r[0].Operand.Name -ne $expected[$name][0] -or $r[0].Operand.GenericArguments[1].Name -ne $expected[$name][1]){throw "Bad or repeated registration: $name"}
 }
 $update=($types|Where-Object Name -eq SignalSaveCoordinatorSystem).Methods|Where-Object Name -eq OnUpdate
 $calls=@($update.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -eq 'Prepare'})
 $expectedOrder=@('DecalVisibilitySystem','SignalWireSystem','FarSideLightSystem','VisualPowerlineSystem','SignalPreviewSystem','StreetLightingSystem','CurbLineEmissionSystem','IntersectionPaintSystem','JunctionMarkingSystem','FixedParkingSystem','ApproachStopSystem','GeneratedVisualSaveSystem','SignalAssetSaveSystem')
 $actual=@($calls|ForEach-Object {$_.Operand.GenericArguments[0].Name})
 if(($actual -join ',') -ne ($expectedOrder -join ',')){throw 'Save callbacks missing, duplicated, or out of order'}
 $fence=@($update.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.GenericArguments[0].Name -eq 'SignalSaveFenceSystem'})
 $audit=@($update.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.GenericArguments[0].Name -eq 'SignalSaveIndexAuditSystem'})
 if($fence.Count -ne 1 -or $audit.Count -ne 1 -or $fence[0].Offset -ge $calls[0].Offset -or $audit[0].Offset -le $calls[-1].Offset){throw 'Fence/audit must execute once around preparation'}
 $prepare=($types|Where-Object Name -eq SignalSaveCoordinatorSystem).Methods|Where-Object Name -eq Prepare
 if(@($prepare.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'PreSerialize'}).Count -ne 1){throw 'Coordinator does not call native save callback exactly once'}
 foreach($name in @('SignalSaveSerializerEntrySystem','SignalSaveSerializerExitSystem')){
  $method=($types|Where-Object Name -eq $name).Methods|Where-Object Name -eq OnUpdate
  if(@($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -ne 'Info' -and $_.Operand.Name -ne 'get_Log'}).Count){throw "Tracing marker has side effects: $name"}
 }
 $f=($types|Where-Object Name -eq SignalSaveFenceSystem).Methods|Where-Object Name -eq OnUpdate
 if(-not ($f.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'GetEnabledData'})){throw 'Native effect dependency fence missing'}
 $writer=($types|Where-Object Name -eq SignalSaveWriterScheduledSystem).Methods|Where-Object Name -eq OnUpdate
 foreach($required in @('get_writeDependency','Complete')){
  if(@($writer.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $required}).Count -ne 1){throw "Missing native write fence: $required"}
 }
 foreach($forbidden in @('WriteBytes','Dispose','AddBuffer','Schedule','SetComponentData','CreateEntity','DestroyEntity')){
  if(@($writer.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $forbidden}).Count){throw "Write fence modifies native state: $forbidden"}
 }
 'PASS Test99: five unique save registrations; 13 callbacks once in order; fence/audit once; passive serialization markers and native write-completion fence.'
}finally{$a.Dispose();$g.Dispose()}

