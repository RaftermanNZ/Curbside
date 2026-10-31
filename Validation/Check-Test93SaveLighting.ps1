param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop';Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $Dll));function Types($list){foreach($t in $list){$t;Types $t.NestedTypes}}
try{
 $types=@(Types $a.MainModule.Types);$load=($types|Where-Object FullName -eq 'MedianGrassReplacement.Mod').Methods|Where-Object Name -eq OnLoad
 $orders=@($load.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -in @('UpdateBefore','UpdateAfter')})
 function Before($first,$last){@($orders|Where-Object {$_.Operand.Name -eq 'UpdateBefore' -and $_.Operand.GenericArguments[0].FullName -like ('*'+$first+'*') -and $_.Operand.GenericArguments[1].FullName -like ('*'+$last+'*')}).Count -gt 0}
 if(-not (Before 'FarSideLightSystem' 'SignalAssetSaveSystem')){throw 'Broad fallback still races tracked signal restoration'}
 foreach($target in @('FarSideLightSystem','SignalAssetSaveSystem','GeneratedVisualSaveSystem','SignalWireSystem','VisualPowerlineSystem')){if(-not (Before 'SignalSaveFenceSystem' $target)){throw "Missing dependency fence before $target"}}
 if(-not (Before 'SignalSaveIndexAuditSystem' 'BeginPrefabSerializationSystem')){throw 'Index audit runs too late'}
 $fence=($types|Where-Object Name -eq SignalSaveFenceSystem).Methods|Where-Object Name -eq OnUpdate
 if(-not ($fence.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'GetEnabledData'})){throw 'Native effect readers not joined'}
 $lamp=($types|Where-Object Name -eq GardenCityLightPolicy).Methods|Where-Object Name -eq SelectLuminaire
 if(-not ($lamp.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'm_Effect' -and $_.OpCode.Code.ToString() -eq 'Ldfld'})){throw 'Native donor light not reused'}
 $strings=@($types.Methods|Where-Object HasBody|ForEach-Object {$_.Body.Instructions}|Where-Object {$_.OpCode.Code.ToString() -eq 'Ldstr'}|ForEach-Object Operand)
 if('CSG Fixture Light' -in $strings){throw 'Private light effect still created/looked up'}
 'PASS Test99: tracked restoration before fallback; native dependency fences; index audit before native serialization; original donor light effect reused.'
}finally{$a.Dispose()}
