using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Modules.Agents;

namespace DynamicIsland.Modules.Ports;

/// <summary>One open port and the program behind it. Killing needs a second tap.</summary>
sealed class PortItem : INotifyPropertyChanged
{
    bool _armed;

    public required int Port { get; init; }
    public required int Pid { get; init; }
    public required string Name { get; init; }
    public required string Project { get; init; }
    public required string Command { get; init; }
    public required bool Exposed { get; init; }

    public string PortText => $":{Port}";
    public string Title => Project.Length > 0 ? $"{Name}  ·  {Project}" : Name;
    public string KillLabel => IsArmed ? "Kill?" : "✕";
    public DateTime ArmedAt { get; private set; }

    public bool IsArmed
    {
        get => _armed;
        set
        {
            if (_armed == value) return;
            _armed = value;
            if (value) ArmedAt = DateTime.UtcNow;
            PropertyChanged?.Invoke(this, new(nameof(IsArmed)));
            PropertyChanged?.Invoke(this, new(nameof(KillLabel)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Ports that something other than Windows itself has open: dev servers, databases, tools you started from a terminal or in the
/// background. Only programs this account is allowed to end are listed (Windows services and elevated programs can't be killed from
/// here, so they stay out), and well-known desktop apps that open internal ports are hidden. The button appears only while there is
/// at least one. Tap ✕ then tap again to end the program (and anything it started).
/// </summary>
sealed class PortsEntry : TabEntry
{
    // never interesting: Windows' own listeners
    static readonly HashSet<string> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "svchost", "lsass", "services", "wininit", "spoolsv", "smss", "csrss", "winlogon", "dwm", "fontdrvhost",
        "wmiprvse", "dashost", "audiodg", "searchindexer", "msmpeng", "nissrv", "sihost", "taskhostw", "ctfmon", "dns", "sqlwriter",
    };

    // desktop apps that keep internal ports open: hidden so the tab only shows what you started yourself
    static readonly HashSet<string> NoiseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "onedrive", "onedrive.sync.service", "chrome", "msedge", "msedgewebview2", "firefox", "brave", "opera", "vivaldi",
        "discord", "spotify", "slack", "teams", "ms-teams", "whatsapp", "telegram", "zoom", "steam", "steamwebhelper",
        "epicgameslauncher", "claude", "chatgpt", "codex", "code", "cursor", "windsurf", "dropbox", "googledrivefs",
        "dynamicisland", "searchhost", "yourphone", "phoneexperiencehost", "widgets", "rdpclip", "dllhost", "runtimebroker",
    };

    static readonly string[] NoisePrefixes = ["adobe", "nvidia", "razer", "logi", "lghub", "microsoft."];

    sealed record Details(string Name, string Project, string Command);

    readonly Dictionary<int, Details?> _known = []; // null = not interesting (cached so each program is only inspected once)
    readonly DispatcherTimer _disarm = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly DispatcherTimer _rescan = new() { Interval = TimeSpan.FromMilliseconds(600) };

    public override ViewKind Kind => ViewKind.Ports;
    public override string Name => "Ports";
    public override string Glyph => "\uE839"; // Segoe Fluent Icons: ethernet
    public override string GlyphFont => "Segoe Fluent Icons, Segoe MDL2 Assets";
    public override string Accent => "#FFB454";
    public override bool IsAvailable => Entries.Count > 0;

    public ObservableCollection<PortItem> Entries { get; } = [];
    public string Summary => Entries.Count == 1 ? "1 open  ·  tap ✕ twice to end it" : $"{Entries.Count} open  ·  tap ✕ twice to end one";
    public ICommand Kill { get; }

    public PortsEntry()
    {
        IsRunning = true;
        Kill = new ParamCommand(p => { if (p is PortItem item) Click(item); });
        _disarm.Tick += (_, _) => DisarmAll();
        _rescan.Tick += (_, _) => { _rescan.Stop(); Tick(false); };
        Entries.CollectionChanged += (_, _) =>
        {
            Badge = Entries.Count > 0 ? Entries.Count.ToString() : null;
            Raise(nameof(IsAvailable));
            Raise(nameof(Summary));
        };
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Visible) && !Visible) DisarmAll(); }; // closing the island cancels a pending kill
    }

