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
/// Samples the screen behind the island (the island itself is excluded from capture) into a small bitmap the glass bends and blurs.
/// The picture is kept at a quarter of the screen's resolution: that is most of the frost (the shader only has to smooth it)
/// and makes every frame sixteen times cheaper to ease and upload.
/// New frames are eased in rather than swapped, so a window switch fades. Runs at ~30fps while the picture is changing
/// (or just after <see cref="Poke"/>), and drops to 4fps once it has been still for a moment.
/// While the island is collapsed only the small area around the notch is sampled; the full area is only needed when expanded.
/// </summary>
sealed class Backdrop
{
    public const int Pad = 40;

    const int Down = 4; // screen pixels per backdrop pixel

    // Collapsed pill: 180 wide, 32 tall, centred in the window, 6 below the screen edge. Keep a margin for the lens/blur and the spring overshoot.
    const double NotchLeft = 130 - 40, NotchWidth = 180 + 80, NotchHeight = 6 + 32 + 50;

    static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(33), FastCollapsed = TimeSpan.FromMilliseconds(80), Idle = TimeSpan.FromMilliseconds(250), ShrinkDelay = TimeSpan.FromMilliseconds(900);
    const int Ease = 115;      // fraction (of 256) of the remaining difference closed per frame
    const int CalmFrames = 8;  // still frames before slowing down

    readonly Window _w;
    readonly Bitmap _grab, _buf;
    readonly Graphics _screen, _g;
    readonly bool _frozen;

    /// <summary>False for liquid glass: the picture is only softened by its low resolution, not blurred.</summary>
    public bool Frost { get; set; }
    readonly double _dpi;
    readonly int _width, _height, _stride;
    readonly Rectangle _full, _notch;
    readonly byte[] _target, _shown, _scratch;
    readonly DispatcherTimer _timer = new(DispatcherPriority.Normal) { Interval = FastCollapsed };
    readonly DispatcherTimer _shrink = new() { Interval = ShrinkDelay };
    Rectangle _region;
    TimeSpan Following => _region == _notch ? FastCollapsed : Fast; // the tiny notch is fine at ~12fps; expanded follows at 30
    int _calm;
    bool _snap = true;

    public WriteableBitmap Bitmap { get; }

    /// <summary>Stops sampling the screen while the island is hidden.</summary>
    public bool Active
    {
        set
        {
            if (_frozen) return;
            _timer.IsEnabled = value;
            if (!value) return;
            _snap = true; // the picture changed while hidden: show it straight away
            Refresh();
        }
    }

    /// <summary>The island is open and this tall (window height): sample that much of the screen.</summary>
    public void Expand(double height)
    {
        _shrink.Stop();
        var region = new Rectangle(0, 0, _width, Math.Min(_height, (int)(height * _dpi / Down) + 1));
        if (region == _region) return;
        _snap = region.Height > _region.Height || _region == _notch; // the newly covered parts are stale
        _region = region;
        Poke();
    }

    /// <summary>Back to the pill: only its surroundings are needed, once the closing animation has finished.</summary>
    public void Collapse() => _shrink.Start();

    /// <param name="fullHeight">The tallest the window gets (it is much shorter while collapsed).</param>
    /// <param name="frozen">Development only: take one picture before the island is on screen and keep it, so screenshots of a capturable island don't show the island in its own glass.</param>
    public Backdrop(Window w, double fullHeight, bool frost, bool frozen)
    {
        _w = w;
        Frost = frost;
        _frozen = frozen;
        _dpi = VisualTreeHelper.GetDpi(w).DpiScaleX;
        _width = (int)((w.Width + 2 * Pad) * _dpi) / Down;
        _height = (int)(fullHeight * _dpi) / Down;
        _stride = _width * 4;
        _full = new Rectangle(0, 0, _width, _height);
        _notch = new Rectangle((int)((NotchLeft + Pad) * _dpi) / Down, 0,
                               Math.Min((int)(NotchWidth * _dpi) / Down, _width), Math.Min((int)(NotchHeight * _dpi) / Down, _height));
        _region = frozen ? _full : _notch;

        _grab = new Bitmap(_width * Down, _height * Down, PixelFormat.Format32bppPArgb);
        _screen = Graphics.FromImage(_grab);
        _buf = new Bitmap(_width, _height, PixelFormat.Format32bppPArgb);
        _g = Graphics.FromImage(_buf);
        _g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear; // averages the pixels it shrinks, so small text doesn't shimmer
        _g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        _target = new byte[_stride * _height];
        _shown = new byte[_target.Length];
        _scratch = new byte[_target.Length];
        Bitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32, null);

        _shrink.Tick += (_, _) => { _shrink.Stop(); _region = _notch; };
        _timer.Tick += (_, _) => Refresh();
        Refresh();
        if (!frozen) _timer.Start();
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
        var big = new Rectangle(r.X * Down, r.Y * Down, r.Width * Down, r.Height * Down);
        // While the PC is locked (or on the secure desktop) the screen can't be read: keep the last picture and snap to the real one afterwards
        try { _screen.CopyFromScreen(left + big.X, big.Y, big.X, big.Y, big.Size); }
        catch (System.ComponentModel.Win32Exception) { _snap = true; return; }
        _g.DrawImage(_grab, r, big, GraphicsUnit.Pixel);

        var d = _buf.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        for (var row = 0; row < r.Height; row++)
            Marshal.Copy(d.Scan0 + row * d.Stride, _target, (r.Y + row) * _stride + r.X * 4, r.Width * 4);
        _buf.UnlockBits(d);
        for (var pass = 0; Frost && pass < 2; pass++) // two box blurs are close to a Gaussian: smooth frost with no ghost copies of bright text
        {
            Box(_target, _scratch, r, 4, r.Width, r.Height);
            Box(_scratch, _target, r, _stride, r.Height, r.Width);
        }

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

    /// <summary>Running-sum box blur of the region along one axis (step = bytes to the next pixel on that axis), edges repeated.</summary>
    void Box(byte[] from, byte[] to, Rectangle r, int step, int length, int lines)
    {
        const int R = 3;
        var across = step == 4 ? _stride : 4;
        for (var line = 0; line < lines; line++)
        {
            var start = r.Y * _stride + r.X * 4 + line * across;
            for (var c = 0; c < 3; c++)
            {
                var sum = 0;
                for (var k = -R; k <= R; k++) sum += from[start + Math.Clamp(k, 0, length - 1) * step + c];
                for (var i = 0; i < length; i++)
                {
                    to[start + i * step + c] = (byte)(sum / (2 * R + 1));
                    sum += from[start + Math.Min(i + R + 1, length - 1) * step + c] - from[start + Math.Max(i - R, 0) * step + c];
                }
            }
        }
    }

    void Push(Rectangle r) =>
        Bitmap.WritePixels(new Int32Rect(r.X, r.Y, r.Width, r.Height), _shown, _stride, r.X, r.Y);
}
