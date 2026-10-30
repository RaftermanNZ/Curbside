param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
if(!$env:CURBSIDE_CECIL){throw 'Set CURBSIDE_CECIL to your installed Mono.Cecil.dll (for example from ILSpy).'}
Add-Type -Path $env:CURBSIDE_CECIL
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Dll))
try{
 $type=$assembly.MainModule.Types|Where-Object Name -eq CurbLineEmissionSystem
 $joins=$type.Methods|Where-Object Name -eq RebuildJoins
 if(!($joins.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'WorkBatchPolicy' -and $_.Operand.Name -eq 'Take'})){throw 'Join snapshot is no longer bounded'}
 foreach($methodName in @('UpdateMarkings','RebuildJoins')){
  $method=$type.Methods|Where-Object Name -eq $methodName
  foreach($instruction in $method.Body.Instructions){
   if($instruction.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Newobj -and $instruction.Operand -is [Mono.Cecil.MethodReference] -and $instruction.Operand.DeclaringType.FullName -match 'System.Collections.Generic.(List|HashSet).*Unity.Entities.Entity'){
    $fullCopy=@($instruction.Operand.Parameters|Where-Object {$_.ParameterType.FullName -match 'IEnumerable.*Unity.Entities.Entity'}).Count -gt 0
    if($methodName -eq 'UpdateMarkings' -or $fullCopy){throw "Recurring/full-backlog Entity work collection allocation in $methodName"}
   }
   if($instruction.Operand -is [Mono.Cecil.MethodReference] -and $instruction.Operand.DeclaringType.FullName -eq 'System.Diagnostics.Stopwatch' -and $instruction.Operand.Name -eq 'StartNew'){throw "Stopwatch allocated each pass in $methodName"}
  }
 }
 $raster=@($assembly.MainModule.Resources|Where-Object {$_.Name -match '\.(png|jpg|jpeg|dds|tga)$'})
 if($raster.Count){throw 'Raster texture remains embedded in core DLL'}
 'PASS: bounded join snapshot, reused Entity work collections/timers, no raster textures embedded in core DLL.'
}finally{$assembly.Dispose()}
