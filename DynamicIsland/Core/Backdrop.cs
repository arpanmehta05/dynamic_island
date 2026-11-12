using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace DynamicIsland.Core;

/// <summary>
/// Samples the screen behind the island (the island itself is excluded from capture) into a bitmap the glass bends and blurs.
/// New frames are eased in rather than swapped, so a window switch fades. Runs at ~30fps while the picture is changing
/// (or just after <see cref="Poke"/>), and drops to 4fps once it has been still for a moment.
/// While the island is collapsed only the small area around the notch is sampled; the full area is only needed when expanded.
/// </summary>
sealed class Backdrop
{
    public const int Pad = 40;

    // Collapsed notch: 180 wide, 34 tall, centred in the window. Keep a margin for the lens/blur and the spring overshoot.
    const double NotchLeft = 110 - 40, NotchWidth = 180 + 80, NotchHeight = 34 + 56;

    static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(33), FastCollapsed = TimeSpan.FromMilliseconds(80), Idle = TimeSpan.FromMilliseconds(250), ShrinkDelay = TimeSpan.FromMilliseconds(900);
    const int Ease = 115;      // fraction (of 256) of the remaining difference closed per frame
    const int CalmFrames = 8;  // still frames before slowing down

    readonly Window _w;
    readonly Bitmap _buf;
    readonly Graphics _g;
    readonly double _dpi;
    readonly int _width, _height, _stride;
    readonly Rectangle _full, _notch;
    readonly byte[] _target, _shown;
    readonly DispatcherTimer _timer = new(DispatcherPriority.Normal) { Interval = FastCollapsed };
    readonly DispatcherTimer _shrink = new() { Interval = ShrinkDelay };
    Rectangle _region;
    TimeSpan Following => _region == _full ? Fast : FastCollapsed; // the tiny notch is fine at ~12fps; expanded follows at 30
    int _calm;
    bool _snap = true;

    public WriteableBitmap Bitmap { get; }

    /// <summary>Stops sampling the screen while the island is hidden.</summary>
    public bool Active
    {
        set
        {
            _timer.IsEnabled = value;
            if (!value) return;
            _snap = true; // the picture changed while hidden: show it straight away
            Refresh();
        }
    }

    /// <summary>True while the island is expanded (needs the whole area). Shrinking waits for the closing animation to finish.</summary>
    public bool Expanded
    {
        set
        {
            _shrink.Stop();
            if (value)
            {
                if (_region == _full) return;
                _region = _full;
                _snap = true; // the parts outside the notch are stale
                Poke();
            }
            else _shrink.Start();
        }
    }

    /// <param name="fullHeight">The window's expanded height (the window itself is shorter while collapsed).</param>
    public Backdrop(Window w, double fullHeight)
    {
        _w = w;
        _dpi = VisualTreeHelper.GetDpi(w).DpiScaleX;
        _width = (int)((w.Width + 2 * Pad) * _dpi);
        _height = (int)((fullHeight + Pad) * _dpi);
        _stride = _width * 4;
        _full = new Rectangle(0, 0, _width, _height);
        _notch = new Rectangle((int)((NotchLeft + Pad) * _dpi), 0,
                               Math.Min((int)(NotchWidth * _dpi), _width), Math.Min((int)(NotchHeight * _dpi), _height));
        _region = _notch;

        _buf = new Bitmap(_width, _height, PixelFormat.Format32bppPArgb);
        _g = Graphics.FromImage(_buf);
        _target = new byte[_stride * _height];
        _shown = new byte[_target.Length];
        Bitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32, null);

        _shrink.Tick += (_, _) => { _shrink.Stop(); _region = _notch; };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    /// <summary>Something that probably changes the picture just happened (window switch, move, hover): follow it at full speed.</summary>
    public void Poke()
    {
        _calm = 0;
        if (_timer.IsEnabled && _timer.Interval != Following) { _timer.Interval = Following; Refresh(); }
    }

    public void Refresh()
    {
        var r = _region;
        var left = (int)((_w.Left - Pad) * _dpi);
        _g.CopyFromScreen(left + r.X, r.Y, r.X, r.Y, r.Size);

        var d = _buf.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        for (var row = 0; row < r.Height; row++)
            Marshal.Copy(d.Scan0 + row * d.Stride, _target, (r.Y + row) * _stride + r.X * 4, r.Width * 4);
        _buf.UnlockBits(d);

        if (_snap)
        {
            for (var row = 0; row < r.Height; row++)
                Buffer.BlockCopy(_target, (r.Y + row) * _stride + r.X * 4, _shown, (r.Y + row) * _stride + r.X * 4, r.Width * 4);
            _snap = false;
            Push(r);
            return;
        }

        int maxDiff = 0;
        for (var row = 0; row < r.Height; row++)
        {
            var i = (r.Y + row) * _stride + r.X * 4;
            for (var end = i + r.Width * 4; i < end; i += 4)
                for (var c = 0; c < 3; c++)
                {
                    int diff = _target[i + c] - _shown[i + c];
                    if (diff == 0) continue;
                    int abs = Math.Abs(diff);
                    if (abs > maxDiff) maxDiff = abs;
                    int step = diff * Ease / 256;
                    _shown[i + c] = (byte)(_shown[i + c] + (step != 0 ? step : Math.Sign(diff)));
                }
        }

        if (maxDiff > 0) Push(r);
        _calm = maxDiff > 4 ? 0 : _calm + 1;
        var wanted = _calm > CalmFrames ? Idle : Following;
        if (_timer.Interval != wanted) _timer.Interval = wanted;
    }

    void Push(Rectangle r) =>
        Bitmap.WritePixels(new Int32Rect(r.X, r.Y, r.Width, r.Height), _shown, _stride, r.X, r.Y);
}
