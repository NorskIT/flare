param([Parameter(Mandatory=$true)][string]$ZipPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Drawing
$zip=[IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ZipPath).Path)
try {
    $required=@('manifest.json','README.md','CHANGELOG.md','LICENSE','icon.png','BepInEx/plugins/NorskIT-Flare/Flare.dll',
        'images/flare-banner.png','images/flare-in-game.png','images/signalbow-and-arrow.png','images/flare-showcase.gif')
    $names=@($zip.Entries.FullName)
    if ($names.Count -ne $required.Count) { throw 'Unexpected package contents' }
    foreach($name in $required) { if ($name -cnotin $names) {throw "Missing entry: $name"} }
    $reader=[IO.StreamReader]::new($zip.GetEntry('manifest.json').Open())
    try {$manifest=$reader.ReadToEnd() | ConvertFrom-Json} finally {$reader.Dispose()}
    if ($manifest.name -cne 'Flare' -or $manifest.description.Length -gt 256 -or $manifest.version_number -notmatch '^\d+\.\d+\.\d+$') {throw 'Invalid metadata'}
    if (@($manifest.dependencies).Count -ne 1 -or $manifest.dependencies[0] -cne 'ValheimModding-Jotunn-2.30.0') {throw 'Unexpected dependencies'}
    $stream=$zip.GetEntry('icon.png').Open()
    try {
        $icon=[Drawing.Image]::FromStream($stream)
        try {if ($icon.Width -ne 256 -or $icon.Height -ne 256) {throw 'Invalid icon size'}} finally {$icon.Dispose()}
    } finally {$stream.Dispose()}
    $memory=[IO.MemoryStream]::new()
    $stream=$zip.GetEntry('BepInEx/plugins/NorskIT-Flare/Flare.dll').Open()
    try { $stream.CopyTo($memory); $assembly=[Reflection.Assembly]::Load($memory.ToArray()) }
    finally { $stream.Dispose(); $memory.Dispose() }
    if ($assembly.GetName().Version.ToString(3) -cne $manifest.version_number) {throw 'DLL/manifest version mismatch'}
    $reader=[IO.StreamReader]::new($zip.GetEntry('README.md').Open())
    try { $readme=$reader.ReadToEnd() } finally { $reader.Dispose() }
    foreach ($match in [regex]::Matches($readme,'!\[[^\]]*\]\((?:https://raw\.githubusercontent\.com/NorskIT/flare/main/)?(images/[^)]+)\)')) {
        if ($match.Groups[1].Value -cnotin $names) { throw 'README image missing from package' }
    }
    "Validated Flare $($manifest.version_number): $($names.Count) files, README images, Jotunn dependency, 256x256 icon."
} finally {$zip.Dispose()}
