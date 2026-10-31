param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Dll))
try{
 $type=$assembly.MainModule.Types|Where-Object Name -eq FarSideLightSystem
 $update=$type.Methods|Where-Object Name -eq UpdateMeasuredCore
 $calls=@($update.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'SignalBindingPolicy' -and $_.Operand.Name -eq 'Changed'})
 if($calls.Count -ne 2){throw 'Visible and suppressed native binding reconciliation are not both connected'}
 foreach($method in $type.Methods){if(!$method.HasBody){continue};foreach($instruction in $method.Body.Instructions){
  if($instruction.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Stfld -and $instruction.Operand -is [Mono.Cecil.FieldReference] -and $instruction.Operand.DeclaringType.FullName -eq 'Game.Objects.TrafficLight' -and $instruction.Operand.Name -ne 'm_GroupMask1'){
   throw "Curbside signal renderer writes native vehicle state/membership: $($instruction.Operand.Name)"
  }
 }}
 $policy=$assembly.MainModule.Types|Where-Object Name -eq SignalBindingPolicy
 $changed=$policy.Methods|Where-Object Name -eq Changed
 if($changed.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -or $_.Operand -is [Mono.Cecil.MethodReference]}){throw 'Binding comparison must remain scalar-only; no polling, phase/color or timing controller calls'}
 'PASS: visible/suppressed binding checks connected; scalar-only comparison; renderer does not write vehicle membership or native light state.'
}finally{$assembly.Dispose()}
