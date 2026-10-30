$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot)
try {
 $version=(& dotnet --version).Trim()
 if($LASTEXITCODE -ne 0){throw 'SDK resolution failed; check global.json'}
 $pattern='^'+[regex]::Escape($version)+'\s+\[(.+)\]$'
 $entry=(& dotnet --list-sdks)|Where-Object {$_ -match $pattern}|Select-Object -First 1
 if(-not $entry){throw "Resolved SDK $version is not installed"}
 $null=$entry -match $pattern
 $compiler=Join-Path $Matches[1] ($version+'/Roslyn/bincore/csc.dll')
 if(-not (Test-Path -LiteralPath $compiler)){throw 'SDK compiler not found'}
 $compiler
} finally {Pop-Location}
