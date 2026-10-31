param([Parameter(Mandatory=$true)][string]$Signals)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $Signals).Path)
try {
 if(@($a.MainModule.Types|Where-Object Name -eq NativeTimberUv).Count){throw 'Unsafe runtime timber conversion is still compiled'}
 function AllTypes($types){foreach($type in $types){$type;AllTypes $type.NestedTypes}}
 $calls=@((AllTypes $a.MainModule.Types)|ForEach-Object Methods|Where-Object HasBody|ForEach-Object {$_.Body.Instructions}|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'GeometryAsset' -and $_.Operand.Name -in @('ObtainMeshes','ReleaseMeshes','SetData','Save')})
 if($calls.Count){throw 'Pack still manually reads or mutates GeometryAsset lifecycle'}
 $source=Get-Content (Join-Path $PSScriptRoot '../../../work/garden-city-signals/pack-test19/Mod.cs') -Raw
 if($source -notmatch 'm_Name=fixture.name,m_Type=nameof\(EffectPrefab\)'){throw 'Fixture stable lookup alias missing'}
 if($source -notmatch 'CS2Native-v14-lamp-joint'){throw 'Unsafe derived cache not isolated'}
 Write-Output 'PASS: no manual GeometryAsset lifecycle calls; private fixture lookup alias; isolated import cache.'
}finally{$a.Dispose()}
