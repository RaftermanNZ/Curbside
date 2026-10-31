param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:\Users\Trevor\AppData\Local\Programs\ILSpy\Mono.Cecil.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll)
$count=0
function Verify($ok,$message){if(-not $ok){throw $message};$script:count++}
try {
 $types=$assembly.MainModule.Types
 $tool=$types|Where-Object Name -eq 'DecalUpgradeTool'
 $calls=@(($tool.Methods|Where-Object Name -eq 'OnUpdate').Body.Instructions|ForEach-Object Operand|Where-Object {$_ -is [Mono.Cecil.MethodReference]}|ForEach-Object FullName)
 Verify (@($calls|Where-Object {$_ -like '*EntityManager::AddComponent*' -and $_ -like '*MGRHideCenterline*'}).Count -gt 0) 'Centerline removal must persist native hiding'
 Verify (@($calls|Where-Object {$_ -like '*SegmentUpgradeSystem::Apply*'}).Count -eq 1) 'New tools must reach click handler'
 foreach($typeName in @('DecalUpgradeTool','UpgradePreviewSystem')){
  $type=$types|Where-Object Name -eq $typeName
  $targetCalls=@(($type.Methods|Where-Object Name -eq 'OnUpdate').Body.Instructions|ForEach-Object Operand|Where-Object {$_ -is [Mono.Cecil.MethodReference]}|ForEach-Object FullName)
  Verify (@($targetCalls|Where-Object {$_ -like '*SectionUpgradeTargeting::TryRoad*'}).Count -eq 1) "Preview/click targeting mismatch: $typeName"
 }
 $transfer=$types|Where-Object Name -eq 'RoadAttachmentTransferSystem'
 $selection=$transfer.NestedTypes|Where-Object Name -eq 'Selection'
 foreach($name in @('LineVisibility','HideCenter','CenterStyle','ParkingLeft','ParkingRight','ParkingBans','Powerlines','Decals','HiddenLeft','HiddenRight','StreetLighting','LightingPrevious')){Verify (@($selection.Fields|Where-Object Name -eq $name).Count -eq 1) "Missing transfer field $name"}
 $segment=$types|Where-Object Name -eq 'SegmentUpgradeSystem'
 $reset=$segment.Methods|Where-Object Name -eq 'Apply'
 $resetCalls=@($reset.Body.Instructions|ForEach-Object Operand|Where-Object {$_ -is [Mono.Cecil.MethodReference]}|ForEach-Object FullName)
 foreach($name in @('Forget','ResetRoad','ResetRoadAppearance','ResetApproach')){Verify (@($resetCalls|Where-Object {$_ -like ('*::'+$name+'(*')}).Count -gt 0) "Reset missing $name reconciliation"}
 Verify (@(($segment.Methods|Where-Object Name -eq 'OnUpdate').Body.Instructions|Where-Object {$_.OpCode.Code -eq 'Call'}).Count -eq 0) 'Reset system must not scan every frame'
 $mark=($types|Where-Object Name -eq 'CurbLineEmissionSystem').NestedTypes|Where-Object Name -eq 'MarkCurve'
 Verify (@($mark.Fields|Where-Object Name -eq 'Protected').Count -eq 1) 'Generated stop bars need visibility protection'
 $visual=$types|Where-Object Name -eq 'CrosswalkVisualSystem'
 Verify (@(($visual.Methods|Where-Object Name -eq 'Suppress').Body.Instructions|Where-Object {$_.Operand -eq 'crosswalk.suppress'}).Count -eq 0) 'Per-application suppression diagnostics flood snapshots'
 Write-Output "$count compiled state integration checks passed; native gameplay verification is still required."
} finally {$assembly.Dispose()}
