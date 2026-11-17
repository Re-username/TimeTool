# Генерирует app.ico (часы) и appicon.png для TimeTool через System.Drawing.
# Запуск: powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1
param(
    [string]$IcoOut = "src/TimeTool/Resources/app.ico",
    [string]$PngOut = "src/TimeTool/Resources/AppIcon/appicon.png",
    [string]$AndroidPngOut = "src/TimeTool/Platforms/Android/Resources/drawable/appicon.png"
)

$root = Split-Path $PSScriptRoot -Parent
$IcoOut = Join-Path $root $IcoOut
$PngOut = Join-Path $root $PngOut
$AndroidPngOut = Join-Path $root $AndroidPngOut

Add-Type -AssemblyName System.Drawing

function Draw-Icon([int]$S) {
    $k = $S / 256.0
    $c = $S / 2.0
    $bmp = New-Object System.Drawing.Bitmap $S, $S
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    # скруглённый тёмный фон
    $r = [int](56 * $k)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($S - 2 * $r, 0, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($S - 2 * $r, $S - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc(0, $S - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $g.SetClip($path)
    $rect = New-Object System.Drawing.Rectangle 0, 0, $S, $S
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,
        ([System.Drawing.Color]::FromArgb(255, 22, 32, 46)),
        ([System.Drawing.Color]::FromArgb(255, 11, 15, 22)), 45.0
    $g.FillRectangle($bg, $rect)
    $g.ResetClip()

    # циферблат
    $accent = [System.Drawing.Color]::FromArgb(255, 88, 166, 255)
    $facePen = New-Object System.Drawing.Pen $accent, ([Math]::Max(2, 10 * $k))
    $R = 88 * $k
    $g.DrawEllipse($facePen, ($c - $R), ($c - $R), (2 * $R), (2 * $R))

    # метки 12/3/6/9
    $tickPen = New-Object System.Drawing.Pen $accent, ([Math]::Max(1.5, 6 * $k))
    foreach ($deg in 0, 90, 180, 270) {
        $a = ($deg - 90) * [Math]::PI / 180.0
        $r1 = 70 * $k; $r2 = 82 * $k
        $g.DrawLine($tickPen,
            ($c + $r1 * [Math]::Cos($a)), ($c + $r1 * [Math]::Sin($a)),
            ($c + $r2 * [Math]::Cos($a)), ($c + $r2 * [Math]::Sin($a)))
    }

    # стрелки: 10:10
    $handPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 219, 233, 255)), ([Math]::Max(2, 8 * $k))
    $handPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $handPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $hourA = ((10 + 10 / 60.0) / 12.0 * 360 - 90) * [Math]::PI / 180.0
    $minA = (10 / 60.0 * 360 - 90) * [Math]::PI / 180.0
    $g.DrawLine($handPen, $c, $c, ($c + 44 * $k * [Math]::Cos($hourA)), ($c + 44 * $k * [Math]::Sin($hourA)))
    $g.DrawLine($handPen, $c, $c, ($c + 66 * $k * [Math]::Cos($minA)), ($c + 66 * $k * [Math]::Sin($minA)))

    # центр
    $cd = [Math]::Max(2, 8 * $k)
    $cdBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 219, 233, 255))
    $g.FillEllipse($cdBrush, ($c - $cd), ($c - $cd), (2 * $cd), (2 * $cd))

    $g.Dispose()
    return $bmp
}

function Png-Bytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return ,$bytes
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = @{}
foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    $images[$s] = Png-Bytes $bmp
    if ($s -eq 256) {
        New-Item -ItemType Directory -Force (Split-Path $PngOut) | Out-Null
        New-Item -ItemType Directory -Force (Split-Path $AndroidPngOut) | Out-Null
        [IO.File]::WriteAllBytes($PngOut, $images[$s])
        [IO.File]::WriteAllBytes($AndroidPngOut, $images[$s])
    }
    $bmp.Dispose()
}

$ms = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter $ms
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $bytes = $images[$s]
    $dim = if ($s -ge 256) { [Byte]0 } else { [Byte]$s }
    $bw.Write($dim); $bw.Write($dim); $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($s in $sizes) { $bw.Write([byte[]]$images[$s]) }
New-Item -ItemType Directory -Force (Split-Path $IcoOut) | Out-Null
[IO.File]::WriteAllBytes($IcoOut, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

Write-Host ("Icon written: {0} ({1} KB)" -f $IcoOut, [math]::Round((Get-Item $IcoOut).Length / 1KB, 1))
Write-Host ("PNG: {0}, {1}" -f $PngOut, $AndroidPngOut)
