param([string]$NodeExe = 'node', [string]$NpmCli = '')
$ErrorActionPreference='Stop'
$ui=Join-Path $PSScriptRoot 'UI'

[xml]$project=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'MedianGrassReplacement.csproj') -Raw
$versionText=($project.Project.PropertyGroup.Version|Where-Object {$_}|Select-Object -First 1)
if($versionText -notmatch '^(\d+)\.(\d+)\.(\d+)(?:-test\.(\d+))?$'){throw 'Expected a release version or a version with a numeric test build.'}
$revision=if($Matches[4]){$Matches[4]}else{'0'}
$moduleVersion="$($Matches[1]).$($Matches[2]).$($Matches[3]).$revision"
$metadataPath=Join-Path $ui 'mod.json';$metadata=Get-Content -LiteralPath $metadataPath -Raw|ConvertFrom-Json
if($metadata.version -ne $moduleVersion){$metadata.version=$moduleVersion;$metadata|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $metadataPath}

if($env:CURBSIDE_NODE){$NodeExe=$env:CURBSIDE_NODE}
if($env:CURBSIDE_NPM_CLI){$NpmCli=$env:CURBSIDE_NPM_CLI}
$version=& $NodeExe --version
if($LASTEXITCODE -ne 0 -or $version -notmatch '^v(22|24)\.'){throw 'Use supported Node.js22 or24 LTS for the UI build.'}
$savedPath=$env:PATH
$env:PATH=(Split-Path (Get-Command $NodeExe -ErrorAction Stop).Source)+[IO.Path]::PathSeparator+$env:PATH
Push-Location $ui
try {
 if(-not (Test-Path 'node_modules/webpack')){throw 'Restore UI packages first with npm ci (or npm install once to create the lockfile).'}
 if($NpmCli){& $NodeExe $NpmCli run build}else{& npm.cmd run build}
 if($LASTEXITCODE -ne 0){throw 'UI typecheck/webpack failed'}
} finally {Pop-Location;$env:PATH=$savedPath}
