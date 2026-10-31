param([Parameter(Mandatory=$true)][string]$Curbside,[Parameter(Mandatory=$true)][string]$Signals)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $Curbside).Path);$b=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $Signals).Path)
$script:count=0
function Check($value,$message){if(-not $value){throw $message};$script:count++}
function Calls($m,$n){@($m.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $n})}
function RemovesEffect($m){@($m.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -eq 'Remove' -and $_.Operand.GenericArguments[0].Name -eq 'EffectSource'}).Count -eq 1}
try {
 $policy=$a.MainModule.Types|Where-Object Name -eq 'GardenCityLightPolicy'
 foreach($method in @('Resolve','SelectSuppressed','SelectSuppressedLamp')){Check (RemovesEffect ($policy.Methods|Where-Object Name -eq $method)) "Inherited effects remain in $method"}
 $names=$a.MainModule.Types|Where-Object Name -eq 'SignalStreetNameSystem'
 $mask=$names.Methods|Where-Object Name -eq 'RefreshCoverage'
 foreach($call in @('GetInstanceID','get_updateCount','get_glyphTable','get_characterTable')){Check ((Calls $mask $call).Count -gt 0) "Atlas invalidation misses $call"}
 $build=$names.Methods|Where-Object Name -eq 'BuildLetterGeometry'
 Check ($build.Parameters[1].ParameterType.Name -eq 'TMP_TextInfo') 'Street-name builder still takes only a single material page'
 Check (@($build.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'materialCount'}).Count -gt 0) 'Additional atlas pages are ignored'
 $system=$a.MainModule.Types|Where-Object Name -eq 'FarSideLightSystem'
 Check ((Calls ($system.Methods|Where-Object Name -eq 'ApplyCrossingPlan') HideSignal).Count -gt 0) 'Unused native signal actors no longer suppressed'
 Check ((Calls ($system.Methods|Where-Object Name -eq 'EdgeMedianSignals') HideSignal).Count -eq 1) 'Edge-owned median signals not reconciled'
 $pack=$b.MainModule.Types|Where-Object Name -eq 'PackSystem'
 $update=$pack.Methods|Where-Object Name -eq 'OnUpdate'
 $strings=@($update.Body.Instructions|Where-Object {$_.OpCode.Code.ToString() -eq 'Ldstr'}|ForEach-Object Operand)
 Check ($strings -contains 'Curbside.Active.v1') 'Optional model pack does not wait for active Curbside'
 Check (@($a.MainModule.AssemblyReferences|Where-Object Name -eq 'GardenCitySignals').Count -eq 0) 'Curbside has a mandatory model assembly dependency'
 Check (@($a.MainModule.Types|Where-Object {$_.Interfaces.InterfaceType.FullName -contains 'Game.Modding.IMod'}).Count -eq 1) 'A second model mod was bundled into Curbside'
 $uv=$b.MainModule.Types|Where-Object Name -eq 'NativeTimberUv'
 Check ($null -eq $uv) 'Unsafe native trunk conversion remains compiled'
 Write-Output "PASS: $count Test78 optional-model, font-page, native-effect and median-signal contracts. Native scene verification remains required."
}finally{$a.Dispose();$b.Dispose()}
