using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Core;

/// <summary>Tiny tileable film-grain brush; the fine grain is what stops a blur from looking like plain plastic.</summary>
static class GlassNoise
{
    public static readonly ImageBrush Brush = Create();

    static ImageBrush Create()
    {
        const int n = 128;
        var px = new byte[n * n * 4];
        var rnd = new Random(7);
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = px[i + 1] = px[i + 2] = (byte)rnd.Next(256);
            px[i + 3] = 46;
        }
        var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, px, n * 4);
        var brush = new ImageBrush(bmp)
        {
            TileMode = TileMode.Tile, Stretch = Stretch.None,
            Viewport = new Rect(0, 0, n, n), ViewportUnits = BrushMappingMode.Absolute, Opacity = 0.08
        };
        brush.Freeze();
        return brush;
    }
}
