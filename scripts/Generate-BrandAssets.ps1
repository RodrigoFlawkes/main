param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$brandRoot = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'assets/branding'))
$originalPath = Join-Path $brandRoot 'original/WaveStudio.png'
$iconDirectory = Join-Path $brandRoot 'icons'
New-Item -ItemType Directory -Path $iconDirectory -Force | Out-Null
$original = [Drawing.Bitmap]::FromFile($originalPath)
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
try {
    foreach ($size in $sizes) {
        $bitmap = New-Object Drawing.Bitmap($size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb))
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scale = [Math]::Min($size / $original.Width, $size / $original.Height)
            $width = [int][Math]::Round($original.Width * $scale)
            $height = [int][Math]::Round($original.Height * $scale)
            $rectangle = New-Object Drawing.Rectangle(([int](($size-$width)/2)), ([int](($size-$height)/2)), $width, $height)
            $graphics.DrawImage($original, $rectangle, 0, 0, $original.Width, $original.Height, [Drawing.GraphicsUnit]::Pixel)
            $pngPath = Join-Path $iconDirectory ("WaveStudio-{0}.png" -f $size)
            $bitmap.Save($pngPath, [Drawing.Imaging.ImageFormat]::Png)
            $frames += ,([IO.File]::ReadAllBytes($pngPath))
        }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
}
finally { $original.Dispose() }
$stream = [IO.File]::Create((Join-Path $iconDirectory 'WaveStudio.ico'))
$writer = New-Object IO.BinaryWriter($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
}
finally { $writer.Dispose() }
Write-Output 'WaveStudio.ico e sete PNGs gerados a partir do original, sem recorte ou alteração de cores.'
