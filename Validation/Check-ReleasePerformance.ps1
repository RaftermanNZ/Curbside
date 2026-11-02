param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Dll))
try {
 function Method($type,$name){$m=($a.MainModule.Types|Where-Object Name -eq $type).Methods|Where-Object Name -eq $name;if(!$m.HasBody){throw "Missing $type.$name"};$m}
 function Calls($m,$type,$name){@($m.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -like $type -and $_.Operand.Name -eq $name})}
 $m=Method IntersectionPaintSystem UpdateMeasured
 if(!(Calls $m 'ManagedWorkIndex*' Rotate)){throw 'Intersection caller lost bounded audit'}
 if(Calls $m 'Dictionary*' GetEnumerator){throw 'Intersection steady update walks full replacement dictionary'}
 $rotation=Calls $m 'ManagedWorkIndex*' Rotate
 if($rotation[0].Previous.OpCode.Code -ne 'Ldc_I4_S' -or [int]$rotation[0].Previous.Operand -ne 64){throw 'Intersection audit budget changed'}
 if(!(Calls $m JobHandle Complete)){throw 'Native dependency completion lost'}
 foreach($name in @('Track','Retire','RetireSaveBatch')){
  $life=Method GeneratedVisualLifecycle $name
  if(!(Calls $life GeneratedPaintOwnerCleanupSystem $(if($name -eq 'Track'){'Track'}else{'Untrack'}))){throw "Lifecycle index wiring missing: $name"}
 }
 $cleanup=Method GeneratedPaintOwnerCleanupSystem UpdatePerformanceCore
 if(!(Calls $cleanup 'ManagedWorkIndex*' CopyOwner) -or !(Calls $cleanup 'ManagedWorkIndex*' Rotate)){throw 'Cleanup lost affected-owner or fallback work'}
 $surface=Method GlobalSurfaceMaterialSystem Apply
 if(!(Calls $surface VanillaSurfaceOwnership get_BatchMaterials)){throw 'Rediscovery lost renderer material catalog'}
 $settings=Method SettingsSaveSystem WritePending
 $state=($a.MainModule.Types|Where-Object Name -eq SettingsSaveSystem).NestedTypes|Where-Object Name -like '*WritePending*'
 if(!$state -or !(Calls ($state.Methods|Where-Object Name -eq MoveNext) 'DeferredSettingsWrite*' Complete)){throw 'Async settings completion no longer retains retry state'}
 $color=Method CurbLineEmissionSystem NativeJunctionColor
 if(!(Calls $color NativeJunctionColorPolicy ThemedTarget) -or !(Calls $color Setting get_FollowCityMarkingTheme)){throw 'All-role theme policy is not connected to actual native recoloring'}
 $paint=Method IntersectionPaintSystem Reconcile
 if(!(Calls $paint CurbLineEmissionSystem NativeJunctionColor) -or !(Calls $paint Setting get_FollowCityMarkingTheme)){throw 'Automatic replacement lost city-theme routing'}
 foreach($pair in @(@('IntersectionPaintSystem','UpdateMeasured'),@('CurbLineEmissionSystem','UpdateMarkings'))) {
  $method=Method $pair[0] $pair[1]
  if(!(Calls $method 'ManagedWorkQueue*' Take)){throw "Fair queue is not wired into $($pair[0])"}
 }
 if(!(Calls (Method CurbLineEmissionSystem UpdateMeasured) CurbLineEmissionSystem UpdateMarkings)){throw 'Road update no longer reaches fair queue worker'}
 $parking=Method CurbLineEmissionSystem AddParkingMarks
 if($parking.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq '_linePrefab'}){throw 'Parking still inherits global edge paint'}
 if(!(Calls $parking YellowPaintDefinitionSystem Explicit)){throw 'Parking lacks dedicated white single paint'}
 $revealed=($a.MainModule.Types|Where-Object Name -eq IntersectionPaintSystem).Fields|Where-Object Name -eq _revealedOriginals
 if(!$revealed -or !($m.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq '_revealedOriginals'})){throw 'Immediate source visibility repair missing'}
 'PASS: compiled integration: bounded 64-source maintenance, no full replacement dictionary enumeration, affected-owner cleanup, lifecycle registration/retirement, renderer material catalog and async retry completion.'
} finally {$a.Dispose()}

