using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland.Core;

/// <summary>
/// Raises <see cref="Changed"/> while the Snipping Tool's screen-capture overlay (Print Screen, Win+Shift+S) is on screen. It covers the whole
/// screen and puts its toolbar at the top centre, exactly where the island is, but never becomes the foreground window, so the
/// foreground checks can't see it: it is found through window show / hide events instead.
/// </summary>
sealed class SnipWatcher
{
    const uint EVENT_OBJECT_CREATE = 0x8000, EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_HIDE = 0x8003, EVENT_OBJECT_SHOW = 0x8002, WINEVENT_OUTOFCONTEXT = 0;
    const string OverlayClass = "SnipOverlayRootWindow";

    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int max);

    readonly WinEventProc _proc; // kept in a field so the GC can't free the native callback
    bool _open;
    IntPtr _overlay;

    public event Action<bool>? Changed;

    public SnipWatcher()
    {
        _proc = OnEvent;
        SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_HIDE, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0) return; // not a window itself
        try
        {
            if (evt is EVENT_OBJECT_SHOW or EVENT_OBJECT_CREATE)
            {
                var cls = new StringBuilder(32);
                GetClassName(hwnd, cls, cls.Capacity);
                if (cls.ToString() != OverlayClass) return;
                _overlay = hwnd;
            }
            else if (hwnd != _overlay) return; // a destroyed window has no class any more, so it is recognised by its handle

            var open = evt is EVENT_OBJECT_SHOW or EVENT_OBJECT_CREATE;
            if (open == _open) return;
            _open = open;
            Changed?.Invoke(open);
        }
        catch (Exception ex) { App.Log("snip event", ex); }
    }
}
