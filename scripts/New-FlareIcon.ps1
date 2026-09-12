param([Parameter(Mandatory=$true)][string]$OutputPath)
# Package-size export; preserve the supplied original in Assets.
Add-Type -AssemblyName System.Drawing
$sourcePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Flare\Assets\mod_icon.png'
$source = [Drawing.Image]::FromFile($sourcePath)
$bitmap = [Drawing.Bitmap]::new(256,256)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($source,[Drawing.Rectangle]::new(0,0,256,256))
    $bitmap.Save($OutputPath,[Drawing.Imaging.ImageFormat]::Png)
} finally { $graphics.Dispose(); $bitmap.Dispose(); $source.Dispose() }
