param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop';Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll);$n=0
function Check($ok,$reason){if(-not $ok){throw $reason};$script:n++}
try{
 $mod=$a.MainModule.Types|Where-Object Name -eq Mod
 $registrations=@($mod.Methods|Where-Object HasBody|ForEach-Object {$_.Body.Instructions}|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.DeclaringType.FullName -eq 'Game.UpdateSystem' -and $_.Operand.Name -like 'Update*'})
 foreach($name in @('CurbDecalSystem','IntersectionPaintSystem','CrosswalkPreviewSystem')){Check (@($registrations|Where-Object {$_.Operand.GenericArguments[0].Name -eq $name}).Count -eq 1) "$name registered more than once"}
 $global=$a.MainModule.Types|Where-Object Name -eq GlobalSurfaceMaterialSystem
 foreach($name in @('ApplyUv','SetUv','Apply')){
  $m=$global.Methods|Where-Object Name -eq $name
  $calls=@($m.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object Operand)
  Check (@($calls|Where-Object {$_.Name -in @('Load','LoadPng','ReadAllBytes')}).Count -eq 0) "$name reloads a texture"
  if($name -ne 'Apply'){Check (@($calls|Where-Object {$_.Name -eq 'FindObjectsOfTypeAll'}).Count -eq 0) 'UV editing scans all materials'}
 }
 $refresh=$global.Methods|Where-Object Name -eq RefreshTextures;Check (@($refresh.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Load'}).Count -eq 2) 'Only explicit texture refresh should resolve/decode selections'
 Write-Output "$n single-registration and cached UV/discovery contracts passed"
}finally{$a.Dispose()}
