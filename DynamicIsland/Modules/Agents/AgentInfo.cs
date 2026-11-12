using System.Diagnostics;
using System.Windows.Input;
using DynamicIsland.Core;

namespace DynamicIsland.Modules.Agents;

public enum PetState { Sleeping, Idle, Working }

/// <summary>
/// One AI assistant. Subclasses read whatever the app leaves on disk and fill in
/// <see cref="TabEntry.IsWorking"/>, <see cref="Headline"/>, <see cref="Body"/> and <see cref="Meta"/>.
/// Its footer button only shows while its window is open on the desktop or it is busy: an app that just sits in the tray stays out of the way.
/// </summary>
abstract class AgentInfo : TabEntry
{
    string _headline = "", _body = "", _meta = "";
    bool _hasWindow;

    /// <summary>Click on the assistant's view: bring its window to the front (or start it again if it is only in the tray).</summary>
    public ICommand Open { get; }

    protected AgentInfo() => Open = new RelayCommand(OpenApp);

    void OpenApp()
    {
        if (!SourceApp.Activate(ProcessNames[0], "")) SourceApp.Launch(ProcessNames[0]);
    }

    protected abstract string[] ProcessNames { get; } // the app's process name(s), lower-case, to know if it is open
    public virtual bool HasPet => false;
    /// <summary>A pet drawn from the app's own artwork (ChatGPT/Codex), as opposed to the pixel pet.</summary>
    public virtual SpriteSet? Sprites => null;
    public bool HasSprite => Sprites is not null;
    public bool NoPet => !HasPet && !HasSprite;
    /// <summary>Ids of this app's running processes (from the last refresh).</summary>
    protected IReadOnlyList<int> Pids { get; private set; } = [];
    public PetState Pet => !IsRunning ? PetState.Sleeping : IsWorking ? PetState.Working : PetState.Idle;

    /// <summary>The app has a visible window on the desktop (not just a tray icon).</summary>
    public bool HasWindow { get => _hasWindow; private set { if (Set(ref _hasWindow, value)) Raise(nameof(IsAvailable)); } }
    public override bool IsAvailable => ShowAll || HasWindow || IsWorking;

    /// <summary>Development only: DI_SHOW_ALL=1 shows every assistant's button even when its app isn't open (to look at all the views).</summary>
    static readonly bool ShowAll = Environment.GetEnvironmentVariable("DI_SHOW_ALL") is not null;

    public string Headline { get => _headline; protected set => Set(ref _headline, value); }
    public string Body { get => _body; protected set => Set(ref _body, value); }
    public string Meta { get => _meta; protected set => Set(ref _meta, value); }

    protected override void OnStateChanged()
    {
        Raise(nameof(Pet));
        Raise(nameof(IsAvailable));
    }

    /// <summary>
    /// Re-reads the app's state. <paramref name="processes"/> (process name -> ids) and <paramref name="windowPids"/> are shared snapshots,
    /// because listing processes and windows is not free. <paramref name="detailed"/> is true while the island is expanded: only then is the
    /// prompt, model and so on worth extracting; when collapsed, only "is it working?" is needed.
    /// </summary>
    public void Refresh(IReadOnlyDictionary<string, List<int>> processes, ISet<int> windowPids, bool detailed)
    {
        var pids = ProcessNames.Where(processes.ContainsKey).SelectMany(n => processes[n]).ToList();
        Pids = pids;
        IsRunning = pids.Count > 0;
        HasWindow = pids.Any(windowPids.Contains);
        if (Icon is null && IsRunning) Icon = SourceApp.Icon(ProcessNames[0]);
        Update(detailed);
    }

    protected abstract void Update(bool detailed);

    /// <summary>Lower-cased process name -> process ids, from a single listing.</summary>
    public static Dictionary<string, List<int>> ProcessSnapshot()
    {
        var all = Process.GetProcesses();
        var map = new Dictionary<string, List<int>>(all.Length);
        foreach (var p in all)
        {
            var name = p.ProcessName.ToLowerInvariant();
            if (!map.TryGetValue(name, out var list)) map[name] = list = [];
            list.Add(p.Id);
            p.Dispose();
        }
        return map;
    }

    protected static string Ago(TimeSpan t) =>
        t.TotalSeconds < 45 ? "just now" :
        t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes}m ago" :
        t.TotalHours < 24 ? $"{(int)t.TotalHours}h ago" : $"{(int)t.TotalDays}d ago";

    protected static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";

    protected static string Join(params string?[] parts) => string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
