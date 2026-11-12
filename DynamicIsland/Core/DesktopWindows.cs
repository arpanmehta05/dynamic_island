using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland.Core;

/// <summary>Which processes currently have a real window on the desktop (as opposed to only living in the tray).</summary>
static class DesktopWindows
{
    const int GW_OWNER = 4, GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, DWMWA_CLOAKED = 14;

    delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, int cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);

    /// <summary>
    /// Process ids that own at least one visible, titled, top-level window. Minimized windows count (they are on the taskbar);
    /// hidden windows, tool windows and windows of suspended apps do not. A chat app closed to the tray has none.
    /// </summary>
    public static HashSet<int> PidsWithWindow()
    {
        var pids = new HashSet<int>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero || GetWindowTextLength(h) == 0) return true;

            var ex = GetWindowLong(h, GWL_EXSTYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return true;

            // 2 = on another virtual desktop (still "open"); anything else cloaked is a suspended/hidden app window
            DwmGetWindowAttribute(h, DWMWA_CLOAKED, out var cloaked, sizeof(int));
            if (cloaked != 0 && cloaked != 2) return true;

            GetWindowThreadProcessId(h, out var pid);
            pids.Add((int)pid);
            return true;
        }, IntPtr.Zero);
        return pids;
    }
}
