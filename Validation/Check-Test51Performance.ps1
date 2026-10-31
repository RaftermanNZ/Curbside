param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop';Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll);$n=0
function T($name){$a.MainModule.Types|Where-Object Name -eq $name}
function M($type,$name){(T $type).Methods|Where-Object Name -eq $name}
function Calls($m){@($m.Body.Instructions|ForEach-Object Operand|Where-Object {$_ -is [Mono.Cecil.MethodReference]}|ForEach-Object FullName)}
function Check($ok,$why){if(-not $ok){throw $why};$script:n++}
try{
 $calls=Calls (M 'CurbLineEmissionSystem' 'ParkingPaintAvailable');foreach($name in @('ReadStopMarks','TryLimits','GetStopRange')){Check (@($calls|Where-Object {$_ -like "*::$name`(*"}).Count -eq 0) "Per-bay hot path still repeats $name"}
 foreach($method in @('AddParkingMarks','ParkingUpgradePreview')){Check (@(Calls (M 'CurbLineEmissionSystem' $method)|Where-Object {$_ -like '*ParkingSetback::TryLimits*'}).Count -eq 1) 'Setback limits must be computed at lane scope'}
 $calls=Calls (M 'SurfaceTextureSystem' 'OnUpdate');Check ($calls.Count -eq 0) 'Quarantined surface system still does work'
 $calls=Calls (M 'GlobalSurfaceMaterialSystem' 'Load');Check (@($calls|Where-Object {$_ -like '*::TryGetValue*'}).Count -gt 0) 'Global texture cache missing'
 $defaults=M 'Setting' 'SetSurfaceDefaults';$strings=@($defaults.Body.Instructions|Where-Object OpCode -Match 'ldstr'|ForEach-Object Operand);Check ($strings -contains '__CURBSIDE_ROAD__') 'Road A bundled default missing';Check ($strings -contains '__CURBSIDE_SIDEWALK__') 'Sidewalk A bundled default missing'
 Write-Output "$n compiled per-lane setback, bounded surface work/loading and dual-default contracts passed. These are not native frame-time measurements."
}finally{$a.Dispose()}
