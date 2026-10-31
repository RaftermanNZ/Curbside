param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Dll).Path)
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:/Program Files (x86)/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Managed/Game.dll')
$script:checks=0
function Check($condition,[string]$reason){if(-not $condition){throw $reason};$script:checks++}
function Calls($method,[string]$name){@($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name})}
try {
    $mod=$assembly.MainModule.Types | Where-Object Name -eq Mod
    $load=$mod.Methods | Where-Object Name -eq OnLoad
    $registration=@($load.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -eq 'UpdateBefore' -and $_.Operand.GenericArguments[0].Name -eq 'GeneratedPaintOwnerCleanupSystem'})
    Check ($registration.Count -eq 1) 'Missing or duplicated early owner cleanup registration'
    Check ($registration[0].Operand.GenericArguments[1].FullName -eq 'Game.Simulation.GroundHeightSystem') 'Cleanup must precede the actual faulting native system'
    $phase=$game.MainModule.Types | Where-Object FullName -eq Game.SystemUpdatePhase
    $value=($phase.Fields | Where-Object Name -eq Modification2).Constant
    $instruction=$registration[0].Previous
    $actual=if($instruction.OpCode.Code.ToString() -in @('Ldc_I4','Ldc_I4_S')){$instruction.Operand}else{[int]($instruction.OpCode.Code.ToString() -replace '^Ldc_I4_','')}
    Check ($actual -eq $value) 'Cleanup runs in the wrong modification phase'
    $type=$assembly.MainModule.Types | Where-Object Name -eq GeneratedPaintOwnerCleanupSystem
    $create=$type.Methods | Where-Object Name -eq OnCreate
    $update=$type.Methods | Where-Object Name -eq OnUpdate
    Check ((Calls $create 'RequireForUpdate').Count -eq 2) 'Cleanup scans without both paint and network-change events'
    $retire=(Calls $update 'Retire')[0]
    Check ($null -ne $retire) 'Invalid owners never retire their paint'
    foreach($name in @('ToEntityArray','ToComponentDataArray')){
        Check ((Calls $update $name)[0].Offset -lt $retire.Offset) 'Structural retirement can invalidate a live query buffer'
    }
    Check ((Calls $update 'TryGetValue').Count -eq 1) 'Shared owners are not cached within a pass'
    Check ((Calls $update 'Exists').Count -eq 1) 'Owner existence is not checked before metadata reads'
    Check ((Calls $update 'DestroyEntity').Count -eq 0) 'Cleanup bypasses native rendering deletion'
    Check ((Calls $update 'GetCullingData').Count -eq 0) 'Early owner cleanup rewrites renderer internals'
    Check ((Calls $update 'SetComponentData').Count -eq 0) 'Early cleanup modifies road definitions or transforms'
    $types=@($create.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod]} | ForEach-Object {$_.Operand.GenericArguments.FullName})
    foreach($name in @('MGRCurbMarking','MGRIntersectionPaint','MGRSignalWire','MGRVisualPowerlineRuntime','Owner','Lane','Edge','Node','Updated','Deleted')){
        Check (@($types | Where-Object {$_ -match ('[.]'+$name+'$')}).Count -gt 0) "Missing owner cleanup query contract: $name"
    }
    Write-Output "$checks compiled Test74 ordering and lifecycle checks passed. Native reproduction remains required."
} finally {$assembly.Dispose();$game.Dispose()}
