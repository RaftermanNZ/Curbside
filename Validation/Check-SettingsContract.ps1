param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Dll))
try {
 $setting=$assembly.MainModule.Types|Where-Object Name -eq Setting
 $reset=$setting.Methods|Where-Object Name -eq SetDefaults
 $stores=@($reset.Body.Instructions|Where-Object {$_.OpCode.Code -eq 'Stfld' -and $_.Operand.Name -eq '_runtimeEventsEnabled'})
 $values=$reset.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'SetDefaultValues'}
 if($stores.Count -ne 2 -or $stores[0].Offset -ge $values.Offset -or $stores[1].Offset -le $values.Offset){throw 'Reset does not bracket assignments with runtime event suppression'}
 if(!($reset.Body.ExceptionHandlers|Where-Object HandlerType -eq Finally)){throw 'Reset must restore runtime event state on failure'}
 $calls=@($reset.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.DeclaringType.Name+'::'+$_.Operand.Name})
 foreach($expected in @('Setting::ApplyAndSave','MedianMaterialSystem::RequestRefresh','GlobalSurfaceMaterialSystem::RequestRefresh','CurbDecalSystem::RequestRefresh','CurbLineEmissionSystem::RequestRoadRefresh','FixedParkingSystem::RequestScan','IntersectionPaintSystem::RequestRefresh','JunctionMarkingSystem::RequestRefresh','TextureCatalog::Invalidate','SurfaceTextureCatalog::Invalidate','DecalCatalog::Invalidate')){
  if(@($calls|Where-Object {$_ -eq $expected}).Count -ne 1){throw "Missing or duplicated reset notification: $expected"}
 }
 if($calls|Where-Object {$_ -match 'EntityManager::|Resources::FindObjects|JobHandle::Complete'}){throw 'Settings reset must defer renderer work to its owning systems'}
 'PASS: reset suppresses intermediate notifications, restores its event flag in finally, queues each owning-system refresh once and performs no direct native mutation/scan/job fence.'
} finally {$assembly.Dispose()}
