param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
if(!$env:CURBSIDE_CECIL){throw 'Set CURBSIDE_CECIL to your installed Mono.Cecil.dll (for example from ILSpy).'}
Add-Type -Path $env:CURBSIDE_CECIL
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Dll))
try{
 $types=$assembly.MainModule.Types
 $clone=($types|Where-Object Name -eq YellowPaintDefinitionSystem).Methods|Where-Object Name -eq Clone
 $remove=@($clone.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -eq 'Remove' -and $_.Operand.GenericArguments[0].Name -eq 'ObsoleteIdentifiers'})
 $add=$clone.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'AddPrefab'}|Select-Object -Last 1
 if($remove.Count -ne 1 -or $remove[0].Offset -ge $add.Offset){throw 'Private clone identifiers not stripped before registration'}
 $recover=($types|Where-Object Name -eq SaveRecoverySystem).Methods|Where-Object Name -eq OnUpdate
 if($recover.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -match 'Stopwatch|UnityEngine.Time|DateTime'}){throw 'Save recovery must not use a timeout'}
 $observe=($types|Where-Object Name -eq SaveRecoverySystem).Methods|Where-Object Name -eq Observe
 if(!($observe.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Complete' -and $_.Operand.DeclaringType.FullName -like '*TaskQueue*'})){throw 'No public native queue completion observation'}
 $batch=($types|Where-Object Name -eq GeneratedVisualLifecycle).Methods|Where-Object Name -eq RetireSaveBatch
 foreach($api in @('GetStaticSearchTree','GetLaneSearchTree','AddComponent')){if(!($batch.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $api})){throw "Missing batch retirement API: $api"}}
 $scan=($types|Where-Object Name -eq PackSystem).Methods|Where-Object Name -eq ScanNativeCatalog
 if(!($scan.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'ToEntityArray'})){throw 'Published discovery must use native prefab entities, not a zero-hash name lookup'}
 $ready=($types|Where-Object Name -eq GardenCityLightPolicy).Methods|Where-Object Name -eq Ready
 if(!($ready.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'NativeSignalCatalog' -and $_.Operand.Name -eq 'TryGet'})){throw 'Runtime module resolution lost native published identity'}
 $unsafe=@($types.Methods|Where-Object {$_.IsPInvokeImpl});if($unsafe.Count){throw 'Unexpected authored P/Invoke'}
 $wear=($types|Where-Object Name -eq FileTextureLoader).Methods|Where-Object Name -eq LoadBundledRoadWear
 $grass=$types|Where-Object Name -eq MedianMaterialSystem
 $snapshot=$grass.NestedTypes|Where-Object Name -eq MaterialSnapshot
 $capture=$snapshot.Methods|Where-Object Name -eq CaptureBaseline
 if(@($capture.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Baseline'}).Count -ne 4){throw 'Grass texture/color takeover baselines are incomplete'}
 $applyGrass=$grass.Methods|Where-Object Name -eq ApplySelectedTexturesToRoadGrass
 if(!($applyGrass.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'CaptureBaseline'})){throw 'Grass application bypasses ownership baseline capture'}
 $applyGrassUv=$grass.Methods|Where-Object Name -eq ApplyUvScaleLive
 if(!($applyGrassUv.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'CaptureUvBaseline'})){throw 'Grass UV application bypasses ownership baseline capture'}
 if(!($wear.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'BundledAssets' -and $_.Operand.Name -eq 'Open'})){throw 'Bundled road-wear loader bypasses the sidecar asset resolver'}
 if($wear.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'GetManifestResourceStream'}){throw 'Road-wear texture must not depend on removed embedded PNG resources'}
 $projectRoot=Split-Path $PSScriptRoot
 [xml]$project=Get-Content -LiteralPath (Join-Path $projectRoot 'MedianGrassReplacement.csproj') -Raw
 $version=$project.Project.PropertyGroup.Version|Where-Object {$_}|Select-Object -First 1
 $null=$version -match '^(\d+)\.(\d+)\.(\d+)-test\.(\d+)$'
 $expected="$($Matches[1]).$($Matches[2]).$($Matches[3]).$($Matches[4])"
 $metadata=Get-Content -LiteralPath (Join-Path $projectRoot 'UI/mod.json') -Raw|ConvertFrom-Json
 if($metadata.version -ne $expected){throw 'UI/project version mismatch'}
 $source=Get-Content -LiteralPath (Join-Path $projectRoot 'Systems/SignalAssetLoader.cs') -Raw
 if(!$source.Contains('CURBSIDE_DEVELOPER_SIGNAL_IMPORT') -or !$source.Contains('Published(model)')){throw 'Missing explicit import gate or published asset ownership check'}
 'PASS: owned clone IDs, public save-queue recovery without timers, batch retirement APIs, no authored P/Invoke, UI version and developer import gate.'
}finally{$assembly.Dispose()}
