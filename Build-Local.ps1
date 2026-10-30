param([string]$OutputFolder = (Join-Path $PSScriptRoot 'BuildOutput'))

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'MedianGrassReplacement.csproj'


$managed = [Environment]::GetEnvironmentVariable('CSII_MANAGEDPATH','User')

if (-not $managed) { $managed = 'C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed' }

$unity = [Environment]::GetEnvironmentVariable('CSII_UNITYMODPROJECTPATH','User')

$post = [Environment]::GetEnvironmentVariable('CSII_MODPOSTPROCESSORPATH','User')

Push-Location $PSScriptRoot

try {

& dotnet msbuild $project -restore '-t:PrepareResources;Compile' -p:Configuration=Release -p:LangVersion=9.0 -p:RestoreIgnoreFailedSources=true -p:NugetAudit=false -verbosity:minimal

} finally { Pop-Location }

if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }

New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null

$stage = Join-Path $OutputFolder ('stage-'+[Guid]::NewGuid().ToString('N'))

New-Item -ItemType Directory -Path $stage | Out-Null

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'obj\Release\net48\MedianGrassReplacement.dll'),(Join-Path $PSScriptRoot 'obj\Release\net48\MedianGrassReplacement.pdb') -Destination $stage

$postArgs = @('PostProcess',(Join-Path $stage 'MedianGrassReplacement.dll'),'-u',$unity,'-p','Windows','-d')

foreach ($reference in Get-ChildItem -LiteralPath $managed -Filter '*.dll') { $postArgs += @('-r',$reference.FullName) }

$quotedArgs = ($postArgs | ForEach-Object { '"' + $_.Replace('"','\"') + '"' }) -join ' '

$postProcess = Start-Process -FilePath $post -ArgumentList $quotedArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $stage 'postprocess.log') -RedirectStandardError (Join-Path $stage 'postprocess-errors.log')

$postProcess.WaitForExit()

$postExit = $postProcess.ExitCode

if ($postExit -ne 0) {

    # The game tool hard-codes a user-profile Burst temp folder. In a restricted

    # workspace it can finish IL processing but fail on that cache permission.

    # Recover only this specific failure, with the same Burst compiler using a

    # workspace temp folder. Other failures remain errors.

    $log = (Get-Content -LiteralPath (Join-Path $stage 'postprocess.log') -Raw) + (Get-Content -LiteralPath (Join-Path $stage 'postprocess-errors.log') -Raw)

    $processed = Join-Path $stage 'Temp\MedianGrassReplacement.dll'

    if (-not (Test-Path -LiteralPath $processed) -or $log -notmatch 'UnauthorizedAccessException' -or $log -notmatch 'Burst') {

        throw "Game post-processing failed; see $stage\postprocess.log"

    }

    $burst = Join-Path $unity 'Library\PackageCache\com.unity.burst@1.8.23'

    $editor = 'C:\Program Files\Unity 2022.3.62f2\Editor\Data'

    $burstArgs = @(('--root-assembly='+$processed),('--output='+(Join-Path $stage 'MedianGrassReplacement_win_x86_64')),'--include-root-assembly-references=False','--platform=Windows','--target=X64_SSE2','--debug=Full',('--temp-folder='+(Join-Path $stage 'BurstTemp')),('--assembly-folder='+(Join-Path $stage 'Temp')),('--assembly-folder='+$managed),('--assembly-folder='+(Join-Path $editor 'Managed')),('--assembly-folder='+$burst),('--assembly-folder='+(Join-Path $burst 'Unity.Burst.CodeGen')),('--key-folder='+(Join-Path $editor 'PlaybackEngines\WindowsStandaloneSupport')))

    & (Join-Path $burst '.Runtime\bcl.exe') @burstArgs *> (Join-Path $stage 'burst.log')

    if ($LASTEXITCODE -ne 0) { throw "Burst failed; see $stage\burst.log" }

    Copy-Item -LiteralPath $processed,(Join-Path $stage 'Temp\MedianGrassReplacement.pdb') -Destination $stage -Force

}

Get-ChildItem -LiteralPath $stage -File | Where-Object Extension -in '.dll','.pdb' | Copy-Item -Destination $OutputFolder -Force

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UI\build\curbside.mjs') -Destination $OutputFolder -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UI\build\curbside.css') -Destination $OutputFolder -Force

Write-Output "Windows test build ready: $OutputFolder. Installed mod was not replaced."




$textureOutput=Join-Path $OutputFolder "BundledTextures"
New-Item -ItemType Directory -Path $textureOutput -Force|Out-Null
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot "BundledTextures") -Filter "*.png" -File|Copy-Item -Destination $textureOutput -Force
