# Turns Rev's app icon (a 1024 x 1024 PNG) into a Windows .ico file.
#
# A .ico file is a small bundle of the same picture at several sizes,
# so Windows can pick a sharp one for the taskbar, the Start menu, the desktop, and so on.
# Each size is stored as a PNG inside the bundle (Windows Vista and later understand this).
#
# Run it from the repository folder:
#   pwsh tools/make-icon.ps1

param(
    [string]$Source = "src/Rev.App/Assets/AppIcon.png",
    [string]$Target = "src/Rev.App/Assets/AppIcon.ico"
)

Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$original = [System.Drawing.Image]::FromFile((Resolve-Path $Source))

# 1. Make a PNG of the icon at each size.
$images = foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.DrawImage($original, 0, 0, $size, $size)
    $graphics.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    ,$stream.ToArray()
}
$original.Dispose()

# 2. Write the .ico file: a short header, one directory entry per size, then the PNGs.
$file = [System.IO.File]::Create((Join-Path (Get-Location) $Target))
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([UInt16]0)              # reserved, always 0
$writer.Write([UInt16]1)              # 1 = icon
$writer.Write([UInt16]$sizes.Count)   # how many pictures

$offset = 6 + 16 * $sizes.Count       # the first PNG starts right after the directory
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $writer.Write([Byte]($size % 256))   # width (0 means 256)
    $writer.Write([Byte]($size % 256))   # height (0 means 256)
    $writer.Write([Byte]0)               # no colour palette
    $writer.Write([Byte]0)               # reserved
    $writer.Write([UInt16]1)             # colour planes
    $writer.Write([UInt16]32)            # bits per pixel
    $writer.Write([UInt32]$images[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($png in $images) { $writer.Write($png) }
$writer.Dispose()

Write-Host "Made $Target with sizes $($sizes -join ', ')"
