using System.ComponentModel;
using System.Windows.Input;
using DynamicIsland.Core;
using DynamicIsland.Modules.Agents;

namespace DynamicIsland.Modules.Settings;

/// <summary>One chip in the settings view: a footer button that can be switched off.</summary>
sealed class TabToggle(TabEntry entry) : INotifyPropertyChanged
{
    public TabEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public bool IsOn => !Entry.Hidden;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(IsOn)));
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
    public ICommand TintDown { get; }
    public ICommand TintUp { get; }
    public ICommand NextHotkey { get; }

    public SettingsEntry(IEnumerable<TabEntry> switchable)
    {
        IsRunning = true;
        Toggles = [.. switchable.Select(t => new TabToggle(t))];
        foreach (var t in Toggles) t.Entry.Hidden = Store.IsHidden(t.Name);

        Toggle = new ParamCommand(p =>
        {
            if (p is not TabToggle t) return;
            t.Entry.Hidden = !t.Entry.Hidden;
            Store.SetHidden(t.Name, t.Entry.Hidden);
            t.Refresh();
        });
        TintDown = new RelayCommand(() => Store.Tint -= 0.1);
        TintUp = new RelayCommand(() => Store.Tint += 0.1);
        NextHotkey = new RelayCommand(() =>
        {
            var choices = SettingsStore.HotkeyChoices;
            Store.Hotkey = choices[(Array.IndexOf(choices, Store.Hotkey) + 1) % choices.Length];
        });
    }
}
