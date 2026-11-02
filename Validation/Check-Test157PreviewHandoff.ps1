param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Dll))
$script:checks=0
function Check($ok,$message){if(!$ok){throw $message};$script:checks++}
function Method($type,$name){($assembly.MainModule.Types|Where-Object Name -eq $type).Methods|Where-Object Name -eq $name}
function Calls($method,$name){@($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name})}
try {
 $suppress=Method CrosswalkVisualSystem SuppressPreviewLine
 foreach($type in @('Temp','Lane','Deleted','CarLane','ParkingLane','PedestrianLane','TrackLane','PrefabRef')){
  Check (@($suppress.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -eq 'HasComponent' -and $_.Operand.GenericArguments[0].Name -eq $type}).Count -gt 0) "Preview render scope missing $type"
 }
 $alias=Method CrosswalkVisualSystem SuppressGeometry
 Check ((Calls $alias Instantiate).Count -eq 1) 'Expected one guarded definition-copy site'
 Check ((Calls $alias ContainsKey)[0].Offset -lt (Calls $alias Instantiate)[0].Offset) 'Existing alias is not reused before allocation'
 Check ((Calls $alias TryGetValue)[0].Offset -lt (Calls $alias Instantiate)[0].Offset) 'Definition cache must precede allocation'
 Check ((Calls $alias WithoutPaint).Count -eq 1) 'Alias does not use the established render-layer-only data policy'
 foreach($method in ($assembly.MainModule.Types|Where-Object Name -eq CrosswalkVisualSystem).Methods|Where-Object HasBody){
  foreach($i in $method.Body.Instructions){
   if($i.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $i.Operand.Name -in @('AddComponent','RemoveComponent')){
    Check ($i.Operand.GenericArguments[0].Name -ne 'Hidden') 'Render aliases must not fight native Hidden flags'
   }
   if($i.Operand -is [Mono.Cecil.MethodReference]){if($i.Operand.Name -in @('CompleteAllTrackedJobs','GetCullingData','GetUnsafePtr','DisposeUnsafe')){throw 'Render aliases mutate or globally fence native rendering memory'}}
  }
 }
 Check ($alias.Body.ExceptionHandlers.Count -gt 0) 'Failed alias initialization has no owned-copy cleanup'
 $layers=Method CrosswalkVisualPolicy WithoutPaint
 $written=@($layers.Body.Instructions|Where-Object {$_.OpCode.Code.ToString() -eq 'Stfld'}|ForEach-Object {$_.Operand.Name})
 Check (($written -join '|') -eq 'm_GameLayers|m_EditorLayers') 'Render alias changes fields beyond layer visibility'
 $restore=Method CrosswalkVisualSystem RestorePlainPreviews
 Check ((Calls $restore RestoreSource).Count -eq 2) 'Restore must cover native-copied temporary references and tracked previews'
 Check (@($restore.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq '_temporaryReferences'}).Count -eq 1) 'Placement restore must use temp-only references, not all city references'
 Check ((Calls (Method PaintPreviewVisibilitySystem Restore) RestorePlainPreviews).Count -eq 1) 'Paint guard fails to release aliases'
 Check ((Calls (Method SignalPreviewRestoreSystem OnUpdate) Restore).Count -eq 2) 'Placement commit must restore paint and signal refs'
 $load=Method Mod OnLoad
 $restorer=@($load.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -like 'Update*' -and $_.Operand.GenericArguments[0].Name -eq 'SignalPreviewRestoreSystem'})
 Check ($restorer.Count -eq 1 -and $restorer[0].Operand.Name -eq 'UpdateBefore' -and $restorer[0].Operand.GenericArguments[1].Name -eq 'ToolApplySystem') 'Restore must run once before both object and network placement commit'
 Check ((Calls (Method DecalVisibilitySystem PreSerialize) Restore).Count -ge 2) 'Save coordinator entry no longer restores preview guard'
 Check ((Calls (Method IntersectionPaintSystem PreSerialize) RestoreAll).Count -eq 1) 'Save cleanup does not restore aliases before prefab serialization'
 $all=Method CrosswalkVisualSystem RestoreAll
 Check ((Calls $all RestoreSource)[0].Offset -lt (Calls $all DestroyEntity)[0].Offset) 'Owned aliases destroyed before original refs are restored'
 $maintenance=Method IntersectionPaintSystem MaintainReplacement
 Check ((Calls $maintenance UnchangedPreview).Count -eq 1) 'Replacement continuity not limited to unchanged geometry'
 $refresh=Method CrosswalkVisualSystem RefreshPreviewContinuity
 foreach($name in @('get_frameCount','get_IsEmptyIgnoreFilter','SamePath','ExceptWith')){Check ((Calls $refresh $name).Count -gt 0) "Continuity lacks bounded/safe check $name"}
 Check ((Calls (Method IntersectionPaintSystem UpdateMeasured) RefreshPreviewContinuity).Count -eq 1) 'Continuity evidence not refreshed before replacement maintenance'
 $find=Method SignalPreviewSystem FindSource
 foreach($name in @('AddOwner','NativeSource','TryPreviewVisual')){Check ((Calls $find $name).Count -gt 0) "Nested signal source lacks scope guard $name"}
 $add=Method SignalPreviewSystem AddOwner
 Check ((Calls $add Contains).Count -eq 1) 'Nested preview ancestry lacks cycle detection'
 Check (@($add.Body.Instructions|Where-Object {$_.OpCode.Code.ToString() -eq 'Ldc_I4_S' -and [int]$_.Operand -eq 16}).Count -eq 1) 'Nested preview ancestry must have a fixed visit budget'
 foreach($field in ($assembly.MainModule.Types|Where-Object {$_.Name -in @('SignalPreviewSystem','CrosswalkVisualSystem','PaintPreviewVisibilitySystem')}).Fields|Where-Object {!$_.IsStatic}){
  Check (!$field.FieldType.IsPointer -and $field.FieldType.FullName -notmatch 'NativeArray|DynamicBuffer|NativeList') "Borrowed preview memory retained: $($field.Name)"
 }
 "$checks Test157 compiled ownership, preview scope, bounded ancestry, commit/save cleanup and lifetime contracts passed. No runtime/FPS claim."
}finally{$assembly.Dispose()}
