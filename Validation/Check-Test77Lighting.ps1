param([Parameter(Mandatory=$true)][string]$Curbside,[Parameter(Mandatory=$true)][string]$Signals)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $Curbside).Path)
$b=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $Signals).Path)
$script:count=0
function Check($value,$message){if(-not $value){throw $message};$script:count++}
function Calls($method,$name){@($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name})}
try {
 $system=$a.MainModule.Types|Where-Object Name -eq FarSideLightSystem
 foreach($method in @('Restore','PreSerialize')){Check ((Calls ($system.Methods|Where-Object Name -eq $method) ClearGeneratedLamps).Count -eq 1) "Missing generated lamp cleanup in $method"}
 $retire=$system.Methods|Where-Object Name -eq RetireGeneratedLamps
 Check ((Calls $retire RetireForSerialization).Count -eq 1) 'Generated lamp save cleanup bypasses native renderer retirement'
 Check ((Calls $retire DestroyEntity).Count -eq 0) 'Generated lamp directly destroys live rendering state'
 $create=$system.Methods|Where-Object Name -eq EnsureGeneratedLamp
 Check ((Calls $create CreateEntity).Count -eq 1) 'No bounded native-archetype fallback for roads without lamps'
 Check ((Calls $create Track).Count -eq 1) 'Generated lamp not registered with native search lifecycle'
 Check (@($create.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -eq 'AddComponent' -and $_.Operand.GenericArguments[0].Name -eq 'Owner'}).Count -eq 0) 'Generated lamp introduces a native network owner dependency'
 foreach($type in @('GeneratedVisualSaveSystem','GeneratedVisualRetirementSystem')){
  $method=($a.MainModule.Types|Where-Object Name -eq $type).Methods|Where-Object Name -eq OnCreate
  Check (@($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.GenericArguments[0].Name -eq 'MGRSignalLuminaireRuntime'}).Count -gt 0) "Lamp absent from $type"
 }
 $policy=$a.MainModule.Types|Where-Object Name -eq GardenCityLightPolicy
 $lamp=$policy.Methods|Where-Object Name -eq SelectLuminaire
 $strings=@($lamp.Body.Instructions|Where-Object {$_.OpCode.Code.ToString() -eq 'Ldstr'}|ForEach-Object Operand)
 foreach($style in @('WireLuminaire','RigidLuminaire','GardenLuminaire')){Check ($strings -contains $style) "Missing fixture style $style"}
 $hidden=$policy.Methods|Where-Object Name -eq SelectSuppressedLamp
 Check (@($hidden.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -eq 'Remove' -and $_.Operand.GenericArguments[0].Name -eq 'EffectSource'}).Count -eq 1) 'Hidden junction lamps still emit native light effects'
 $pack=$b.MainModule.Types|Where-Object Name -eq PackSystem
 $update=$pack.Methods|Where-Object Name -eq OnUpdate
 Check ((Calls $update GetSurfaceAsset).Count -eq 1) 'Timber does not reference a native surface'
 Check ((Calls $update ObtainMaterials).Count -eq 0) 'Timber unnecessarily allocates native Unity materials'
 Check ((Calls $update ObtainMeshes).Count -eq 0) 'Timber reads/mutates native meshes'
 Write-Output "PASS: $count compiled lighting, native-surface and save-lifecycle contracts. In-game visuals and removal matrix remain untested."
} finally {$a.Dispose();$b.Dispose()}
