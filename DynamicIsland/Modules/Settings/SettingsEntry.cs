using System.ComponentModel;
using System.Windows.Input;
using DynamicIsland.Core;
using DynamicIsland.Modules.Agents;

namespace DynamicIsland.Modules.Settings;

/// <summary>One chip in the settings view: something that is on or off (a footer button, auto-hide).</summary>
sealed class TabToggle(string name, Func<bool> isOn, Action flip) : INotifyPropertyChanged
{
    public string Name => name;
    public bool IsOn => isOn();
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Flip() { flip(); PropertyChanged?.Invoke(this, new(nameof(IsOn))); }
}

/// <summary>Which footer buttons show, how dark the glass is and which key opens the island. Everything is saved at once (see <see cref="SettingsStore"/>).</summary>
sealed class SettingsEntry : TabEntry
{
    public override ViewKind Kind => ViewKind.Settings;
    public override string Name => "Settings";
    public override string Glyph => "";
    public override string GlyphFont => "Segoe Fluent Icons, Segoe MDL2 Assets";
    public override string Accent => "#9AA0A6";

    public SettingsStore Store => SettingsStore.Current;
    public IReadOnlyList<TabToggle> Toggles { get; }
    public ICommand Toggle { get; }
    public ICommand SetGlass { get; }
    public ICommand NextHotkey { get; }

    public SettingsEntry(IEnumerable<TabEntry> switchable)
    {
        IsRunning = true;
        var tabs = switchable.ToList();
        foreach (var t in tabs) t.Hidden = Store.IsHidden(t.Name);
        Toggles =
        [
            .. tabs.Select(t => new TabToggle(t.Name, () => !t.Hidden, () => { t.Hidden = !t.Hidden; Store.SetHidden(t.Name, t.Hidden); })),
            new TabToggle("Auto-hide", () => Store.HideInFullscreen, () => Store.HideInFullscreen = !Store.HideInFullscreen),
        ];

        Toggle = new ParamCommand(p => (p as TabToggle)?.Flip());
        SetGlass = new ParamCommand(p => Store.Glass = Convert.ToInt32(p));
        NextHotkey = new RelayCommand(() =>
        {
            var choices = SettingsStore.HotkeyChoices;
            Store.Hotkey = choices[(Array.IndexOf(choices, Store.Hotkey) + 1) % choices.Length];
        });
    }
}
