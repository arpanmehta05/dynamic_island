using System.Windows.Interop;

namespace DynamicIsland.Core;

/// <summary>
/// A two-finger horizontal swipe on a touchpad (or a tilt wheel) over the window: raised once per swipe, +1 for right and -1 for left.
/// A swipe is a stream of small WM_MOUSEHWHEEL messages that keeps trickling on after the fingers lift, so one switch locks
/// the gesture until the stream has been quiet for a moment.
/// </summary>
sealed class SwipeGesture
{
    // Windows delivers a touchpad's sideways scroll as WM_POINTERHWHEEL to windows that take pointer input and turns it into WM_MOUSEHWHEEL
    // only if nobody handled it: listen for both so it works wherever the pointer is.
    const int WM_MOUSEHWHEEL = 0x020E, WM_POINTERHWHEEL = 0x024F, Needed = 70;
    static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(250);

    DateTime _last;
    int _sum;
    bool _spent;

    public event Action<int>? Swiped;

    public SwipeGesture(HwndSource source) => source.AddHook(Hook);

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_MOUSEHWHEEL && msg != WM_POINTERHWHEEL) return IntPtr.Zero;
        handled = true;

        var now = DateTime.UtcNow;
        if (now - _last > Quiet) { _sum = 0; _spent = false; }
        _last = now;
        if (_spent) return IntPtr.Zero;

        _sum += (short)((wParam.ToInt64() >> 16) & 0xFFFF);
        if (Math.Abs(_sum) < Needed) return IntPtr.Zero;
        _spent = true;
        Swiped?.Invoke(Math.Sign(_sum));
        return IntPtr.Zero;
    }
}
