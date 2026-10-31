param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:\Users\Trevor\AppData\Local\Programs\ILSpy\Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll);$n=0
function Check($ok,$message){if(-not $ok){throw $message};$script:n++}
function ModType($name){$a.MainModule.Types|Where-Object Name -eq $name}
function Method($type,$name){(ModType $type).Methods|Where-Object Name -eq $name}
function Calls($method){@($method.Body.Instructions|ForEach-Object Operand|Where-Object {$_ -is [Mono.Cecil.MethodReference]}|ForEach-Object FullName)}
try{
 $calls=Calls (Method 'Mod' 'OnLoad')
 Check (@($calls|Where-Object {$_ -like '*UpdateBefore<*YellowPaintDefinitionSystem,*CurbLineEmissionSystem*'}).Count -eq 1) 'Palette not scheduled before live road emission'
 Check (@($calls|Where-Object {$_ -like '*UpdateAt<*YellowPaintDefinitionSystem*'}).Count -eq 0) 'Palette still tied to demand-driven prefab phase'
 $fields=@((Method 'YellowPaintDefinitionSystem' 'OnGamePreload').Body.Instructions|Where-Object {$_.OpCode.Code -eq 'Stfld'}|ForEach-Object {$_.Operand.Name})
 Check (@($fields|Where-Object {$_ -in @('_solid','_doubleYellow','_white','_doubleWhite')}).Count -eq 0) 'Preload discards registered paint definitions'
 $calls=Calls (Method 'YellowPaintDefinitionSystem' 'Explicit');Check (@($calls|Where-Object {$_ -like '*::ReadyEntity*'}).Count -eq 1) 'No exact native paint fallback'
 $calls=Calls (Method 'CurbLineEmissionSystem' 'AddNativeLineCaps');Check (@($calls|Where-Object {$_ -like '*SegmentUpgradePolicy::HidesNative*'}).Count -eq 1) 'Native-only hiding leaves orphan native-derived node caps'
 $calls=Calls (Method 'CurbLineEmissionSystem' 'AddJunctionEdges')
 Check (@($calls|Where-Object {$_ -like '*MarkCurve::get_Median*'}).Count -eq 0) 'Unexpected mutable mark access'
 $rebuild=Method 'CurbLineEmissionSystem' 'RebuildJoins';$instructions=$rebuild.Body.Instructions
 $index=0;for($i=0;$i -lt $instructions.Count;$i++){if($instructions[$i].Operand -is [Mono.Cecil.MethodReference] -and $instructions[$i].Operand.Name -eq 'AddJunctionEdges'){$index=$i;break}}
 Check ($index -gt 0) 'Explicit junction emission missing'
 $recent=@($instructions[($index-14)..$index]|ForEach-Object Operand|Where-Object {$_ -is [Mono.Cecil.MethodReference]}|ForEach-Object FullName)
 Check (@($recent|Where-Object {$_ -like '*HasComponent<*MGRJunctionEdgeLines*'}).Count -gt 0) 'Explicit junction marker not gating additive edges'
 Check (@($recent|Where-Object {$_ -like '*HasComponent<*MGRHideNodeLines*'}).Count -eq 0) 'Hide native junction paint still suppresses additive edges'
 $calls=Calls (Method 'StreetLightingSystem' 'NodeMode');Check (@($calls|Where-Object {$_ -like '*::Mode(*' -or $_ -like '*::SharedMode*'}).Count -eq 0) 'Node lamps still derive segment state'
 $calls=Calls (Method 'DecalUpgradeTool' 'OnUpdate');Check (@($calls|Where-Object {$_ -like '*StreetLightingSystem::ApplyNode*'}).Count -eq 1) 'Node hover does not have independent lamp action'
 $apply=Method 'StreetLightingSystem' 'Apply';$instructions=$apply.Body.Instructions
 for($i=0;$i -lt $instructions.Count;$i++){if($instructions[$i].Operand -is [Mono.Cecil.MethodReference] -and $instructions[$i].Operand.Name -eq 'NativeFlag'){Check ($instructions[$i-1].OpCode.Code -eq 'Ldc_I4_1') 'Segment removal disables native junction lighting'}}
 foreach($name in @('MGRLaneDivider','MGRNodeStreetLighting','MGRNodeStreetLightingPrevious','MGRStreetLightingOwnedFlag')){Check (@((ModType $name).Interfaces|Where-Object {$_.InterfaceType.Name -in @('ISerializable','IEmptySerializable')}).Count -eq 1) "Missing saved intent $name"}
 $fields=@((ModType 'RoadAttachmentTransferSystem').NestedTypes|Where-Object Name -eq 'Selection'|ForEach-Object Fields|ForEach-Object Name)
 foreach($field in @('DividerStyle','LightingOwned')){Check ($fields -contains $field) "Road replacement loses $field"}
 $calls=Calls (Method 'NodeSplitTool' 'ValidSplitPreview');Check (@($calls|Where-Object {$_ -like '*ToolBaseSystem::GetAllowApply*'}).Count -eq 1) 'Split tool bypasses native validation'
 foreach($name in @('Define','OnUpdate','ClearDefinitions')){
  $calls=Calls (Method 'NodeSplitTool' $name)
  Check (@($calls|Where-Object {$_ -match 'EntityManager::(SetComponentData|DestroyEntity|CreateEntity)'}).Count -eq 0) "Split tool directly mutates topology in $name"
  Check (@($calls|Where-Object {$_ -like '*AddComponent<*Game.Net.Node*' -or $_ -like '*AddComponent<*Game.Net.Edge*'}).Count -eq 0) "Split tool fabricates native graph entities in $name"
 }
 $calls=Calls (Method 'NodeSplitTool' 'Define');Check (@($calls|Where-Object {$_ -like '*EntityCommandBuffer::AddComponent<*NetCourse*'}).Count -eq 1) 'Split does not use native course definitions'
 $calls=Calls (Method 'SegmentUpgradeSystem' 'Apply');Check (@($calls|Where-Object {$_ -like '*Remove<*MGRLaneDivider*'}).Count -gt 0) 'Restore defaults misses interior lane choice'
 Write-Output "$n compiled live-palette, additive-edge, independent-lamp, transfer and native-split contracts passed."
}finally{$a.Dispose()}
