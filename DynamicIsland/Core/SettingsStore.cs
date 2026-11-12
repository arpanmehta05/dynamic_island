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
    sealed record Data(double Tint = 1.0, string Hotkey = "Ctrl+Alt+I", string[]? HiddenTabs = null);

    public static readonly string[] HotkeyChoices = ["Ctrl+Alt+I", "Ctrl+Shift+Space", "Win+Alt+I", "Ctrl+Alt+Space"];
    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static SettingsStore Current { get; } = new();

    readonly HashSet<string> _hidden;
    double _tint;
    string _hotkey;

    SettingsStore()
    {
        Data data;
        try { data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data(); }
        catch { data = new Data(); }
        _tint = Math.Clamp(data.Tint, 0.3, 1.0);
        _hotkey = data.Hotkey;
        _hidden = [.. data.HiddenTabs ?? []];
    }

    /// <summary>How dark the glass is: 1 = the original smoked glass, lower = clearer.</summary>
    public double Tint { get => _tint; set { value = Math.Round(Math.Clamp(value, 0.3, 1.0), 1); if (value == _tint) return; _tint = value; Changed(nameof(Tint), nameof(TintText)); } }
    public string TintText => $"{Math.Round(_tint * 100)}%";

    public string Hotkey { get => _hotkey; set { if (value == _hotkey) return; _hotkey = value; Changed(nameof(Hotkey)); HotkeyChanged?.Invoke(); } }

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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Data(_tint, _hotkey, [.. _hidden]), Json));
        }
        catch { /* settings are a convenience: never fail the island over them */ }
    }
}
