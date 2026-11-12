# Draws the app icon (a cute round black island with a face and a chat-dots bubble on a violet tile) and writes DynamicIsland\App.ico
Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $k = $size / 256.0

    # tile: soft violet-to-blue
    $r = 56 * $k
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $d = 2 * $r
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90); $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90); $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90); $path.CloseFigure()
    $tile = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 132, 168, 255)), ([System.Drawing.Color]::FromArgb(255, 176, 118, 255)), 60
    $g.FillPath($tile, $path)

    # the island: a round black body
    $cx = 128 * $k; $cy = 134 * $k; $cr = 90 * $k
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath; $body.AddEllipse($cx - $cr, $cy - $cr, 2 * $cr, 2 * $cr)
    $bb = New-Object System.Drawing.Drawing2D.PathGradientBrush $body
    $bb.CenterColor = [System.Drawing.Color]::FromArgb(255, 44, 44, 56); $bb.SurroundColors = @([System.Drawing.Color]::FromArgb(255, 8, 8, 12))
    $bb.CenterPoint = New-Object System.Drawing.PointF ($cx - 28 * $k), ($cy - 36 * $k)
    $g.FillPath($bb, $body)

    # three dots on top, like a chat "typing" bubble
    $bub = New-Object System.Drawing.RectangleF (148 * $k), (22 * $k), (84 * $k), (38 * $k); $br = 19 * $k
    $bp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bp.AddArc($bub.X, $bub.Y, 2 * $br, 2 * $br, 90, 180); $bp.AddArc($bub.Right - 2 * $br, $bub.Y, 2 * $br, 2 * $br, 270, 180); $bp.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 255, 255, 255))), $bp)
    foreach ($i in 0..2) {
        $dx = (165 + 25 * $i) * $k; $dy = 41 * $k; $dr = 7 * $k
        $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 70 + 40 * $i, 70 + 40 * $i, 90 + 40 * $i))), $dx - $dr, $dy - $dr, 2 * $dr, 2 * $dr)
    }

    # face: shiny eyes, rosy cheeks, a small smile
    $white = [System.Drawing.Brushes]::White
    foreach ($sx in -1, 1) {
        $ex = $cx + $sx * 36 * $k; $ey = $cy - 6 * $k
        $g.FillEllipse($white, $ex - 17 * $k, $ey - 24 * $k, 34 * $k, 48 * $k)
        $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 20, 20, 30))), $ex - 10 * $k, $ey - 6 * $k, 20 * $k, 28 * $k)
        $g.FillEllipse($white, $ex - 6 * $k, $ey - 3 * $k, 8 * $k, 8 * $k)
        $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110, 255, 120, 160))), $cx + $sx * 66 * $k - 14 * $k, $cy + 28 * $k, 28 * $k, 17 * $k)
    }
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.2, 9 * $k)); $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawArc($pen, $cx - 22 * $k, $cy + 14 * $k, 44 * $k, 30 * $k, 25, 130)
    # soft highlight along the top edge of the tile
    $hi = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 0, 0, $size, ($size * 0.5)), ([System.Drawing.Color]::FromArgb(46, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), 90
    $g.SetClip($path); $g.FillRectangle($hi, 0, 0, $size, $size * 0.5); $g.ResetClip()
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
