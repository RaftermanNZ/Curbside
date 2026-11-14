param(
    [Parameter(Mandatory=$true)][string]$ProjectRoot,
    [string]$AssemblyPath = ''
)

$ErrorActionPreference = 'Stop'
function Get-IconFileHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-','') }
    finally { $hash.Dispose(); $stream.Dispose() }
}
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$iconFolder = Join-Path $ProjectRoot 'Icons'
$manifestPath = Join-Path $iconFolder 'required-icons.txt'
if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw 'Required icon manifest is missing; refusing an incomplete package.'
}

$required = @(Get-Content -LiteralPath $manifestPath | ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and !$_.StartsWith('#') })
if ($required.Count -eq 0) { throw 'Required icon manifest is empty.' }
$unique = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($name in $required) {
    if ($name -cnotmatch '^[A-Za-z0-9_-]+\.svg$' -or !$unique.Add($name)) {
        throw "Invalid or duplicate icon manifest entry: $name"
    }
    $file = Join-Path $iconFolder $name
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Required icon missing: $name" }
    $item = Get-Item -LiteralPath $file
    if ($item.Name -cne $name) { throw "Icon filename case does not match: $name" }
    if ($item.Length -eq 0) { throw "Required icon is empty: $name" }
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($file, $settings)
    try {
        $svg = [Xml.XmlDocument]::new()
        $svg.XmlResolver = $null
        $svg.Load($reader)
        if ($svg.DocumentElement.LocalName -cne 'svg' -or
            $svg.DocumentElement.NamespaceURI -cne 'http://www.w3.org/2000/svg') {
            throw "Required icon is not an SVG document: $name"
        }
    } finally { $reader.Dispose() }
}

$actual = @(Get-ChildItem -LiteralPath $iconFolder -Filter '*.svg' -File)
foreach ($file in $actual) {
    if ($file.Name -cnotin $required) { throw "Icon is not declared in required-icons.txt: $($file.Name)" }
}

# Check production references too, so adding a button cannot silently omit its artwork.
# Skip generated files and test fixtures; no dependency on developer-installed icon caches.
$excluded = @('bin','obj','BuildOutput','node_modules','.git','Validation','Icons','Packaging')
$folders = [Collections.Generic.Stack[string]]::new()
$folders.Push($ProjectRoot)
$references = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$pattern = '["''](?:coui://mgr-icons/)?([A-Za-z0-9_-]+\.svg)["'']'
while ($folders.Count -gt 0) {
    $folder = $folders.Pop()
    foreach ($file in Get-ChildItem -LiteralPath $folder -File) {
        if ($file.Extension -notin @('.cs','.tsx','.ts','.jsx')) { continue }
        foreach ($match in [regex]::Matches([IO.File]::ReadAllText($file.FullName), $pattern)) {
            [void]$references.Add($match.Groups[1].Value)
        }
    }
    foreach ($child in Get-ChildItem -LiteralPath $folder -Directory) {
        if ($child.Name -notin $excluded) { $folders.Push($child.FullName) }
    }
}
foreach ($name in $references) {
    if ($name -cnotin $required) { throw "Referenced UI icon is not in required-icons.txt: $name" }
}

if ($AssemblyPath) {
    if (![IO.Path]::IsPathRooted($AssemblyPath)) { $AssemblyPath = Join-Path $ProjectRoot $AssemblyPath }
    if (!(Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) { throw "Assembly to validate is missing: $AssemblyPath" }
    # Metadata-only loading: no mod startup, game world or native library initialization.
    $assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom([IO.Path]::GetFullPath($AssemblyPath))
    $prefix = 'MedianGrassReplacement.'
    $resources = @($assembly.GetManifestResourceNames() | Where-Object { $_.EndsWith('.svg', [StringComparison]::OrdinalIgnoreCase) })
    if ($resources.Count -ne $required.Count) {
        throw "Embedded icon count mismatch: expected $($required.Count), found $($resources.Count)."
    }
    foreach ($name in $required) {
        $resource = $prefix + $name
        if ($resource -cnotin $resources) { throw "Required embedded icon missing or misnamed: $resource" }
        $stream = $assembly.GetManifestResourceStream($resource)
        if ($null -eq $stream) { throw "Required embedded icon cannot be read: $resource" }
        try {
            $hash = [Security.Cryptography.SHA256]::Create()
            try { $embeddedHash = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-','') }
            finally { $hash.Dispose() }
            $sourceHash = Get-IconFileHash (Join-Path $iconFolder $name)
            if ($embeddedHash -cne $sourceHash) { throw "Embedded icon does not match source artwork: $name" }
        } finally { $stream.Dispose() }
    }
}

Write-Output "Curbside icons verified: $($required.Count) SVGs, $($references.Count) production references; assembly checked=$([bool]$AssemblyPath)."
