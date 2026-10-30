ï»¿param(
    [Parameter(Mandatory=$true)][string]$BuildFolder,
    [Parameter(Mandatory=$true)][string]$ReleaseFolder
)
$ErrorActionPreference='Stop'
# Replace only CodeView symbol-file paths. Executable sections and metadata stay intact.
function Set-PortableSymbolPaths([string]$Path) {
    [byte[]]$data=[IO.File]::ReadAllBytes($Path)
    $pe=[BitConverter]::ToInt32($data,0x3c)
    if([BitConverter]::ToUInt32($data,$pe) -ne 0x4550){throw 'Invalid PE signature'}
    $sectionCount=[BitConverter]::ToUInt16($data,$pe+6)
    $optionalSize=[BitConverter]::ToUInt16($data,$pe+20)
    $optional=$pe+24
    $magic=[BitConverter]::ToUInt16($data,$optional)
    if($magic -eq 0x20b){$directory=$optional+112}elseif($magic -eq 0x10b){$directory=$optional+96}else{throw 'Unsupported PE optional header'}
    $rva=[BitConverter]::ToUInt32($data,$directory+48)
    $size=[BitConverter]::ToUInt32($data,$directory+52)
    if($rva -eq 0 -or $size -eq 0){return}
    $table=$optional+$optionalSize
    $debugOffset=-1
    for($i=0;$i -lt $sectionCount;$i++){
        $section=$table+40*$i
        $start=[BitConverter]::ToUInt32($data,$section+12)
        $virtualSize=[BitConverter]::ToUInt32($data,$section+8)
        $rawSize=[BitConverter]::ToUInt32($data,$section+16)
        if($rva -ge $start -and $rva -lt ($start+[Math]::Max($virtualSize,$rawSize))){$debugOffset=[BitConverter]::ToUInt32($data,$section+20)+$rva-$start;break}
    }
    if($debugOffset -lt 0){throw 'Debug directory is outside PE sections'}
    for($i=0;$i+28 -le $size;$i+=28){
        $entry=$debugOffset+$i
        if([BitConverter]::ToUInt32($data,$entry+12) -ne 2){continue}
        $length=[BitConverter]::ToUInt32($data,$entry+16)
        $pointer=[BitConverter]::ToUInt32($data,$entry+24)
        if($length -lt 25 -or $pointer+$length -gt $data.Length){throw 'Invalid CodeView bounds'}
        if([Text.Encoding]::ASCII.GetString($data,$pointer,4) -ne 'RSDS'){continue}
        $pathStart=$pointer+24
        $capacity=$length-24
        $old=[Text.Encoding]::UTF8.GetString($data,$pathStart,$capacity).TrimEnd([char]0)
        $name=[IO.Path]::GetFileName($old.Replace('/','\'))
        $encoded=[Text.Encoding]::UTF8.GetBytes($name)
        if($encoded.Length -ge $capacity){throw 'Symbol filename does not fit CodeView record'}
        [Array]::Clear($data,$pathStart,$capacity)
        [Array]::Copy($encoded,0,$data,$pathStart,$encoded.Length)
    }
    [IO.File]::WriteAllBytes($Path,$data)
}
New-Item -ItemType Directory -Path $ReleaseFolder -Force | Out-Null
foreach($name in @('MedianGrassReplacement.dll','MedianGrassReplacement_win_x86_64.dll')){
    $destination=Join-Path $ReleaseFolder $name
    Copy-Item -LiteralPath (Join-Path $BuildFolder $name) -Destination $destination
    Set-PortableSymbolPaths $destination
}
Copy-Item -LiteralPath (Join-Path $BuildFolder 'curbside.mjs') -Destination $ReleaseFolder
foreach($name in @('README.md','CHANGELOG.md','PARADOX-MODS-DESCRIPTION.txt')){
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $ReleaseFolder
}
Write-Output 'Release folder prepared. Symbol files remain in the build folder.'


Copy-Item -LiteralPath (Join-Path $BuildFolder "curbside.css") -Destination $ReleaseFolder
Copy-Item -LiteralPath (Join-Path $BuildFolder "BundledTextures") -Destination $ReleaseFolder -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "THIRD-PARTY-NOTICES.txt") -Destination $ReleaseFolder
