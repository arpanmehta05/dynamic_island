using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Modules.Approvals;
using DynamicIsland.Modules.Clip;
using DynamicIsland.Modules.Ports;
using DynamicIsland.Modules.Power;
using DynamicIsland.Modules.Settings;
using DynamicIsland.Modules.Shots;

namespace DynamicIsland.Modules.Agents;

/// <summary>
/// The footer buttons (Player, Claude, Copilot, ChatGPT, Screenshots, Clipboard, Ports, Power), which one is selected, and the refresh
/// of everything that watches the system: every second while the island is expanded, every few seconds while collapsed (so the notch
/// can show that an assistant is working). Buttons that have nothing to show (an assistant that isn't open, no recent screenshot,
/// no extra open port) are hidden.
/// </summary>
class AgentsHub : INotifyPropertyChanged
{
    static readonly TimeSpan ExpandedEvery = TimeSpan.FromSeconds(1), CollapsedEvery = TimeSpan.FromSeconds(4), ProcessListEvery = TimeSpan.FromSeconds(30);

    readonly MediaModule _media;
    readonly DispatcherTimer _timer = new() { Interval = CollapsedEvery };
    int _index;
    bool _expanded;
    Dictionary<string, List<int>> _processes = [];
    DateTime _listed = DateTime.MinValue;

    public IReadOnlyList<TabEntry> Items { get; }
    public ClipboardMonitor ClipboardMonitor { get; } = new();
    public ScreenshotsEntry Screenshots { get; }
    public ClipboardEntry Clipboard { get; }
    public PortsEntry Ports { get; }
    public PowerEntry Power { get; }
    public SettingsEntry Settings { get; }
    public ApprovalEntry Approvals { get; }
    /// <summary>True while Claude Code is waiting for an answer: the island opens and stays open.</summary>
    public bool NeedsAttention => Approvals.IsAvailable;
    public event Action<bool>? AttentionChanged;
    public ICommand Select { get; }

    TabEntry Current => Items[_index];
    public bool ShowPlayer => Current.Kind == ViewKind.Player;
    public bool ShowAgent => Current.Kind == ViewKind.Agent;
    public bool ShowScreenshots => Current.Kind == ViewKind.Screenshots;
    public bool ShowClipboard => Current.Kind == ViewKind.Clipboard;
    public bool ShowPorts => Current.Kind == ViewKind.Ports;
    public bool ShowPower => Current.Kind == ViewKind.Power;
    public bool ShowSettings => Current.Kind == ViewKind.Settings;
    public bool ShowApproval => Current.Kind == ViewKind.Approval;
    public AgentInfo? SelectedAgent => Current as AgentInfo;
    public bool Expanded => _expanded;
    /// <summary>The pet only animates while it can be seen: island expanded and an assistant that has a pet selected.</summary>
    public bool PetActive => _expanded && SelectedAgent is { HasPet: true } or { HasSprite: true };

    // Collapsed notch: with nothing playing, the first assistant that is working shows up beside the time.
    public AgentInfo? CompactAgent { get; private set; }
    /// <summary>Development: DI_DEMO_IDLE=1 pretends nothing is playing and no assistant is working (to look at the idle pill).</summary>
    static readonly bool DemoIdle = Environment.GetEnvironmentVariable("DI_DEMO_IDLE") is not null;
    public bool ShowCompactAgent => !DemoIdle && !_media.IsPlaying && CompactAgent is not null;
    public bool ShowCover => !DemoIdle && _media.HasMedia && !ShowCompactAgent;
    public bool ShowEqualizer => !DemoIdle && _media.IsPlaying;
    /// <summary>Nothing playing and no assistant working: the island's own avatar sits beside the time.</summary>
    public bool ShowAvatar => !ShowCover && !ShowCompactAgent;

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Raised when the user picks a different button (the window plays a small switch animation).</summary>
    public event Action<int>? TabChanged;