    public override void Tick(bool expanded)
    {
        var wanted = new List<(TcpListeners.Listener L, Details D)>();
        var seenPids = new HashSet<int>();
        foreach (var l in TcpListeners.Get())
        {
            seenPids.Add(l.Pid);
            if (Describe(l.Pid) is { } d) wanted.Add((l, d));
        }
        foreach (var pid in _known.Keys.Where(k => !seenPids.Contains(k)).ToList()) _known.Remove(pid); // forget programs that closed

        // update the list in place, so rows (and a half-confirmed kill) don't flicker away on every refresh
        foreach (var gone in Entries.Where(e => !wanted.Any(w => w.L.Port == e.Port && w.L.Pid == e.Pid)).ToList()) Entries.Remove(gone);
        foreach (var (l, d) in wanted)
        {
            if (Entries.Any(e => e.Port == l.Port && e.Pid == l.Pid)) continue;
            var item = new PortItem { Port = l.Port, Pid = l.Pid, Name = d.Name, Project = d.Project, Command = d.Command, Exposed = l.Exposed };
            var at = Entries.TakeWhile(e => e.Port < l.Port).Count();
            Entries.Insert(at, item);
        }
    }

    Details? Describe(int pid)
    {
        if (_known.TryGetValue(pid, out var cached)) return cached;
        Details? result = null;
        try
        {
            using var proc = Process.GetProcessById(pid);
            var name = proc.ProcessName;
            var hidden = SystemNames.Contains(name) || NoiseNames.Contains(name) || NoisePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (!hidden && ProcessInfo.CanTerminate(pid))
                result = new Details(name, ProjectName(ProcessInfo.WorkingDirectory(pid)), ShortCommand(ProcessInfo.CommandLine(pid)));
        }
        catch { /* the process ended while we looked */ }
        _known[pid] = result;
        return result;
    }

    /// <summary>The last folder of the working directory ("rabbitt-backend"); empty for a drive root or a system folder, which say nothing.</summary>
    static string ProjectName(string dir)
    {
        if (dir.Length == 0) return "";
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (dir.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return "";
        var name = Path.GetFileName(dir);
        return name.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase) ? "" : name;
    }

    /// <summary>"node C:\proj\node_modules\vite\bin\vite.js --port 2000" -> "node vite.js --port 2000".</summary>
    static string ShortCommand(string commandLine)
    {
        if (commandLine.Length == 0) return "";
        var parts = Regex.Matches(commandLine, "\"[^\"]*\"|\\S+").Select(m => m.Value.Trim('"'))
            .Select(t => t.Contains('\\') || t.Contains('/') ? Path.GetFileName(t.TrimEnd('\\', '/')) : t);
        var text = string.Join(' ', parts);
        return text.Length <= 80 ? text : text[..79] + "…";
    }

    // ---- kill (two taps) ---------------------------------------------------------------------------------------------------

    void Click(PortItem item)
    {
        if (item.IsArmed)
        {
            if ((DateTime.UtcNow - item.ArmedAt).TotalMilliseconds < 400) return; // a double-click must not confirm
            DisarmAll();
            End(item.Pid);
            return;
        }
        DisarmAll();
        item.IsArmed = true;
        _disarm.Start();
    }

    void DisarmAll()
    {
        _disarm.Stop();
        foreach (var e in Entries) e.IsArmed = false;
    }

    void End(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.Kill(entireProcessTree: true);
        }
        catch { /* already gone, or access was denied */ }
        _rescan.Start(); // refresh the list as soon as it has had a moment to close
    }
}
