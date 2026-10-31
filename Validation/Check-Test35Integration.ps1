param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop'
Add-Type -Path 'C:\Users\Trevor\AppData\Local\Programs\ILSpy\Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll);$n=0
function Check($ok,$message){if(-not $ok){throw $message};$script:n++}
function Calls($method){@($method.Body.Instructions|ForEach-Object Operand|Where-Object {$_ -is [Mono.Cecil.MethodReference]}|ForEach-Object FullName)}
try{
 $types=$a.MainModule.Types
 $palette=$types|Where-Object Name -eq 'YellowPaintDefinitionSystem'
 $strings=@(($palette.Methods|Where-Object Name -eq 'OnUpdate').Body.Instructions|Where-Object {$_.OpCode.Code -eq 'Ldstr'}|ForEach-Object Operand)
 foreach($mesh in @('Yellow Double Solid Line Mesh','White Double Solid Line Mesh','White Solid Line Mesh')){Check ($strings -contains $mesh) "Missing native palette mesh $mesh"}
 $explicit=$palette.Methods|Where-Object Name -eq 'Explicit'
 $fields=@($explicit.Body.Instructions|ForEach-Object Operand|Where-Object {$_ -is [Mono.Cecil.FieldReference]}|ForEach-Object Name)
 foreach($field in @('_doubleYellow','_doubleWhite','_solid','_white')){Check ($fields -contains $field) "Colour/shape palette missing $field"}
 foreach($pair in @(@('UpgradeAssetsSystem','OnUpdate'),@('CrosswalkUpgradeSystem','EnsureToolbar'))){$type=$types|Where-Object Name -eq $pair[0];$calls=Calls ($type.Methods|Where-Object Name -eq $pair[1]);Check (@($calls|Where-Object {$_ -like '*::AddElement*'}).Count -eq 0) "Native-menu insertion remains in $($pair[0])";Check (@($calls|Where-Object {$_ -like '*DynamicBuffer*::RemoveAt*'}).Count -gt 0) "Stale menu entry cleanup missing in $($pair[0])"}
 $emission=$types|Where-Object Name -eq 'CurbLineEmissionSystem'
 Check (@((Calls ($emission.Methods|Where-Object Name -eq 'AddCenterUpgrade'))|Where-Object {$_ -like '*YellowPaintDefinitionSystem::Explicit*'}).Count -ge 1) 'Center upgrade does not resolve paired palette'
 $inspector=$types|Where-Object Name -eq 'RoadFeatureInspector';$calls=Calls ($inspector.Methods|Where-Object Name -eq 'Describe')
 Check (@($calls|Where-Object {$_ -match 'EntityManager::(Add|Remove|Set|Create|Destroy)'}).Count -eq 0) 'Inspector mutates entities'
 Check (@($calls|Where-Object {$_ -like '*::RequestRefresh*' -or $_ -like '*::QueueRoad*'}).Count -eq 0) 'Inspector queues rebuilds'
 $lights=$types|Where-Object Name -eq 'StreetLightingSystem'
 foreach($name in @('NodeMode','EffectiveMode','IsOwnedNode')){Check (@($lights.Methods|Where-Object Name -eq $name).Count -eq 1) "Missing node-light reconciliation $name"}
 Check (@((Calls ($lights.Methods|Where-Object Name -eq 'NodeMode'))|Where-Object {$_ -like '*StreetLightingPolicy::SharedMode*'}).Count -eq 1) 'Node lamp policy not connected'
 $setting=$types|Where-Object Name -eq 'Setting'
 Check (@((Calls ($setting.Methods|Where-Object Name -eq 'set_MarkingToolColor'))|Where-Object {$_ -like '*EntityManager::*'}).Count -eq 0) 'Theme setting changes saved entity intent'
 $post=$types|Where-Object Name -eq 'CurbsidePanelSystem';$calls=Calls ($post.Methods|Where-Object Name -eq 'OnUpdate')
 Check (@($calls|Where-Object {$_ -like '*RoadFeatureInspector::Describe*'}).Count -eq 1) 'Inspector binding missing'
 Write-Output "$n compiled palette/menu/node-light/inspector contracts passed; native city testing remains required."
}finally{$a.Dispose()}
