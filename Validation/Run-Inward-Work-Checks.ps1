param([string]$Candidate=(Join-Path $PSScriptRoot '../obj/Release/net48/MedianGrassReplacement.dll'),[string]$Output=(Join-Path $PSScriptRoot '../../../outputs/Curbside-Inward-Corners-Prototype/Checks'))
$ErrorActionPreference='Stop'
$Output=[IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$managed='C:/Program Files (x86)/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Managed'
$deps=@('Game','Unity.Entities','Unity.Mathematics','Unity.Collections','UnityEngine.CoreModule','UnityEngine.SharedInternalsModule','UnityEngine.PropertiesModule','Colossal.Mathematics','Colossal.Core','Colossal.Logging','Unity.Burst','Colossal.Collections','Colossal.IO.AssetDatabase','Colossal.Localization','Colossal.UI.Binding','Colossal.AssetPipeline','Colossal.PSI.Common','netstandard')
foreach($name in $deps){Copy-Item -LiteralPath (Join-Path $managed ($name+'.dll')) -Destination $Output -Force}
Copy-Item -LiteralPath $Candidate -Destination (Join-Path $Output 'MedianGrassReplacement.dll') -Force
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Fixtures') -Filter '*.json' | Copy-Item -Destination $Output -Force
$compiler=& (Join-Path $PSScriptRoot 'Resolve-Compiler.ps1')
$framework='C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8'
$refs=@('mscorlib','System','System.Core','System.Web.Extensions')|ForEach-Object {'-r:'+(Join-Path $framework ($_+'.dll'))}
$refs+=@($deps+'MedianGrassReplacement')|ForEach-Object {'-r:'+(Join-Path $Output ($_+'.dll'))}
$tests=@('SignalWorkBudgetChecks','InwardCornerChecks','UnchangedHoverChecks','PreviewInheritanceChecks','NativeSignalPreviewChecks','SignalWireChecks','RightBankChecks','SignalLaneBindingChecks227','IndividualDividerChecks','ToolFamilyAuditChecks212','WireCornerChecks229','SurfaceRestoreChecks','NativeNetworkFixChecks','SignalPoseOwnershipChecks214','MastCollisionChecks222','WireClearanceChecks223','CoherentWireClearance229','VirtualWireSupports229','SignalPlacementChecks226','SignalUpgradeChecks226','SignalMaintenanceChecks','SignalCombinationChecks214','FortyIntersectionChecks214','Intersection300Checks214')
$results=@()
foreach($name in $tests){
 $sources=@((Join-Path $PSScriptRoot ($name+'.cs')))
 if($name -in @('SignalWorkBudgetChecks','InwardCornerChecks')){$sources+=Join-Path $PSScriptRoot 'WireClearanceChecks223.cs'}
 & dotnet $compiler -nologo -nowarn:0649 -langversion:9 -target:exe ('-main:'+$name) ('-out:'+(Join-Path $Output ($name+'.exe'))) @refs @sources *> (Join-Path $Output ($name+'-Compile.log'))
 if($LASTEXITCODE){$results+=@{name=$name;compileExit=$LASTEXITCODE;runExit=$null};Write-Output ($name+' compile failed');continue}
 Push-Location $Output
 try {& 'C:/Program Files/Unity 2022.3.62f2/Editor/Data/MonoBleedingEdge/bin/mono.exe' ($name+'.exe') *> ($name+'.log');$code=$LASTEXITCODE}
 finally{Pop-Location}
 $results+=@{name=$name;compileExit=0;runExit=$code}
 Write-Output ($name+' exit='+$code)
}
$results|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Output 'Results.json')
if($results|Where-Object {$_.compileExit -ne 0 -or $_.runExit -ne 0}){exit 1}
