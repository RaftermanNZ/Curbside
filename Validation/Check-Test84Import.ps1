param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $Dll).Path)
function Types($items){foreach($t in $items){$t;Types $t.NestedTypes}}
try {
 $all=@(Types $a.MainModule.Types)
 $pack=$all|Where-Object FullName -eq 'GardenCitySignals.PackSystem'
 $import=$pack.Methods|Where-Object Name -eq Import
 if($import.ReturnType.FullName -ne 'System.Threading.Tasks.Task'){throw 'Import escapes the shutdown task lifecycle'}
 $update=$pack.Methods|Where-Object Name -eq OnUpdate
 if(-not @($update.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'SharedTask'}).Count){throw 'Import is not registered in native TaskManager'}
 $state=$pack.NestedTypes|Where-Object Name -like '<Import>*'
 $body=($state.Methods|Where-Object Name -eq MoveNext).Body.Instructions
 if(-not @($body|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'ImportProgress'}).Count){throw 'ImportPath has no shutdown callback'}
 $cancel=$pack.Methods|Where-Object Name -eq get_CancelImport
 if(-not @($cancel.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'get_IsCancellationRequested'}).Count){throw 'Game termination token is ignored'}
 if(-not @($cancel.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'get_state'}).Count){throw 'Early Quitting state is ignored'}
 $geometryCalls=@($all|ForEach-Object Methods|Where-Object HasBody|ForEach-Object {$_.Body.Instructions}|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'GeometryAsset' -and $_.Operand.Name -in @('ObtainMeshes','ReleaseMeshes','SetData','Save')})
 if($geometryCalls.Count){throw 'Unsafe geometry lifecycle calls were introduced'}
 Write-Output 'PASS Test84: Task-returning importer; native shutdown task tracking; native cancellation callback; termination token and early Quitting guard; no manual GeometryAsset lifecycle calls.'
}finally{$a.Dispose()}
