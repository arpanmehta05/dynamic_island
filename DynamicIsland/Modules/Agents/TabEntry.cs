using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace DynamicIsland.Modules.Agents;

/// <summary>What fills the expanded island when a footer button is selected.</summary>
public enum ViewKind { Player, Agent, Screenshots, Clipboard, Ports, Power, Settings, Approval }

/// <summary>One footer button: the player, an assistant, screenshots, the clipboard, open ports or the power controls. Selecting it swaps the island's expanded content.</summary>
abstract class TabEntry : INotifyPropertyChanged
{
    bool _selected, _visible, _working, _running, _hidden;
    string? _badge;
    ImageSource? _icon;

    public virtual ViewKind Kind => ViewKind.Agent;

    /// <summary>A small number on the button (for example how many ports are open).</summary>
    public string? Badge { get => _badge; protected set { if (Set(ref _badge, value)) Raise(nameof(HasBadge)); } }
    public bool HasBadge => !string.IsNullOrEmpty(_badge);

    /// <summary>Called on the hub's refresh (every second while expanded, every few seconds when collapsed) for entries that need to look at the system.</summary>
    public virtual void Tick(bool expanded) { }

    /// <summary>False hides the button (an assistant that isn't open and isn't busy shouldn't clutter the footer).</summary>
    public virtual bool IsAvailable => true;

    /// <summary>Turned off in Settings: the button never shows.</summary>
    public bool Hidden { get => _hidden; set { if (Set(ref _hidden, value)) Raise(nameof(Shown)); } }
    /// <summary>What the footer actually uses: available, and not switched off by the user.</summary>
    public bool Shown => IsAvailable && !_hidden;

    public abstract string Name { get; }
    public abstract string Glyph { get; }           // shown on the button when the app has no icon of its own
    public virtual string GlyphFont => "Segoe UI Symbol";
    public abstract string Accent { get; }          // hex colour for the status dot / logo tile
    public int Index { get; set; }

    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
    /// <summary>True while the island is expanded: animations only need to run when someone can see them.</summary>
    public bool Visible { get => _visible; set { if (Set(ref _visible, value)) Raise(nameof(Live)); } }
    /// <summary>The app is open (dimmed button when not).</summary>
    public bool IsRunning { get => _running; protected set { if (Set(ref _running, value)) OnStateChanged(); } }
    /// <summary>Playing / answering right now (pulsing dot on the button).</summary>
    public bool IsWorking { get => _working; protected set { if (Set(ref _working, value)) { Raise(nameof(Live)); OnStateChanged(); } } }
    public bool Live => IsWorking && Visible;

    public ImageSource? Icon
    {
        get => _icon;
        protected set { if (Set(ref _icon, value)) { Raise(nameof(HasIcon)); Raise(nameof(NoIcon)); Raise(nameof(ShowGlyph)); } }
    }
    public bool HasIcon => _icon is not null;
    public bool NoIcon => _icon is null;
    /// <summary>The assistant's own mark as an outline, used instead of the window icon when that icon isn't the assistant's (see <see cref="Core.Logos"/>).</summary>
    public virtual Geometry? Logo => null;
    public bool ShowGlyph => _icon is null && Logo is null;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnStateChanged() { }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise(string? name)
    {
        PropertyChanged?.Invoke(this, new(name));
        if (name == nameof(IsAvailable)) PropertyChanged?.Invoke(this, new(nameof(Shown)));
    }
}
