using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland.Core;

/// <summary>
/// Players that don't publish to the Windows media controls (VLC, mpv, MPC, PotPlayer...). They are recognised by their window
/// (whose title carries the file name) and "playing" is read from Windows' audio sessions: a program that is currently producing sound
/// has an active session. Only looked at while Windows reports no media session of its own.
/// </summary>
static class LegacyPlayers
{
    sealed record Spec(string Process, string Display, string[] TitleSuffixes);

    static readonly Spec[] Known =
    [
        new("vlc", "VLC media player", [" - VLC media player"]),
        new("mpv", "mpv", [" - mpv"]),
        new("mpc-hc64", "MPC-HC", [" - MPC-HC"]), new("mpc-hc", "MPC-HC", [" - MPC-HC"]),
        new("mpc-be64", "MPC-BE", [" - MPC-BE"]), new("mpc-be", "MPC-BE", [" - MPC-BE"]),
        new("potplayermini64", "PotPlayer", [" - PotPlayer"]), new("potplayermini", "PotPlayer", [" - PotPlayer"]), new("potplayer64", "PotPlayer", [" - PotPlayer"]),
        new("wmplayer", "Windows Media Player", [" - Windows Media Player"]),
        new("kmplayer", "KMPlayer", [" - KMPlayer"]), new("gomplayer", "GOM Player", [" - GOM Player"]), new("smplayer", "SMPlayer", [" - SMPlayer"]),
        new("foobar2000", "foobar2000", [" [foobar2000]", " - foobar2000"]), new("musicbee", "MusicBee", [" - MusicBee"]),
        new("aimp", "AIMP", [" - AIMP"]), new("winamp", "Winamp", [" - Winamp"]),
    ];

    /// <param name="Process">Lower-case process name, e.g. "vlc".</param>
    public readonly record struct Found(int Pid, string Process, string Display, string Title, IntPtr Window, bool Playing);

    delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, int cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    static readonly Dictionary<int, string> ProcessNames = []; // pid -> name, so windows are not looked up again and again
    static int _calls;

    public static Found? Find()
    {
        if (++_calls % 120 == 0) ProcessNames.Clear(); // process ids get reused: forget now and then

        Found? best = null;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero) return true;
            var text = new StringBuilder(256);
            if (GetWindowText(h, text, text.Capacity) == 0) return true;

            GetWindowThreadProcessId(h, out var pidU);
            var pid = (int)pidU;
            if (!ProcessNames.TryGetValue(pid, out var name))
            {
                try { using var p = Process.GetProcessById(pid); name = p.ProcessName.ToLowerInvariant(); } catch { name = ""; }
                ProcessNames[pid] = name;
            }

            var spec = Array.Find(Known, s => s.Process == name);
            if (spec is null) return true;

            var title = Clean(text.ToString(), spec);
            if (title.Length == 0) return true; // the player is open but has nothing loaded
            if (best is null || title.Length > best.Value.Title.Length) best = new Found(pid, spec.Process, spec.Display, title, h, false);
            return true;
        }, IntPtr.Zero);

        return best is { } f ? f with { Playing = GuessPlaying(f.Pid) } : null;
    }

    /// <summary>"Movie.mkv - VLC media player" -> "Movie.mkv"; the bare player name means nothing is loaded.</summary>
    static string Clean(string title, Spec spec)
    {
        title = title.Trim();
        foreach (var suffix in spec.TitleSuffixes)
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { title = title[..^suffix.Length]; break; }
        title = title.Trim();
        return title.Equals(spec.Display, StringComparison.OrdinalIgnoreCase) || title.Equals(spec.Process, StringComparison.OrdinalIgnoreCase) ? "" : title;
    }

    // ---- guessing "playing" when the player offers nothing better ----
    // Windows keeps a paused player's audio session "active" for a long while, so that flag can't be trusted. Instead: sound heard in the
    // last few seconds, or the player busy decoding (a paused one barely uses the CPU).
    static readonly Dictionary<int, DateTime> LastLoud = [];
    static readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> LastCpu = [];

    static bool GuessPlaying(int pid)
    {
        var now = DateTime.UtcNow;
        var (_, peak) = AudioActivity.Probe(pid);
        if (peak > 0.0005f) LastLoud[pid] = now;
        var loud = LastLoud.TryGetValue(pid, out var heard) && now - heard < TimeSpan.FromSeconds(4);

        var busy = false;
        try
        {
            using var p = Process.GetProcessById(pid);
            var cpu = p.TotalProcessorTime;
            if (LastCpu.TryGetValue(pid, out var before))
                busy = (cpu - before.Cpu).TotalMilliseconds / Math.Max(1, (now - before.At).TotalMilliseconds) * 100 >= 2.0;
            LastCpu[pid] = (cpu, now);
        }
        catch { /* process ended */ }

        return loud || busy;
    }
}