param([Parameter(Mandatory=$true)][string]$Dll,[Parameter(Mandatory=$true)][string]$OutputFolder,[string]$NodeExe=$env:CURBSIDE_NODE)
$ErrorActionPreference="Stop"
& (Join-Path $PSScriptRoot "Run-CurrentChecks.ps1") @PSBoundParameters
