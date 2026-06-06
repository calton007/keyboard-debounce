$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null

$pngPath = Join-Path $assets 'keyboard-debounce-256.png'
$icoPath = Join-Path $assets 'keyboard-debounce.ico'
$sizes = @(16, 24, 32, 48, 64, 128, 256)

function New-RoundedPath {
    param(
        [System.Drawing.RectangleF]$Bounds,
        [float]$Radius
    )

    $diameter = $Radius * 2.0
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($Bounds.X, $Bounds.Y, $diameter, $diameter, 180, 90)
    $path.AddArc(($Bounds.Right - $diameter), $Bounds.Y, $diameter, $diameter, 270, 90)
    $path.AddArc(($Bounds.Right - $diameter), ($Bounds.Bottom - $diameter), $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.X, ($Bounds.Bottom - $diameter), $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $scale = $Size / 256.0
    $blue = [System.Drawing.Color]::FromArgb(255, 24, 103, 192)
    $blueDark = [System.Drawing.Color]::FromArgb(255, 12, 62, 140)
    $cyan = [System.Drawing.Color]::FromArgb(255, 60, 210, 235)
    $white = [System.Drawing.Color]::FromArgb(255, 248, 252, 255)
    $ink = [System.Drawing.Color]::FromArgb(255, 31, 45, 62)

    function Scale([float]$x) { return [float]($x * $scale) }

    $shield = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shield.AddLine((Scale 128), (Scale 20), (Scale 214), (Scale 56))
    $shield.AddBezier((Scale 214), (Scale 56), (Scale 210), (Scale 147), (Scale 185), (Scale 190), (Scale 128), (Scale 232))
    $shield.AddBezier((Scale 128), (Scale 232), (Scale 70), (Scale 190), (Scale 46), (Scale 147), (Scale 42), (Scale 56))
    $shield.CloseFigure()

    $shieldBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF((Scale 38), (Scale 18), (Scale 180), (Scale 218))),
        $blue,
        $blueDark,
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillPath($shieldBrush, $shield)
    $g.DrawPath((New-Object System.Drawing.Pen($white, (Scale 8))), $shield)

    $keyboardRect = New-Object System.Drawing.RectangleF((Scale 64), (Scale 92), (Scale 128), (Scale 76))
    $keyboardPath = New-RoundedPath -Bounds $keyboardRect -Radius (Scale 15)
    $g.FillPath((New-Object System.Drawing.SolidBrush($white)), $keyboardPath)
    $g.DrawPath((New-Object System.Drawing.Pen($ink, (Scale 7))), $keyboardPath)
    $keyboardPath.Dispose()

    $keyBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 220, 235, 248))
    $keyPen = New-Object System.Drawing.Pen($ink, (Scale 3))
    foreach ($row in 0..1) {
        foreach ($col in 0..3) {
            $x = Scale(78 + ($col * 27))
            $y = Scale(108 + ($row * 25))
            $rect = New-Object System.Drawing.RectangleF($x, $y, (Scale 18), (Scale 14))
            $keyPath = New-RoundedPath -Bounds $rect -Radius (Scale 4)
            $g.FillPath($keyBrush, $keyPath)
            $g.DrawPath($keyPen, $keyPath)
            $keyPath.Dispose()
        }
    }
    $space = New-Object System.Drawing.RectangleF((Scale 94), (Scale 151), (Scale 68), (Scale 9))
    $spacePath = New-RoundedPath -Bounds $space -Radius (Scale 4)
    $g.FillPath($keyBrush, $spacePath)
    $spacePath.Dispose()

    $wavePen = New-Object System.Drawing.Pen($cyan, (Scale 12))
    $wavePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $wavePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($wavePen, (Scale 36), (Scale 70), (Scale 52), (Scale 92), 295, 130)
    $g.DrawArc($wavePen, (Scale 168), (Scale 70), (Scale 52), (Scale 92), 65, 130)

    $checkPen = New-Object System.Drawing.Pen($cyan, (Scale 16))
    $checkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $checkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLines($checkPen, @(
        (New-Object System.Drawing.PointF((Scale 92), (Scale 182))),
        (New-Object System.Drawing.PointF((Scale 119), (Scale 205))),
        (New-Object System.Drawing.PointF((Scale 168), (Scale 166)))
    ))

    $g.Dispose()
    return $bmp
}

$pngImages = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap -Size $size
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngImages += [PSCustomObject]@{ Size = $size; Bytes = $stream.ToArray() }
    if ($size -eq 256) {
        $bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $stream.Dispose()
    $bmp.Dispose()
}

$fs = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter($fs)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$pngImages.Count)
$offset = 6 + (16 * $pngImages.Count)
foreach ($image in $pngImages) {
    $dim = if ($image.Size -eq 256) { 0 } else { $image.Size }
    $writer.Write([byte]$dim)
    $writer.Write([byte]$dim)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$image.Bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $image.Bytes.Length
}
foreach ($image in $pngImages) {
    $writer.Write($image.Bytes)
}
$writer.Dispose()
$fs.Dispose()

Write-Host "Icon written: $icoPath"
Write-Host "Preview written: $pngPath"
