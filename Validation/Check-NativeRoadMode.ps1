param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Dll))
try {
 function Method($type,$name) {
  $result=($assembly.MainModule.Types|Where-Object Name -eq $type).Methods|Where-Object Name -eq $name
  if(!$result -or !$result.HasBody){throw "Missing $type.$name"};return $result
 }
 foreach($pair in @(@('CurbLineEmissionSystem','BuildCurves'),@('IntersectionPaintSystem','Reconcile'))) {
  $method=Method $pair[0] $pair[1]
  if(!($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'AutomaticMarkingPolicy' -and $_.Operand.Name -eq 'PreservesGeometry'})){throw "Native road mode is not wired into $($pair[0]).$($pair[1])"}
 }
 $update=Method 'IntersectionPaintSystem' 'UpdateMeasured'
 if(!($update.Body.ExceptionHandlers|Where-Object HandlerType -eq Finally)){throw 'Native anchor cache must clear on exceptions'}
 $clears=@($update.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Clear' -and $_.Operand.DeclaringType.FullName -match 'Dictionary.*HighwayPaintAnchor'})
 if($clears.Count -lt 2){throw 'Native anchor cache must be cleared before and after its reconciliation pass'}
 $anchor=($assembly.MainModule.Types|Where-Object Name -eq 'HighwayPaintAnchor')
 foreach($field in $anchor.Fields|Where-Object {!$_.IsStatic}) {
  if($field.FieldType.FullName -notin @('Unity.Mathematics.float3','System.Boolean')){throw 'Native anchor cache gained a non-value or borrowed field'}
 }
 $boundary=Method 'IntersectionPaintSystem' 'PreservedNodeBoundary'
 $color=Method 'CurbLineEmissionSystem' 'NativeJunctionColor'
 if($color.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'get_YellowCenterLines'}){throw 'Opt-in native recoloring is incorrectly blocked by the legacy centerline shape setting'}
 $traffic=Method 'IntersectionPaintSystem' 'PreservedTrafficRole'
 if(!($traffic.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'NativeTrafficBoundaries'})){throw 'Native traffic role is not wired'}
 $trafficClears=@($update.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Clear' -and $_.Operand.DeclaringType.FullName -match 'Dictionary.*NativeTrafficBoundary'})
 if($trafficClears.Count -lt 2){throw 'Copied traffic-boundary cache must clear on entry and in finally'}
 $value=($assembly.MainModule.Types|Where-Object Name -eq 'NativeTrafficBoundary')
 foreach($field in $value.Fields|Where-Object {!$_.IsStatic}){if($field.FieldType.FullName -notin @('Colossal.Mathematics.Bezier4x3','System.Single','System.Boolean')){throw 'Traffic cache contains borrowed/native state'}}
 foreach($required in @('TryGetValue','HighwayPaintAnchors','Find','Yellow','IsYellowNativeNodeBoundary')) {
  if(!($boundary.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $required})){throw "Native boundary classifier lost $required"}
 }
 'PASS: native mode gates section generation and source reconciliation; per-pass endpoint cache clears in finally and stores only values; native-endpoint classification is wired. Runtime appearance remains unverified.'
} finally {$assembly.Dispose()}
