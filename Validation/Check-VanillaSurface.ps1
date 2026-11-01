param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Dll))
try{
 $types=$assembly.MainModule.Types
 $owner=$types|Where-Object Name -eq VanillaSurfaceOwnership
 foreach($method in $owner.Methods){foreach($i in $method.Body.Instructions){if($i.Operand -is [Mono.Cecil.MethodReference] -and $i.Operand.Name -in @('Load','LoadData','Unload','Destroy','SetValue','GetNativeBatchGroups','GetNativeBatchInstances','CompleteAllTrackedJobs')){throw ('Ownership inspection uses forbidden mutation/loading: '+$i.Operand)}}}
 $global=$types|Where-Object Name -eq GlobalSurfaceMaterialSystem
 if($owner.Methods|Where-Object Name -in @('Explain','NativeAreaReport')){throw 'Temporary per-surface tracing remains'}
 if($global.Fields|Where-Object Name -Like '*explain*'){throw 'Temporary surface tracing state remains'}
 # Cataloging can include historical/custom recovery targets, but NEW writes
 # must still use the exact allowlist through AllowsArea and ApplyProperty.
 foreach($name in @('AllowsArea')){
  $targetMethod=$owner.Methods|Where-Object Name -eq $name
  if(!($targetMethod.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'TargetArea'})){throw 'Area scope bypasses exact pavement allowlist'}
 }
 $policy=$types|Where-Object Name -eq GlobalSurfacePolicy
 $assetKind=$policy.Methods|Where-Object Name -eq AssetKind
 if($assetKind.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -in @('Contains','ToLowerInvariant')}){throw 'Broad substring surface classification remains'}
 $areaAdd=$owner.Methods|Where-Object Name -eq AddAreaPrefab
 if(@($areaAdd.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'AddAreaName'}).Count -ne 1 -or !($areaAdd.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'AreaBatchName'})){throw 'Area scope must register only the native area-batch wrapper'}
 foreach($method in $owner.Methods){foreach($i in $method.Body.Instructions){if($i.Operand -is [Mono.Cecil.MethodReference] -and $i.Operand.Name -in @('SetTexture','SetFloat','SetVector','GetAreaBatch','GetAreaBuffer','SetComponentData','DestroyEntity')){throw 'Area ownership must remain read-only'}}}
 $areaRefresh=$global.Methods|Where-Object Name -eq RefreshAreaCatalog
 if(!($areaRefresh.Body.ExceptionHandlers|Where-Object HandlerType -eq Finally)){throw 'Area-prefab query copy lacks guaranteed disposal'}
 $method=$global.Methods|Where-Object Name -eq ApplyProperty
 $guard=@($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'VanillaSurfaceOwnership' -and $_.Operand.Name -eq 'Allows'})
 $writes=@($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'SetTexture'})
 if($guard.Count -ne 1 -or $writes.Count -ne 1 -or $guard[0].Offset -gt $writes[0].Offset){throw 'Texture write bypasses provenance gate'}
 $method=$global.Methods|Where-Object Name -eq Apply
 foreach($required in @('AssetKind','get_SurfaceTexturesOnAssets')){if(!($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $required})){throw 'Experimental surface route must retain its setting and exact classification gates'}}
 if(!($method.Body.ExceptionHandlers|Where-Object HandlerType -eq Finally)){throw 'Batch provenance snapshot is not cleared in finally'}
 $method=$global.Methods|Where-Object Name -eq UpdateMeasured
 if(@($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'RefreshCatalog'}).Count -ne 2){throw 'Expected explicit-refresh and once-disabled recovery catalogs only'}
 if(@($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'RefreshAreaCatalog'}).Count -ne 2){throw 'Expected explicit-refresh and once-disabled area catalogs only'}
 if(!($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq '_disabledRestored'})){throw 'Disabled recovery lacks one-shot state'}
 $recover=$global.Methods|Where-Object Name -eq RecoverUntrackedAreas
 $recoveryGuard=@($recover.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'SurfaceRestorePolicy' -and $_.Operand.Name -eq 'CanRecover'})
 $recoveryWrites=@($recover.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'SetTexture'})
 if($recoveryGuard.Count -ne 1 -or $recoveryWrites.Count -ne 1 -or $recoveryGuard[0].Offset -gt $recoveryWrites[0].Offset){throw 'Recovery texture write bypasses ownership gate'}
 $restore=$global.Methods|Where-Object Name -eq Restore
 $retained=@($restore.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'ReferencedTextures'})
 $destroy=@($restore.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Destroy'})
 if($retained.Count -ne 1 -or $destroy.Count -ne 1 -or $retained[0].Offset -gt $destroy[0].Offset){throw 'Texture disposal bypasses live-material reference collection'}
 'PASS: provenance before texture writes, no asset load/unload/destroy/private writes/native-container access, transient batch snapshot cleared in finally.'
}finally{$assembly.Dispose()}
