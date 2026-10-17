<#
.SYNOPSIS
  Draws installer\app.ico, the icon used for the Add/Remove Programs entry.

.DESCRIPTION
  The repository carries the icon as a binary, which nobody can review. This script is the
  source of that binary: run it and the same file comes out. Replace the drawing code (or just
  overwrite app.ico with your own artwork) if the project gets a real logo.

  Produces one .ico holding PNG-compressed 16, 32, 48, 64, 128 and 256 pixel images - the set
  Windows asks for in the shell, Add/Remove Programs and the MSI's ARPPRODUCTICON.

.EXAMPLE
  .\installer\make-icon.ps1
.EXAMPLE
  .\installer\make-icon.ps1 -OutputPath $env:TEMP\test.ico -NoWrite
#>
[CmdletBinding()]
param(
    [string]$OutputPath,

    # Draw the images and report them, but write nothing.
    [switch]$NoWrite
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# $PSScriptRoot is not populated while parameter defaults are being evaluated.
if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot 'app.ico' }

$sizes = @(16, 32, 48, 64, 128, 256)
$gold = [System.Drawing.Color]::FromArgb(255, 201, 162, 39)
$goldDeep = [System.Drawing.Color]::FromArgb(255, 160, 118, 20)
$ink = [System.Drawing.Color]::FromArgb(255, 26, 26, 26)

function New-RoundedPath([System.Drawing.RectangleF]$rect, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $inset = [single]([Math]::Max(0.5, $size * 0.03))
        $rect = New-Object System.Drawing.RectangleF(
            $inset, $inset, [single]($size - 2 * $inset), [single]($size - 2 * $inset))
        $radius = [single]([Math]::Max(1.5, $size * 0.20))
        $plate = New-RoundedPath $rect $radius

        $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $rect, $gold, $goldDeep, [single]45)
        $graphics.FillPath($gradient, $plate)
        $gradient.Dispose()

        # A dark "M" - the add-on is the bridge between an MCP client and CorelDRAW.
        $fontSize = [single]($size * 0.58)
        $font = New-Object System.Drawing.Font(
            'Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold,
            [System.Drawing.GraphicsUnit]::Pixel)
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $box = New-Object System.Drawing.RectangleF(0, [single]($size * 0.02), $size, $size)
        $inkBrush = New-Object System.Drawing.SolidBrush($ink)
        $graphics.DrawString('M', $font, $inkBrush, $box, $format)

        $font.Dispose(); $format.Dispose(); $inkBrush.Dispose(); $plate.Dispose()

        $stream = New-Object System.IO.MemoryStream
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$images = foreach ($size in $sizes) {
    [pscustomobject]@{ Size = $size; Bytes = (New-IconPng $size) }
}

if ($NoWrite) {
    foreach ($image in $images) {
        Write-Host ("  {0,3}x{0,-3} {1,5} bytes" -f $image.Size, $image.Bytes.Length)
    }
    Write-Host "Dry run - nothing written."
    return
}

# ICO container: a 6-byte header, a 16-byte directory entry per image, then the image data.
# Width and height are one byte each, 0 meaning 256.
$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)
try {
    $writer.Write([uint16]0)            # reserved
    $writer.Write([uint16]1)            # type: icon
    $writer.Write([uint16]$images.Count)

    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension)     # width
        $writer.Write([byte]$dimension)     # height
        $writer.Write([byte]0)              # palette colours
        $writer.Write([byte]0)              # reserved
        $writer.Write([uint16]1)            # colour planes
        $writer.Write([uint16]32)           # bits per pixel
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }

    foreach ($image in $images) { $writer.Write($image.Bytes) }
    $writer.Flush()

    $directory = Split-Path -Parent $OutputPath
    if ($directory -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
    [System.IO.File]::WriteAllBytes($OutputPath, $stream.ToArray())
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

$file = Get-Item $OutputPath
Write-Host ("Wrote {0} ({1:N0} bytes, {2} images: {3})" -f
    $file.FullName, $file.Length, $images.Count, (($sizes | ForEach-Object { "$_" }) -join ', ')) -ForegroundColor Green
