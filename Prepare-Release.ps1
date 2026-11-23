param(
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
$dependencies = Join-Path $PSScriptRoot 'Dependencies'
$symbols = @('MedianGrassReplacement.dll', 'MedianGrassReplacement_win_x86_64.dll')
$builtFiles = $symbols + @('curbside.mjs', 'curbside.css', 'curbside.mjs.LICENSE.txt')
$dependencyFiles = @(Get-ChildItem -LiteralPath $dependencies -Filter '*.dll' -File)
foreach ($file in $dependencyFiles) { $builtFiles += $file.Name }
foreach ($name in $builtFiles) {
    $path = Join-Path $BuildFolder $name
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Release input is missing or empty: $name"
    }
}
foreach ($file in $dependencyFiles) {
    $built = Join-Path $BuildFolder $file.Name
    if ((Get-FileHash -LiteralPath $built).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
        throw "Release dependency does not match the source package: $($file.Name)"
    }
}
$textures = @(Get-ChildItem -LiteralPath (Join-Path $BuildFolder 'BundledTextures') -Filter '*.png' -File)
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'BundledTextures') -Filter '*.png' -File) {
    if ($file.Name -notin $textures.Name) { throw "Release texture is missing: $($file.Name)" }
}
$resolvedBuild = (Resolve-Path -LiteralPath $BuildFolder).Path
$resolvedRelease = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ReleaseFolder)
if ($resolvedBuild.TrimEnd('\') -eq $resolvedRelease.TrimEnd('\')) {
    throw 'Use a separate release output folder.'
}
New-Item -ItemType Directory -Path $ReleaseFolder -Force | Out-Null
foreach ($name in $builtFiles) {
    Copy-Item -LiteralPath (Join-Path $BuildFolder $name) -Destination $ReleaseFolder -Force
}
foreach ($name in $symbols) { Set-PortableSymbolPaths (Join-Path $ReleaseFolder $name) }
foreach ($name in @('README.md', 'CHANGELOG.md', 'LICENSE', 'THIRD-PARTY-NOTICES.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $ReleaseFolder -Force
}
$noticeFolder = Join-Path $ReleaseFolder 'ThirdParty'
New-Item -ItemType Directory -Path $noticeFolder -Force | Out-Null
Get-ChildItem -LiteralPath $dependencies -File |
    Where-Object Extension -in '.txt', '.nuspec' | Copy-Item -Destination $noticeFolder -Force
$textureFolder = Join-Path $ReleaseFolder 'BundledTextures'
New-Item -ItemType Directory -Path $textureFolder -Force | Out-Null
$textures | Copy-Item -Destination $textureFolder -Force
Write-Output "Release folder prepared with $($dependencyFiles.Count) managed dependencies."
