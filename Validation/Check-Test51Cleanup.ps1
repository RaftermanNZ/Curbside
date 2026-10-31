param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference='Stop';Add-Type -Path 'C:/Users/Trevor/AppData/Local/Programs/ILSpy/Mono.Cecil.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll);$n=0
function Check($ok,$why){if(-not $ok){throw $why};$script:n++}
try{
 $surface=$a.MainModule.Types|Where-Object Name -eq SurfaceTextureSystem
 Check (@($surface.Methods|Where-Object Name -in @('Piece','CompositionVariant','RestorePieceReferences')).Count -eq 0) 'Quarantined surface generator remains'
 $global=$a.MainModule.Types|Where-Object Name -eq GlobalSurfaceMaterialSystem
 $restore=$global.Methods|Where-Object Name -eq Restore
 Check (@($restore.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'CanRestore'}).Count -eq 1) 'Global restore must respect later third-party material changes'
 $setting=$a.MainModule.Types|Where-Object Name -eq Setting
 $folder=$setting.Methods|Where-Object Name -eq set_TextureFolder
 Check (@($folder.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Invalidate'}).Count -eq 2) 'Shared folder must invalidate both library views'
 Write-Output "$n read-only cleanup, owned-reference write and folder-browsing contracts passed. Native profiling remains required."
}finally{$a.Dispose()}
