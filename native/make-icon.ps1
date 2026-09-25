# Generates native\app.ico (16..256 px, PNG-compressed entries) for SoundMaster.
# Look: ink rounded tile with a lilac -> magenta waveform, matching the Theme.cs palette.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Render([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Tile: tiny sizes fill the whole cell so they stay legible.
    $inset = if ($s -le 24) { 0 } else { $s * 0.04 }
    $tile = $s - 2 * $inset
    $path = RoundRect $inset $inset $tile $tile ($tile * 0.24)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x12, 0x12, 0x12))), $path)

    # Waveform bars: fewer, fatter bars at small sizes.
    if ($s -le 24) { $hh = @(0.20, 0.36, 0.24) }
    elseif ($s -le 48) { $hh = @(0.14, 0.30, 0.40, 0.26, 0.16) }
    else { $hh = @(0.10, 0.22, 0.36, 0.28, 0.40, 0.24, 0.12) }
    $n = $hh.Count
    $area = $tile * 0.64
    $left = $inset + ($tile - $area) / 2
    $step = $area / $n
    $bw = [Math]::Max(1.5, $step * 0.58)
    $mid = $s / 2.0
    $lilac = [System.Drawing.Color]::FromArgb(255, 0xc5, 0xb0, 0xf4)
    $magenta = [System.Drawing.Color]::FromArgb(255, 0xff, 0x3d, 0x8b)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF $left, 0, $area, $s), $lilac, $magenta, 0.0
    for ($i = 0; $i -lt $n; $i++) {
        $cx = $left + $step * ($i + 0.5)
        $half = $tile * $hh[$i]
        $bar = RoundRect ($cx - $bw / 2) ($mid - $half) $bw (2 * $half) ($bw / 2 - 0.01)
        $g.FillPath($grad, $bar)
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) {
        # Vista+ PNG entry; also kept as a standalone preview.
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Save((Join-Path $PSScriptRoot 'app-256.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # Classic 32-bit DIB entry: System.Drawing.Icon (Form.Icon) cannot decode small PNG entries.
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([UInt32]40); $bw.Write([Int32]$s); $bw.Write([Int32]($s * 2))
        $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
        $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
        $bw.Write([UInt32]($s * $s * 4 + $maskStride * $s))
        $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
        for ($y = $s - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $s; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
            }
        }
        $bw.Write((New-Object byte[] ($maskStride * $s)))   # AND mask unused with alpha
        $bw.Flush()
    }
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @(); foreach ($s in $sizes) { $pngs += ,(Render $s) }

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'app.ico'), $out.ToArray())
Write-Host "Wrote app.ico ($($sizes -join ', ') px)"
