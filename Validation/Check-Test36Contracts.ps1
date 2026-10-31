param([string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:\Users\Trevor\AppData\Local\Programs\ILSpy\Mono.Cecil.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll)
$checks=0
function Assert-Check($ok,$message){if(-not $ok){throw $message};$script:checks++}
try {
 $types=$assembly.MainModule.Types
 $setting=$types | Where-Object Name -eq 'Setting'
 foreach($name in @('AlleyLineOffset','AlleyLineStopGap')){
  $property=$setting.Properties | Where-Object Name -eq $name
  Assert-Check (@($property.CustomAttributes | Where-Object {$_.AttributeType.Name -eq 'SettingsUIHiddenAttribute'}).Count -eq 1) "Legacy alley slider visible: $name"
 }
 $emission=$types | Where-Object Name -eq 'CurbLineEmissionSystem'
 foreach($method in $emission.Methods){if(-not $method.HasBody){continue};foreach($i in $method.Body.Instructions){if($i.Operand -is [Mono.Cecil.MethodReference]){Assert-Check ($i.Operand.Name -notin @('get_AlleyLineOffset','get_AlleyLineStopGap')) 'Runtime still reads obsolete alley placement'}}}
 foreach($name in @('ApproachStopTool','ParkingUpgradeTool','DecalUpgradeTool','NodeLineUpgradeTool','FarSideLightUpgradeTool')){
  $type=$types | Where-Object Name -eq $name
  $method=$type.Methods | Where-Object Name -eq 'OnUpdate'
  $calls=@($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object {$_.Operand.FullName})
  Assert-Check (@($calls | Where-Object {$_ -like '*UpgradeInputPolicy::RemovePressed*'}).Count -gt 0) "Missing remove policy: $name"
  Assert-Check (@($calls | Where-Object {$_ -like '*UpgradeInputPolicy::get_PointerBlocked*'}).Count -gt 0) "Missing UI guard: $name"
 }
 $approach=$types | Where-Object Name -eq 'ApproachStopTool'
 $update=$approach.Methods | Where-Object Name -eq 'OnUpdate'
 Assert-Check (@($update.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'RefreshEndpoint'}).Count -eq 2) 'Both endpoint refresh calls required'
 Assert-Check (@($assembly.MainModule.Resources | Where-Object Name -like '*.svg').Count -eq 33) '33 icons required'
 Write-Output "$checks compiled structural checks passed. These do not run the native game world."
} finally {$assembly.Dispose()}
