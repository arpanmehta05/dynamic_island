using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Core;

/// <summary>Finds the app behind a media session: its logo, and its window (to bring to the front).</summary>
static class SourceApp
{
    const int GW_OWNER = 4, SW_RESTORE = 9, DWMWA_CLOAKED = 14;

    delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, int cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);

    /// <summary>Candidate process names for an app id such as "Spotify.exe" or "Chrome._crx_abc".</summary>
    static string[] Names(string id)
    {
        var s = id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? id[..^4] : id;
        return [s, s.Split('.', '!', '_')[0]];
    }

    /// <summary>The app's own icon (e.g. the VLC cone), or null when it can't be found.</summary>
    public static ImageSource? Icon(string id)
    {
        try
        {
            foreach (var name in Names(id))
            {
                var path = Process.GetProcessesByName(name).Select(p => p.MainModule?.FileName).FirstOrDefault(f => f is not null);
                if (path is null) continue;
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is null) continue;
                var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
        }
        catch { /* protected process: no icon */ }
        return null;
    }

    /// <summary>
    /// Brings the app's window to the front. Prefers a window whose title contains the media title (the right browser tab/app),
    /// then any window of the matching process, and for packaged apps falls back to launching it (which focuses the running one).
    /// </summary>
    public static bool Activate(string id, string title)
    {
        var names = Names(id);
        var procNames = new Dictionary<uint, string>();
        IntPtr best = IntPtr.Zero;
        var bestScore = 0;

        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
            DwmGetWindowAttribute(h, DWMWA_CLOAKED, out var cloaked, sizeof(int));
            if (cloaked != 0) return true;

            var text = new StringBuilder(256);
            if (GetWindowText(h, text, text.Capacity) == 0) return true;
            GetWindowThreadProcessId(h, out var pid);
            if (pid == Environment.ProcessId) return true;

            if (!procNames.TryGetValue(pid, out var proc))
            {
                try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { proc = ""; }
                procNames[pid] = proc;
            }

            var score = (title.Length >= 4 && text.ToString().Contains(title, StringComparison.OrdinalIgnoreCase) ? 2 : 0)
                      + (names.Any(n => n.Equals(proc, StringComparison.OrdinalIgnoreCase)) ? 1 : 0);
            if (score > bestScore) { best = h; bestScore = score; }
            return true;
        }, IntPtr.Zero);

        if (best != IntPtr.Zero) { Bring(best); return true; }
        if (id.Contains('!')) { Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{id}") { UseShellExecute = true }); return true; }
        return false;
    }

    /// <summary>Starts the program a running process came from (an app closed to the tray usually reopens its window when started again).</summary>
    public static void Launch(string processName)
    {
        try
        {
            var path = Process.GetProcessesByName(processName).Select(p => p.MainModule?.FileName).FirstOrDefault(f => f is not null);
            if (path is not null) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { /* protected or packaged app: nothing more to try */ }
    }

    internal static void Bring(IntPtr h)
    {
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);

        // Windows only lets the foreground thread hand over focus: borrow its input queue for a moment.
        var fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var me = GetCurrentThreadId();
        AttachThreadInput(me, fgThread, true);
        BringWindowToTop(h);
        SetForegroundWindow(h);
        AttachThreadInput(me, fgThread, false);
    }
}
