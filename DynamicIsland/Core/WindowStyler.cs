using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DynamicIsland.Core;

/// <summary>Makes a window behave like an overlay: hidden from Alt-Tab, never steals focus, flush with the top-center of the screen.</summary>
static class WindowStyler
{
    const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

    /// <summary>Puts the island back above other always-on-top windows (and shows it if something hid it) without taking focus, and re-centres it: the screen size may have changed.</summary>
    public static void KeepOnTop(Window w)
    {
        w.Left = (SystemParameters.PrimaryScreenWidth - w.Width) / 2;
        w.Top = 0;
        SetWindowPos(new WindowInteropHelper(w).Handle, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

    static IntPtr _previous;

    /// <summary>
    /// The island normally never takes the keyboard. While you type your own answer it must: the no-activate style is lifted and the window
    /// comes forward; when typing ends, the style is restored and the window you were in gets the keyboard back.
    /// </summary>
    public static void AllowTyping(Window w, bool on)
    {
        var h = new WindowInteropHelper(w).Handle;
        var style = GetWindowLong(h, GWL_EXSTYLE);
        if (on)
        {
            if ((style & WS_EX_NOACTIVATE) == 0) return;
            _previous = GetForegroundWindow();
            SetWindowLong(h, GWL_EXSTYLE, style & ~WS_EX_NOACTIVATE);
            SourceApp.Bring(h);
        }
        else
        {
            if ((style & WS_EX_NOACTIVATE) != 0) return;
            SetWindowLong(h, GWL_EXSTYLE, style | WS_EX_NOACTIVATE);
            if (GetForegroundWindow() == h && _previous != IntPtr.Zero && _previous != h) SetForegroundWindow(_previous);
            _previous = IntPtr.Zero;
        }
    }

    /// <param name="excludeFromCapture">Hides this window from screen captures, so the glass can sample what is behind it.</param>
    public static void Apply(Window w, bool excludeFromCapture)
    {
        var h = new WindowInteropHelper(w).Handle;
        SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        if (excludeFromCapture) SetWindowDisplayAffinity(h, WDA_EXCLUDEFROMCAPTURE);
        w.Left = (SystemParameters.PrimaryScreenWidth - w.Width) / 2;
        w.Top = 0;
    }
}
