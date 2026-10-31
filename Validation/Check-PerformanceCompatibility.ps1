param([Parameter(Mandatory=$true)][string]$Baseline,[Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$before=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Baseline))
$after=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Dll))
try {
 $schemas=0;$serializers=0;$saveMethods=0
 foreach($type in $before.MainModule.Types) {
  $next=$after.MainModule.Types|Where-Object FullName -eq $type.FullName
  $persisted=@($type.Interfaces|Where-Object {$_.InterfaceType.FullName -match 'Colossal.Serialization.Entities.I(Empty)?Serializable'}).Count -gt 0
  if($persisted) {
   if(!$next){throw "Removed persisted type $($type.FullName)"}
   $old=($type.Fields|Where-Object {!$_.IsStatic}|ForEach-Object {$_.FieldType.FullName+' '+$_.Name}) -join '|'
   $new=($next.Fields|Where-Object {!$_.IsStatic}|ForEach-Object {$_.FieldType.FullName+' '+$_.Name}) -join '|'
   if($old -ne $new){throw "Changed persisted layout: $($type.FullName)"};$schemas++
  }
  foreach($method in $type.Methods|Where-Object HasBody) {
   $isSerializer=$method.Name -match '^(Serialize|Deserialize)$'
   $isSave=$type.Name -match 'SaveBoundary|SavePreparation|SaveCoordinator|SaveRecovery|SignalAssetSave|GeneratedVisualSave'
   if(!$isSerializer -and !$isSave){continue}
   $other=$next.Methods|Where-Object FullName -eq $method.FullName
   if(!$other -or !$other.HasBody){throw "Removed compatibility method $($method.FullName)"}
   $old=($method.Body.Instructions|ForEach-Object {$_.OpCode.ToString()+' '+$_.Operand}) -join "`n"
   $new=($other.Body.Instructions|ForEach-Object {$_.OpCode.ToString()+' '+$_.Operand}) -join "`n"
   if($old -ne $new){throw "Changed compatibility body: $($method.FullName)"}
   if($isSerializer){$serializers++};if($isSave){$saveMethods++}
  }
 }
 "PASS: $schemas persisted component layouts, $serializers serializer bodies and $saveMethods selected save-path method bodies unchanged from baseline. Native save/reload testing remains required."
} finally {$before.Dispose();$after.Dispose()}
