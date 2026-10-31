param([string]$Dll=(Join-Path $PSScriptRoot '../../../outputs/The-Curbside-Mod-v0.5.1-Test80-build/MedianGrassReplacement.dll'))
$ErrorActionPreference='Stop'
$Dll=(Resolve-Path $Dll).Path
$managed='C:/Program Files (x86)/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Managed'
$framework='C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8'
$compiler=& (Join-Path $PSScriptRoot 'Resolve-Compiler.ps1')
Copy-Item -LiteralPath $Dll -Destination (Join-Path $PSScriptRoot 'MedianGrassReplacement.dll') -Force
$dependencies=@('Game','Unity.Entities','Unity.Mathematics','Unity.Collections','Unity.Burst','UnityEngine.CoreModule','Colossal.Mathematics','Colossal.Core','Colossal.UI.Binding','Colossal.IO.AssetDatabase','Colossal.AssetPipeline','netstandard')
$references=@('mscorlib','System','System.Core')|ForEach-Object {'-r:'+(Join-Path $framework ($_+'.dll'))}
foreach($name in $dependencies){$path=Join-Path $managed ($name+'.dll');Copy-Item -LiteralPath $path -Destination $PSScriptRoot -Force;$references+='-r:'+$path}
$references+='-r:'+$Dll
Push-Location $PSScriptRoot
try {
 foreach($name in @('LuminaireChecks','SignalWireChecks','StopPriorityChecks','Test83Geometry','Test85Readiness')){
  & dotnet $compiler -nologo -target:exe ('-out:'+$name+'.exe') @references ($name+'.cs')
  if($LASTEXITCODE -ne 0){throw "Compile failed: $name"}
  & (Join-Path $PSScriptRoot ($name+'.exe'));if($LASTEXITCODE -ne 0){throw "Failed: $name"}
 }
 foreach($check in @('Check-Test74Order.ps1','Check-Test76Contracts.ps1')){& (Join-Path $PSScriptRoot $check) -Dll $Dll}
 & (Join-Path $PSScriptRoot 'Check-Test73Lifecycle.ps1') -Dll $Dll -ExpectedBuild Test99
}finally{Pop-Location}
