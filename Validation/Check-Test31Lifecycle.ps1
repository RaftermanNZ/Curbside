param([string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:\Users\Trevor\AppData\Local\Programs\ILSpy\Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll)
$n=0
function Check($ok,$why){if(-not $ok){throw $why};$script:n++}
try{
 $t=$a.MainModule.Types | Where-Object Name -eq CrosswalkVisualSystem
 $calls=@($t.Methods | Where-Object HasBody | ForEach-Object {$_.Body.Instructions} | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object {$_.Operand.FullName})
 Check (@($calls | Where-Object {$_ -match 'Hidden|Retire|SetComponentData<Game.Net.PedestrianLane|SetComponentData<Game.Net.CarLane'}).Count -eq 0) 'Visual helper changes routing/hides lane'
 foreach($entry in @('Instantiate','SetComponentEnabled','DestroyEntity')){Check (@($calls | Where-Object {$_ -match $entry}).Count -gt 0) "Missing lifecycle operation $entry"}
 $restore=$t.Methods | Where-Object Name -eq 'RestoreAll'
 Check (@($restore.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'ToEntityArray'}).Count -gt 0) 'Save cleanup must include copied references'
 $paint=$a.MainModule.Types | Where-Object Name -eq IntersectionPaintSystem
 $pre=$paint.Methods | Where-Object Name -eq PreSerialize
 Check (@($pre.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'RestoreAll'}).Count -gt 0) 'Missing save restoration'
 $resource=$a.MainModule.Resources | Where-Object Name -eq MedianGrassReplacement.RoadWear.png
 Check ($null -ne $resource) 'Road-wear PNG missing'
 $sha=[Security.Cryptography.SHA256]::Create();$hash=[BitConverter]::ToString($sha.ComputeHash($resource.GetResourceData())).Replace('-','').ToLowerInvariant()
 Check ($hash -eq 'fd0fc9ca35cd746298f16a6d1acf88978998f6e9a31428d1d5d51489236b048e') 'Bundled alpha/color file bytes differ'
 Write-Output "$n compiled lifecycle/resource contracts passed (native save/load still requires play testing)."
}finally{$a.Dispose()}
