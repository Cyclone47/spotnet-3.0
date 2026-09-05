Add-Type -AssemblyName System.Drawing

$sizes   = @(256, 128, 64, 48, 32, 16)
$srcPath = Join-Path $PSScriptRoot 'new_icon.png'
$dstPath = Join-Path $PSScriptRoot 'spotnet.ico'

$src = [System.Drawing.Image]::FromFile($srcPath)

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count

$imageData = @()
foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()

    $imgMs = New-Object System.IO.MemoryStream
    $bmp.Save($imgMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $imageData += ,$imgMs.ToArray()
    $imgMs.Dispose()
}

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz    = $sizes[$i]
    $bytes = $imageData[$i]

    $bw.Write([byte]$(if ($sz -eq 256) { 0 } else { $sz }))
    $bw.Write([byte]$(if ($sz -eq 256) { 0 } else { $sz }))
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $bytes.Length
}

foreach ($bytes in $imageData) {
    $bw.Write($bytes, 0, $bytes.Length)
}

$bw.Flush()
$src.Dispose()

[System.IO.File]::WriteAllBytes($dstPath, $ms.ToArray())

Copy-Item $dstPath 'D:\sourcecode\src\Spotnet\Spotnet\Resources\ImagesInternal\spotnet.ico' -Force
Copy-Item $dstPath 'D:\sourcecode\src\Spotnet\Spotnet\Resources\ImagesInternal\smallspotnet.ico' -Force
if (Test-Path 'C:\Users\Tobias\AppData\Local\Spotnet\') {
    Copy-Item $dstPath 'C:\Users\Tobias\AppData\Local\Spotnet\app.ico' -Force
}

Write-Host 'New icon converted and copied successfully'


