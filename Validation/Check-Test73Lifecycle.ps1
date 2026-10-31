param([Parameter(Mandatory=$true)][string]$Dll,[string]$ExpectedBuild='Test73')
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Dll).Path)
$script:checks=0
function Check($condition,[string]$reason){if(-not $condition){throw $reason};$script:checks++}
function Calls($method,[string]$name){@($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name})}
try {
    $lifecycle=$assembly.MainModule.Types | Where-Object Name -eq GeneratedVisualLifecycle
    $retire=$lifecycle.Methods | Where-Object Name -eq Retire
    $save=$lifecycle.Methods | Where-Object Name -eq RetireForSerialization
    Check ($null -ne $save) 'Explicit save cleanup is missing'
    Check (@($lifecycle.Methods|Where-Object Name -eq PreserveSerializationDeletion).Count -eq 0) 'Direct save culling rewrite remains'
    foreach($method in @($retire,$save)) {
        Check ((Calls $method 'GetCullingData').Count -eq 0) 'Retirement writes renderer data directly'
        Check ((Calls $method 'get_Saving').Count -eq 0) 'Save flag selects a different renderer retirement path'
    }
    foreach($method in @($retire,$save)) {
        Check ((Calls $method 'Unregister').Count -eq 1) 'Owned search entries are not removed'
        Check (@($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -eq 'AddComponent' -and $_.Operand.GenericArguments.FullName -contains 'Game.Common.Deleted'}).Count -eq 1) 'Native Deleted lifecycle is bypassed'
        Check ((Calls $method 'DestroyEntity').Count -eq 0) 'Retirement destroys entities before native rendering cleanup'
    }
    $saveSystem=$assembly.MainModule.Types | Where-Object Name -eq GeneratedVisualSaveSystem
    $callback=$saveSystem.Methods | Where-Object Name -eq PreSerialize
    Check ((Calls $callback 'RetireForSerialization').Count -eq 1) 'Serialization uses the ordinary frame cleanup route'
    $accessors=@($assembly.MainModule.Types.Methods | Where-Object HasBody | Where-Object {(Calls $_ 'GetCullingData').Count -ne 0})
    Check ($accessors.Count -eq 0) 'Curbside still accesses native culling records directly'
    Check (@($assembly.MainModule.Types | Where-Object Name -eq SignalConstructionGuardSystem).Count -eq 0) 'Road/bulldozer signal guard is still compiled'
    $panel=$assembly.MainModule.Types | Where-Object Name -eq CurbsidePanelSystem
    $labels=@(@($panel.Methods)+@($panel.NestedTypes.Methods) | Where-Object HasBody | ForEach-Object {$_.Body.Instructions} | Where-Object {$_.OpCode.Code.ToString() -eq 'Ldstr'} | ForEach-Object Operand)
    Check ($labels -contains ('The Curbside Mod '+$ExpectedBuild)) 'Diagnostic snapshot labels the wrong build'
    Write-Output "$checks compiled $ExpectedBuild lifecycle contracts passed. Native crash reproduction is still required."
} finally { $assembly.Dispose() }
