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
 foreach($name in @('AddAreaPrefab','AllowsArea')){
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
 if(!($method.Body.ExceptionHandlers|Where-Object HandlerType -eq Finally)){throw 'Batch provenance snapshot is not cleared in finally'}
 $method=$global.Methods|Where-Object Name -eq UpdateMeasured
 if(@($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'RefreshCatalog'}).Count -ne 1){throw 'Catalog refresh contract changed'}
 if(@($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'RefreshAreaCatalog'}).Count -ne 1){throw 'Area catalog refresh contract changed'}
 'PASS: provenance before texture writes, no asset load/unload/destroy/private writes/native-container access, transient batch snapshot cleared in finally.'
}finally{$assembly.Dispose()}
