# Draws the app icon (dark tile, the black island hanging from the top with a cover and equalizer bars) and writes DynamicIsland\App.ico
Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $k = $size / 256.0

    # tile
    $r = 56 * $k
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $d = 2 * $r
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90); $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90); $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90); $path.CloseFigure()
    $tile = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 52, 52, 62)), ([System.Drawing.Color]::FromArgb(255, 22, 22, 28)), 90
    $g.FillPath($tile, $path)

    # a soft glow under the island, like light passing through glass
    $glow = New-Object System.Drawing.Drawing2D.GraphicsPath; $glow.AddEllipse(30 * $k, 70 * $k, 196 * $k, 170 * $k)
    $gb = New-Object System.Drawing.Drawing2D.PathGradientBrush $glow
    $gb.CenterColor = [System.Drawing.Color]::FromArgb(120, 90, 130, 255); $gb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 90, 130, 255))
    $g.SetClip($path); $g.FillPath($gb, $glow); $g.ResetClip()

    # the island: a black pill hanging from the top edge (flat top, rounded bottom) with a light rim
    $nx = 28 * $k; $nw = 200 * $k; $nh = 112 * $k; $nr = 46 * $k
    $np = New-Object System.Drawing.Drawing2D.GraphicsPath
    $np.AddLine($nx, 0, $nx + $nw, 0)
    $np.AddLine($nx + $nw, 0, $nx + $nw, $nh - $nr)
    $np.AddArc($nx + $nw - 2 * $nr, $nh - 2 * $nr, 2 * $nr, 2 * $nr, 0, 90)
    $np.AddLine($nx + $nw - $nr, $nh, $nx + $nr, $nh)
    $np.AddArc($nx, $nh - 2 * $nr, 2 * $nr, 2 * $nr, 90, 90)
    $np.CloseFigure()
    $g.FillPath([System.Drawing.Brushes]::Black, $np)
    $rim = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), ([Math]::Max(1, 3 * $k))
    $g.DrawPath($rim, $np)

    # inside the island: a cover on the left, equalizer bars on the right (it is a media island)
    $cs = 56 * $k; $cx = $nx + 30 * $k; $cy = 26 * $k
    $cover = New-Object System.Drawing.Drawing2D.GraphicsPath; $cr = 14 * $k
    $cover.AddArc($cx, $cy, 2 * $cr, 2 * $cr, 180, 90); $cover.AddArc($cx + $cs - 2 * $cr, $cy, 2 * $cr, 2 * $cr, 270, 90)
    $cover.AddArc($cx + $cs - 2 * $cr, $cy + $cs - 2 * $cr, 2 * $cr, 2 * $cr, 0, 90); $cover.AddArc($cx, $cy + $cs - 2 * $cr, 2 * $cr, 2 * $cr, 90, 90); $cover.CloseFigure()
    $cb = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF $cx, $cy, $cs, $cs), ([System.Drawing.Color]::FromArgb(255, 255, 122, 162)), ([System.Drawing.Color]::FromArgb(255, 110, 110, 255)), 45
    $g.FillPath($cb, $cover)
    $bw = 12 * $k; $bx = $nx + $nw - 30 * $k - 3 * $bw - 2 * 7 * $k; $base = $cy + $cs
    $heights = 30, 52, 38
    for ($i = 0; $i -lt 3; $i++) {
        $h = $heights[$i] * $k; $x = $bx + $i * ($bw + 7 * $k)
        $bp = New-Object System.Drawing.Drawing2D.GraphicsPath; $br = $bw / 2
        $bp.AddArc($x, $base - $h, $bw, $bw, 180, 180); $bp.AddArc($x, $base - $bw, $bw, $bw, 0, 180); $bp.CloseFigure()
        $g.FillPath([System.Drawing.Brushes]::White, $bp)
    }

    # a thin progress line along the bottom of the island
    $lw = $nw - 90 * $k
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 255, 255, 255))), $nx + 45 * $k, $nh - 26 * $k, $lw, [Math]::Max(1, 6 * $k))
    $g.FillRectangle([System.Drawing.Brushes]::White, $nx + 45 * $k, $nh - 26 * $k, $lw * 0.55, [Math]::Max(1, 6 * $k))
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
