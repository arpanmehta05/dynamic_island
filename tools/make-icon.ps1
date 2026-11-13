# Draws the app icon (the island's violet face on a dark glass tile) and writes DynamicIsland\App.ico
Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $k = $size / 256.0
    function C([int]$a, [int]$r, [int]$gr, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $gr, $b) }
    function B([int]$a, [int]$r, [int]$gr, [int]$b) { New-Object System.Drawing.SolidBrush (C $a $r $gr $b) }

    # tile: dark smoked glass, lighter at the top, with a thin lit edge
    $r = 58 * $k
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $d = 2 * $r
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90); $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90); $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90); $path.CloseFigure()
    $tile = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (C 255 46 44 66), (C 255 12 12 20), 90
    $g.FillPath($tile, $path)
    $g.SetClip($path)
    # a violet glow rising behind the face
    $glow = New-Object System.Drawing.Drawing2D.GraphicsPath; $glow.AddEllipse(-20 * $k, 60 * $k, 296 * $k, 296 * $k)
    $gb = New-Object System.Drawing.Drawing2D.PathGradientBrush $glow
    $gb.CenterColor = C 120 150 120 255; $gb.SurroundColors = @((C 0 150 120 255))
    $g.FillPath($gb, $glow)
    $g.ResetClip()
    if ($size -ge 32) {
        $edge = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (C 120 255 255 255), (C 20 255 255 255), 70
        $g.DrawPath((New-Object System.Drawing.Pen $edge, ([Math]::Max(1, 3 * $k))), $path)
    }

    # the island's face: a soft violet blob (the same character as Views\AvatarControl)
    $cx = 128 * $k; $cy = 132 * $k; $cr = 92 * $k
    $bodyRect = New-Object System.Drawing.RectangleF ($cx - $cr), ($cy - $cr), (2 * $cr), (2 * $cr)
    $body = New-Object System.Drawing.Drawing2D.LinearGradientBrush $bodyRect, (C 255 166 196 255), (C 255 192 124 255), 60
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @((C 255 166 196 255), (C 255 154 140 255), (C 255 192 124 255)); $blend.Positions = @(0.0, 0.55, 1.0)
    $body.InterpolationColors = $blend
    $g.FillEllipse($body, $bodyRect)
    # sheen on the forehead
    $sheenRect = New-Object System.Drawing.RectangleF ($cx - 58 * $k), ($cy - 84 * $k), (104 * $k), (62 * $k)
    $sheen = New-Object System.Drawing.Drawing2D.LinearGradientBrush $sheenRect, (C 95 255 255 255), (C 0 255 255 255), 90
    $g.FillEllipse($sheen, $sheenRect)

    # big eyes, rosy cheeks, a small smile
    foreach ($sx in -1, 1) {
        $ex = $cx + $sx * 29 * $k; $ey = $cy - 6 * $k
        $g.FillEllipse((B 255 255 255 255), $ex - 20 * $k, $ey - 27 * $k, 40 * $k, 55 * $k)
        $g.FillEllipse((B 255 28 24 48), $ex - 12 * $k, $ey - 10 * $k, 24 * $k, 33 * $k)
        $g.FillEllipse((B 255 255 255 255), $ex - 8 * $k, $ey - 6 * $k, 10 * $k, 10 * $k)
        $g.FillEllipse((B 180 255 141 181), $cx + $sx * 64 * $k - 15 * $k, $cy + 22 * $k, 30 * $k, 18 * $k)
    }
    $pen = New-Object System.Drawing.Pen (C 255 43 36 82), ([Math]::Max(1.2, 8 * $k)); $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawArc($pen, $cx - 18 * $k, $cy + 20 * $k, 36 * $k, 24 * $k, 20, 140)
    $g.Dispose(); $bmp
}

# Small sizes use the classic bitmap (DIB) encoding that every Windows component understands; only 256px uses PNG.
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $w, $h), 'ReadOnly', 'Format32bppArgb')
    $pixels = New-Object byte[] ($w * $h * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length); $bmp.UnlockBits($data)
    $ms = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([uint32]40); $bw.Write([int]$w); $bw.Write([int]($h * 2)); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]0); $bw.Write([uint32]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
    for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($pixels, $y * $w * 4, $w * 4) }          # bottom-up rows
    $maskRow = [int]([Math]::Ceiling($w / 32.0) * 4); $bw.Write((New-Object byte[] ($maskRow * $h)))  # AND mask: all opaque (alpha does the work)
    ,$ms.ToArray()
}
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -ge 256) { $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); ,$ms.ToArray() }
    else { Get-DibBytes $bmp }
    $bmp.Dispose()
}

# ICO container with PNG-compressed images
$out = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$target = Join-Path $PSScriptRoot '..\DynamicIsland\App.ico'
[IO.File]::WriteAllBytes($target, $out.ToArray())
"wrote $((Resolve-Path $target).Path)  ($([int]((Get-Item $target).Length/1KB)) KB, sizes: $($sizes -join ', '))"
