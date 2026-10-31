param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Dll))
try {
 function Method($typeName,$methodName) {
  $type=$assembly.MainModule.Types|Where-Object Name -eq $typeName
  $method=$type.Methods|Where-Object Name -eq $methodName
  if(!$method -or !$method.HasBody){throw "Missing $typeName.$methodName"};return $method
 }
 $power=Method 'VisualPowerlineSystem' 'UpdatePerformanceCore'
 if(!($power.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'WorkBatchPolicy' -and $_.Operand.Name -eq 'Take'})){throw 'Powerline node snapshot lost bounded selection'}
 foreach($pair in @(@('VisualPowerlineSystem','UpdatePerformanceCore'),@('RoadAttachmentTransferSystem','UpdatePerformanceCore'))) {
  $method=Method $pair[0] $pair[1]
  foreach($instruction in $method.Body.Instructions) {
   if($instruction.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Newobj -and $instruction.Operand.DeclaringType.FullName -match 'System.Collections.Generic.(List|Dictionary).*Unity.Entities.Entity') {
    if($pair[0] -eq 'RoadAttachmentTransferSystem' -or @($instruction.Operand.Parameters|Where-Object {$_.ParameterType.FullName -match 'IEnumerable'}).Count){throw "Recurring collection allocation in $($pair[0])"}
   }
  }
 }
 foreach($pair in @(@('ApproachStopSystem','UpdatePerformanceCore'),@('CurbDecalSystem','UpdateMeasured'),@('JunctionMarkingSystem','UpdatePerformanceCore'),@('StreetLightingSystem','UpdateMeasuredCore'),@('VisualPowerlineSystem','UpdatePerformanceCore'))) {
  $method=Method $pair[0] $pair[1]
  if($method.Body.Instructions|Where-Object {$_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Newarr -and $_.Operand.FullName -eq 'Unity.Entities.ComponentType'}){throw "Recurring change-filter allocation in $($pair[0])"}
 }
 $labels=Method 'FarSideLightSystem' 'UpdateSignLabels'
 $firstCall=@($labels.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]})[0]
 if($firstCall.Operand.Name -ne 'get_LetteringIsolationEnabled'){throw 'Disabled street-name planning must return before entity/approach work'}
 $maintenance=Method 'FarSideLightSystem' 'MaintainTrackedSignals'
 if(!($maintenance.Body.ExceptionHandlers|Where-Object {$_.HandlerType -eq [Mono.Cecil.Cil.ExceptionHandlerType]::Finally})){throw 'Maintenance scalar state must clear even on exceptions'}
 $cacheClears=@($maintenance.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Clear' -and $_.Operand.DeclaringType.FullName -match 'Dictionary'})
 if($cacheClears.Count -lt 2){throw 'Maintenance cache must be scoped to each pass'}
 $lookup=Method 'FarSideLightSystem' 'MaintenanceNode'
 if(!($lookup.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'TryGetValue'})){throw 'Maintenance does not reuse node state'}
 foreach($field in ($assembly.MainModule.Types|Where-Object Name -eq FarSideLightSystem).Fields) {
  if($field.Name -eq '_maintenanceNodes' -and $field.FieldType.FullName -notmatch 'System.Collections.Generic.Dictionary`2<Unity.Entities.Entity,System.Byte>'){throw 'Maintenance cache must only retain managed scalar data'}
 }
 'PASS: compiled powerline caller uses bounded work selection; transfer scratch is reused; five recurring change-filter arrays removed; disabled label planning exits early. Native runtime behavior remains unverified.'
} finally {$assembly.Dispose()}