    public AgentsHub(MediaModule media)
    {
        _media = media;
        Screenshots = new ScreenshotsEntry(ClipboardMonitor);
        Clipboard = new ClipboardEntry(ClipboardMonitor);
        Ports = new PortsEntry();
        Power = new PowerEntry();
        TabEntry[] switchable = [new ClaudeAgent(), new CopilotAgent(), new ChatGptAgent(), Screenshots, Clipboard, Ports, Power];
        Settings = new SettingsEntry(switchable);
        Approvals = new ApprovalEntry();
        Items = [new PlayerEntry(media), Approvals, .. switchable, Settings];
        Approvals.Attention += attention =>
        {
            if (attention) SelectIndex(Approvals.Index);
            else if (Current == Approvals) SelectIndex(0);
            AttentionChanged?.Invoke(attention);
        };
        for (var i = 0; i < Items.Count; i++) Items[i].Index = i;
        Items[0].IsSelected = true;

        Select = new ParamCommand(p => SelectIndex(Convert.ToInt32(p)));

        foreach (var a in Items.OfType<AgentInfo>())
            a.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TabEntry.IsWorking)) UpdateCompact(); };
        media.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MediaModule.IsPlaying) or nameof(MediaModule.HasMedia)) Raise(nameof(ShowCompactAgent), nameof(ShowCover), nameof(ShowAvatar), nameof(ShowEqualizer));
        };

        _timer.Tick += (_, _) => RefreshAll();
        _timer.Start();
        RefreshAll();
    }

    void SelectIndex(int i)
    {
        if (i == _index) return;
        Current.IsSelected = false;
        var previous = _index;
        _index = i;
        Current.IsSelected = true;
        if (Current == Screenshots) Screenshots.MarkSeen();
        Raise(nameof(ShowPlayer), nameof(ShowAgent), nameof(ShowScreenshots), nameof(ShowClipboard), nameof(ShowPorts), nameof(ShowPower), nameof(ShowSettings), nameof(ShowApproval), nameof(SelectedAgent), nameof(PetActive));
        if (_expanded) RefreshAll();
        TabChanged?.Invoke(Math.Sign(i - previous));
    }

    /// <summary>A swipe: moves to the next (+1) or previous (-1) footer button. Not while Claude is waiting for an answer.</summary>
    public void Step(int direction)
    {
        if (NeedsAttention) return;
        var shown = Items.Where(t => t.Shown).Select(t => t.Index).ToList();
        if (shown.Count == 0) return;
        var at = Math.Max(0, shown.IndexOf(_index));
        SelectIndex(shown[Math.Clamp(at + direction, 0, shown.Count - 1)]);
    }

    /// <summary>The island was closed: next time it opens on its first button, not wherever you left it.</summary>
    public void ResetTab()
    {
        if (NeedsAttention) return;
        if (Items.FirstOrDefault(t => t.Shown) is { } first) SelectIndex(first.Index);
    }

    public bool IsExpanded
    {
        set
        {
            if (_expanded == value) return;
            _expanded = value;
            foreach (var a in Items) a.Visible = value;
            _timer.Interval = value ? ExpandedEvery : CollapsedEvery;
            if (value)
            {
                RefreshAll();
                // a screenshot was just taken and nobody has looked at it: open straight onto it (unless you were already on another view)
                if (Screenshots.IsFresh && Current.Kind == ViewKind.Player) SelectIndex(Screenshots.Index);
            }
            Raise(nameof(Expanded), nameof(PetActive));
        }
    }

    void RefreshAll()
    {
        // Listing every process is the costly part of a refresh: while collapsed, reuse the last list for up to 30 seconds.
        if (_expanded || DateTime.UtcNow - _listed > ProcessListEvery) { _processes = AgentInfo.ProcessSnapshot(); _listed = DateTime.UtcNow; }
        var windows = DesktopWindows.PidsWithWindow();
        foreach (var a in Items.OfType<AgentInfo>()) a.Refresh(_processes, windows, _expanded);
        foreach (var t in Items) t.Tick(_expanded);
        UpdateCompact();

        // the selected view's button went away (assistant closed, screenshot expired, port freed): fall back to the player
        if (!Current.Shown) SelectIndex(0);
    }

    void UpdateCompact()
    {
        var working = Items.OfType<AgentInfo>().FirstOrDefault(a => a.IsWorking);
        if (ReferenceEquals(working, CompactAgent)) return;
        CompactAgent = working;
        Raise(nameof(CompactAgent), nameof(ShowCompactAgent), nameof(ShowCover), nameof(ShowAvatar));
    }

    void Raise(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new(n));
    }
}
