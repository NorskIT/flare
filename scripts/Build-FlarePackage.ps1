param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
& dotnet build (Join-Path $repo 'src\Flare\Flare.csproj') -c Release -p:ExecutePrebuild=false | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Flare build failed' }
$stage = Join-Path $repo ('artifacts\flare\stage-' + [Guid]::NewGuid().ToString('N'))
$pluginFolder = Join-Path $stage 'BepInEx\plugins\NorskIT-Flare'
New-Item -ItemType Directory -Path $pluginFolder -Force | Out-Null
$dll = Join-Path $repo 'src\Flare\bin\Release\net481\Flare.dll'
Copy-Item -LiteralPath $dll -Destination $pluginFolder
$version = [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3)
$manifest = @{name='Flare';version_number=$version;description='A disposable Signalbow and Arrow that lights up the landscape. Adjustable server lighting, local shadows, and visibility beyond loaded areas.';website_url='https://github.com/NorskIT/flare';dependencies=@('ValheimModding-Jotunn-2.30.0')}
[IO.File]::WriteAllText((Join-Path $stage 'manifest.json'), ($manifest | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
foreach ($file in @('README.md','CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $repo "$file") -Destination (Join-Path $stage $file)
}
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repo 'images') -Destination $stage -Recurse
& "$PSScriptRoot\New-FlareIcon.ps1" -OutputPath (Join-Path $stage 'icon.png')
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$zipPath = Join-Path $repo "artifacts\flare\NorskIT-Flare-$version.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath }
$zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse) {
        $entry = $file.FullName.Substring($stage.Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,$entry) | Out-Null
    }
} finally { $zip.Dispose() }
& "$PSScriptRoot\Validate-FlarePackage.ps1" -ZipPath $zipPath
"PACKAGE: $zipPath"
