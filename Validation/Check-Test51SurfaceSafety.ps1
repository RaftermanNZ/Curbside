param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll);$n=0
function Check($ok,$why){if(-not $ok){throw $why};$script:n++}
function T($name){$a.MainModule.Types|Where-Object Name -eq $name}
function M($type,$name){(T $type).Methods|Where-Object Name -eq $name}
function Calls($m){@($m.Body.Instructions|ForEach-Object Operand|Where-Object {$_ -is [Mono.Cecil.MethodReference]}|ForEach-Object FullName)}
try{
 foreach($name in @('MGRSurfaceSlots','MGRParkingHatching')){foreach($f in (T $name).Fields){Check ($f.FieldType.FullName -eq 'System.Byte') 'Saved upgrade contains a private asset/entity reference'}}
 Check ((M 'SurfaceTextureSystem' 'CompositionVariant') -eq $null) 'Unsafe private composition generator remains'
 Check ((M 'SurfaceTextureSystem' 'Piece') -eq $null) 'Unsafe private piece generator remains'
 foreach($m in (T 'SurfaceTextureSystem').Methods){if(!$m.HasBody){continue};foreach($call in (Calls $m)){Check ($call -notmatch '(SetComponentData|AddComponent|RemoveComponent)<Game\.(Buildings\.Building|Net\.(Edge|Road|Upgraded|Lane|CarLane|ParkingLane))>') 'Surface system mutates simulation/topology'}}
 $calls=Calls (M 'CurbLineEmissionSystem' 'ParkingHatchPlan');Check (@($calls|Where-Object {$_ -like '*::SetComponentData*' -or $_ -like '*::AddComponent*' -or $_ -like '*::RemoveComponent*'}).Count -eq 0) 'Hatch planning changes parking/access'
 foreach($m in (T 'GlobalSurfaceMaterialSystem').Methods){if(!$m.HasBody){continue};foreach($call in (Calls $m)){Check ($call -notmatch 'EntityManager::|PrefabSystem::|RenderPrefab::Clone') 'Global texture renderer mutates the network or clones assets'}}
 Write-Output "$n compiled surface save-safety and paint-only hatching contracts passed. Native disabled-mod save/reload remains unverified."
}finally{$a.Dispose()}
