using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace DynamicIsland.Core;

/// <summary>
/// Raises <see cref="Changed"/> when the foreground window becomes (or stops being) a fullscreen window.
/// Driven by WinEvent hooks, so there is no polling. Two tests, either is enough:
/// borderless + covers the whole monitor, or Windows' own "a fullscreen app is running" state (catches browsers
/// and players whose fullscreen window still carries a title bar style). A normal maximized window doesn't count.
/// Windows updates its own state a moment after a window changes, so every event also schedules a re-check.
/// </summary>
sealed class FullscreenWatcher
{
    const uint EVENT_SYSTEM_FOREGROUND = 0x3, EVENT_OBJECT_LOCATIONCHANGE = 0x800B, WINEVENT_OUTOFCONTEXT = 0;
    const int GWL_STYLE = -16, WS_CAPTION = 0xC00000, MONITOR_DEFAULTTONEAREST = 2;

    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int Size; public RECT Monitor, Work; public int Flags; }

    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, int flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

    // QUNS_BUSY, QUNS_RUNNING_D3D_FULL_SCREEN, QUNS_PRESENTATION_MODE
    static bool WindowsSaysFullscreen() => SHQueryUserNotificationState(out var s) == 0 && s is 2 or 3 or 4;

    readonly DispatcherTimer _recheck = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly IntPtr _own;
    readonly WinEventProc _proc; // kept in a field so the GC can't free the native callback
    bool _fullscreen;

    public event Action<bool>? Changed;

    /// <summary>The foreground window switched, moved or resized: the picture behind the island is probably about to change.</summary>
    public event Action? Activity;
    public bool IsFullscreen => _fullscreen;

    public FullscreenWatcher(IntPtr ownWindow)
    {
        _own = ownWindow;
        _proc = OnEvent;
        _recheck.Tick += (_, _) => { _recheck.Stop(); Evaluate(); };
        SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        Evaluate();
        _recheck.Start();
    }

    void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Location changes fire constantly; only the foreground window's own frame matters.
        if (evt == EVENT_OBJECT_LOCATIONCHANGE && (idObject != 0 || hwnd != GetForegroundWindow())) return;
        Activity?.Invoke();
        Evaluate();
        _recheck.Stop(); _recheck.Start(); // debounced: fires 400ms after the last event
    }

    void Evaluate()
    {
        var now = Check();
        if (now == _fullscreen) return;
        _fullscreen = now;
        Changed?.Invoke(now);
    }

    bool Check()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero || h == _own || IsIconic(h) || !IsWindowVisible(h)) return false;

        var cls = new StringBuilder(64);
        GetClassName(h, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        if (WindowsSaysFullscreen()) return true;
        if ((GetWindowLong(h, GWL_STYLE) & WS_CAPTION) == WS_CAPTION) return false;

        GetWindowRect(h, out var r);
        var mi = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST), ref mi);
        return r.L <= mi.Monitor.L && r.T <= mi.Monitor.T && r.R >= mi.Monitor.R && r.B >= mi.Monitor.B;
    }
}
