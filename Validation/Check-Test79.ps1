param([string]$Curbside,[Parameter(Mandatory=$true)][string]$Signals)
& (Join-Path $PSScriptRoot 'Check-Test80Safety.ps1') -Signals $Signals
