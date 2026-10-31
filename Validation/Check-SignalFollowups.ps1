param([Parameter(Mandatory=$true)][string]$Curbside,[Parameter(Mandatory=$true)][string]$Signals)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$curb=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Curbside).Path)
$pack=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Signals).Path)
$script:checks=0
function Check($condition,[string]$reason){if(-not $condition){throw $reason};$script:checks++}
function Calls($method,[string]$name){@($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name})}
function Types($type){$type;foreach($child in $type.NestedTypes){Types $child}}
try {
 $names=$curb.MainModule.Types | Where-Object Name -eq SignalStreetNameSystem
 $material=$names.Methods | Where-Object Name -eq EnsureMaterial
 Check ((Calls $material 'ValidateMaterial').Count -eq 1) 'HDRP physical sign material is not validated'
 Check ((Calls $material 'RefreshCoverage')[0].Offset -lt (Calls $material 'ValidateMaterial')[0].Offset) 'HDRP validation precedes material/atlas setup'
 $update=$names.Methods | Where-Object Name -eq UpdateNames
 Check ((Calls $update 'ExportDiagnostics').Count -eq 0) 'Diagnostic texture readback runs during normal updates'
 $callers=@(foreach($type in $curb.MainModule.Types){foreach($entry in (Types $type)){foreach($method in $entry.Methods){if($method.HasBody -and (Calls $method 'ExportDiagnostics').Count -gt 0){$method}}}})
 Check ($callers.Count -eq 1 -and $callers[0].DeclaringType.FullName -match 'CurbsidePanelSystem') 'Diagnostics must be triggered only by the panel snapshot action'
 $policy=$curb.MainModule.Types | Where-Object Name -eq GardenCityLightPolicy
 Check ((Calls ($policy.Methods | Where-Object Name -eq SelectSuppressed) 'OutlineMeshes').Count -eq 1) 'Suppression still discards native render slots'
 $system=$pack.MainModule.Types | Where-Object Name -eq PackSystem
 Check (@($pack.MainModule.AssemblyReferences | Where-Object Name -eq MedianGrassReplacement).Count -eq 0) 'Optional asset pack acquired a Curbside dependency'
 $cache=$system.Methods | Where-Object Name -eq get_CacheRestoreEnabled
 Check (@($cache.Body.Instructions | Where-Object {$_.OpCode.Code.ToString() -eq 'Ldc_I4_0'}).Count -eq 1) 'Unverified cached prefab restoration was re-enabled'
 $machine=$system.NestedTypes | Where-Object Name -Like '<Import>d__*'
 $move=$machine.Methods | Where-Object Name -eq MoveNext
 Check ((Calls $move 'ImportPath').Count -eq 1) 'Source import has multiple pipeline call sites'
 Check ((Calls $move 'SourceImportPaths').Count -eq 1) 'Source import does not receive the whole module path batch'
 Check ((Calls $move 'SourceImportPaths')[0].Offset -lt (Calls $move 'ImportPath')[0].Offset) 'Module batch is constructed after source import'
 Write-Output "$checks compiled signal material, diagnostics, suppression and independent import contracts passed. Native rendering/startup measurement still required."
}finally{$curb.Dispose();$pack.Dispose()}
