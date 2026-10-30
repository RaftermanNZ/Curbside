param([Parameter(Mandatory=$true)][string]$Dll,[Parameter(Mandatory=$true)][string]$OutputFolder,[string]$NodeExe=$env:CURBSIDE_NODE)
$ErrorActionPreference='Stop'
$candidate=(Resolve-Path -LiteralPath $Dll).Path
$output=[IO.Path]::GetFullPath($OutputFolder)
New-Item -ItemType Directory -Path $output -Force|Out-Null
Copy-Item -LiteralPath $candidate -Destination (Join-Path $output 'MedianGrassReplacement.dll') -Force
$sidecars=Join-Path (Split-Path $candidate) 'BundledTextures'
if(!(Test-Path -LiteralPath $sidecars)){throw 'Candidate build is missing BundledTextures'}
Copy-Item -LiteralPath $sidecars -Destination $output -Recurse -Force
$managed=[Environment]::GetEnvironmentVariable('CSII_MANAGEDPATH','User')
if(!$managed){$managed='C:/Program Files (x86)/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Managed'}
$dependencies=@('Game','Unity.Entities','Unity.Burst','Unity.Mathematics','Unity.Collections','UnityEngine.CoreModule','Colossal.Mathematics','Colossal.Core','Colossal.UI.Binding','Colossal.IO.AssetDatabase','Colossal.AssetPipeline','Colossal.PSI.Common','netstandard')
foreach($name in $dependencies){Copy-Item -LiteralPath (Join-Path $managed ($name+'.dll')) -Destination $output -Force}
$framework='C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8'
$refs=@('mscorlib','System','System.Core','System.Web.Extensions')|ForEach-Object {'-r:'+(Join-Path $framework ($_+'.dll'))}
$refs+=@($dependencies+'MedianGrassReplacement')|ForEach-Object {'-r:'+(Join-Path $output ($_+'.dll'))}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Fixtures/Prepared-Pack-Test1-native-pack.json') -Destination $output -Force
$compiler=& (Join-Path $PSScriptRoot 'Resolve-Compiler.ps1')
$manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'current-checks.json') -Raw|ConvertFrom-Json
$results=@()
Push-Location $output
try{
 foreach($name in $manifest.tests){
  $source=Join-Path $PSScriptRoot ($name+'.cs');if(!(Test-Path -LiteralPath $source)){throw "Missing current test: $name"}
  & dotnet $compiler -nologo -langversion:9 -target:exe ('-out:'+$name+'.exe') @refs $source
  if($LASTEXITCODE){throw "Compile failed: $name"}
  $message=& (Join-Path $output ($name+'.exe'));if($LASTEXITCODE){throw "Check failed: $name"}
  $message|Set-Content -LiteralPath (Join-Path $output ($name+'-result.txt'));$message
  $results+=@{test=$name;result='passed';output=$message}
 }
 & (Join-Path $PSScriptRoot 'Check-Test95SaveCoordinator.ps1') -Dll $candidate
 if(!$NodeExe){$NodeExe='node'}
 & $NodeExe (Join-Path $PSScriptRoot 'UI-migration-check.mjs');if($LASTEXITCODE){throw 'UI behavior failed'}
 & (Join-Path $PSScriptRoot 'Check-AuditHardening.ps1') -Dll $candidate
 & (Join-Path $PSScriptRoot 'Check-SignalBinding.ps1') -Dll $candidate
 & (Join-Path $PSScriptRoot 'Check-WorkBatch.ps1') -Dll $candidate
 & (Join-Path $PSScriptRoot 'Check-SettingsContract.ps1') -Dll $candidate
 @{utc=[DateTime]::UtcNow.ToString('o');dll=$candidate;sha256=(Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash;results=$results;nativeRuntimeValidation='pending'}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $output 'results.json')
}finally{Pop-Location}
