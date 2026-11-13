using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace DynamicIsland.Core;

/// <summary>
/// The user's settings: one small JSON file at %AppData%\DynamicIsland\settings.json, loaded once and saved on every change.
/// Missing or damaged file = defaults.
/// </summary>
sealed class SettingsStore : INotifyPropertyChanged
{
    sealed record Data(double Tint = 0.6, string Hotkey = "Ctrl+Alt+I", string[]? HiddenTabs = null, bool HideInFullscreen = false);

    public static readonly string[] HotkeyChoices = ["Ctrl+Alt+I", "Ctrl+Shift+Space", "Win+Alt+I", "Ctrl+Alt+Space"];
    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static SettingsStore Current { get; } = new();

    readonly HashSet<string> _hidden;
    double _tint;
    string _hotkey;
    bool _hideInFullscreen;

    SettingsStore()
    {
        Data data;
        try { data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data(); }
        catch { data = new Data(); }
        _tint = Math.Clamp(data.Tint, 0, 1);
        _hotkey = data.Hotkey;
        _hidden = [.. data.HiddenTabs ?? []];
        _hideInFullscreen = data.HideInFullscreen;
    }

    static readonly double[] GlassTints = [0, 0.3, 0.6, 1.0];

    /// <summary>How dark the glass is (the shader's tint): 0 = liquid (almost nothing but the edges), 0.3 = clear, 0.6 = frosted, 1 = dark smoked glass.</summary>
    public double Tint => _tint;
    /// <summary>The same as a choice: 0 liquid, 1 clear, 2 frosted, 3 dark.</summary>
    public int Glass
    {
        get => _tint < 0.15 ? 0 : _tint < 0.45 ? 1 : _tint < 0.8 ? 2 : 3;
        set { if (value == Glass) return; _tint = GlassTints[Math.Clamp(value, 0, 3)]; Changed(nameof(Tint), nameof(Glass)); }
    }

    public string Hotkey { get => _hotkey; set { if (value == _hotkey) return; _hotkey = value; Changed(nameof(Hotkey)); HotkeyChanged?.Invoke(); } }

    /// <summary>Off by default: the island stays on screen whatever you are doing. On: it steps away while a fullscreen game or video is in front.</summary>
    public bool HideInFullscreen { get => _hideInFullscreen; set { if (value == _hideInFullscreen) return; _hideInFullscreen = value; Changed(nameof(HideInFullscreen)); } }

    public bool IsHidden(string tab) => _hidden.Contains(tab);
    public void SetHidden(string tab, bool hidden) { if (hidden ? _hidden.Add(tab) : _hidden.Remove(tab)) Changed(); }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? HotkeyChanged;

    void Changed(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new(n));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Data(_tint, _hotkey, [.. _hidden], _hideInFullscreen), Json));
        }
        catch { /* settings are a convenience: never fail the island over them */ }
    }
}
