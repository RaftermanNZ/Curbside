param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Dll))
function Calls($m,$name){@($m.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name})}
try {
 $types=$a.MainModule.Types
 $pack=$types|Where-Object Name -eq PackSystem
 $adopt=$pack.Methods|Where-Object Name -eq AdoptPreparedPack
 if((Calls $adopt 'Expired').Count -ne 1){throw 'Partial native readiness must use bounded progress policy'}
 if((Calls $adopt 'ImportPath').Count -ne 0){throw 'Native discovery must not import assets'}
 if(!($pack.Methods|Where-Object Name -eq OnGameLoadingComplete)){throw 'Negative boot discovery cannot retry on a new world load'}
 $parking=($types|Where-Object Name -eq CurbLineEmissionSystem).Methods|Where-Object Name -eq AddParkingMarks
 if((Calls $parking 'Suppressed').Count){throw 'Automatic No Markings flag still suppresses explicit parking'}
 if(!(Calls $parking 'ParkingMode').Count){throw 'Explicit parking must still require saved side intent'}
 $cross=($types|Where-Object Name -eq CrosswalkUpgradeSystem).Methods|Where-Object Name -eq SetChoice
 if((Calls $cross 'RequestCrossing').Count -ne 1){throw 'Clicked crossing is not prioritized'}
 $paint=$types|Where-Object Name -eq IntersectionPaintSystem
 $update=$paint.Methods|Where-Object Name -eq UpdateMeasured
 if(!($update.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq '_clickedCrossings'})){throw 'Priority queue is not drained'}
 $restore=$paint.Methods|Where-Object Name -eq Restore
 if(!($restore.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq '_clickedCrossings'})){throw 'Priority queue retains stale world IDs'}
 $global=$types|Where-Object Name -eq GlobalSurfaceMaterialSystem
 $apply=$global.Methods|Where-Object Name -eq ApplyProperty
 if((Calls $apply 'SharedParkingAtlas').Count -ne 1){throw 'Shared parking material exclusion is not wired'}
 if((Calls $apply 'Allows')[0].Offset -ge (Calls $apply 'SharedParkingAtlas')[0].Offset){throw 'Scope guard precedes ownership evidence'}
 if((Calls $apply 'SharedParkingAtlas')[0].Offset -ge (Calls $apply 'SetTexture')[0].Offset){throw 'Scope guard runs after material write'}
 if($types|Where-Object Name -eq NetworkAtlasCache){throw 'Unvalidated GPU compositor remains compiled'}
 $mixed=$global.Methods|Where-Object Name -eq ApplyParkingAtlas
 if((Calls $mixed 'Allows')[0].Offset -ge (Calls $mixed 'Compose')[0].Offset){throw 'Parking composition bypasses native ownership'}
 if((Calls $mixed 'Compose')[0].Offset -ge (Calls $mixed 'SetTexture')[0].Offset){throw 'Parking patch written before composition succeeds'}
 $compose=($types|Where-Object Name -eq ParkingAtlas).Methods|Where-Object Name -eq Compose
 if(!(Calls $compose 'CopyTexture').Count -or (Calls $compose 'ReadPixels').Count){throw 'Parking preparation must use GPU copy without CPU readback'}
 if(!(Calls $compose 'Destroy').Count -or !(Calls $compose 'set_active').Count){throw 'Temporary render target cleanup/state restoration missing'}
 $uv=$global.Methods|Where-Object Name -eq ApplyUv
 if(!(Calls $uv 'SetUv').Count){throw 'Ordinary sidewalk tiling path was removed'}
 Write-Output 'PASS: original sidewalk routing, exact native parking scope, ownership before composition, GPU copy/no CPU readback, temporary target cleanup, explicit parking intent, crosswalk queue/reset and bounded signal readiness. Runtime validation pending.'
}finally{$a.Dispose()}
