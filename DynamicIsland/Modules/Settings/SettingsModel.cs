using System.ComponentModel;
using System.Windows.Input;
using DynamicIsland.Core;
using DynamicIsland.Modules.Agents;

namespace DynamicIsland.Modules.Settings;

/// <summary>One thing in the settings window that is on or off, or chosen: a footer button, a glass look, auto-hide.</summary>
sealed class SettingItem(string name, string description, Func<bool> isOn, Action flip, TabEntry? tab = null, double preview = 0) : INotifyPropertyChanged
{
    public string Name => name;
    public string Description => description;
    public TabEntry? Tab => tab;
    /// <summary>How dark the little sample of this glass look is (glass choices only).</summary>
    public double Preview => preview;
    public bool IsOn => isOn();
    public ICommand Flip { get; } = new RelayCommand(flip);
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(IsOn)));
}

/// <summary>
/// Everything the settings window shows. Settings are saved the moment they change (<see cref="SettingsStore"/>) and the island follows
/// them live, so there is no Save button. New settings are added here as a <see cref="SettingItem"/> (or a property) and one row in the window.
/// </summary>
sealed class SettingsModel : INotifyPropertyChanged
{
    static readonly Dictionary<string, string> TabNotes = new()
    {
        ["Claude"] = "What Claude Code and the Claude app are doing, and their questions",
        ["Copilot"] = "GitHub Copilot Chat in VS Code, while a chat is running",
        ["Antigravity"] = "Google Antigravity, while its agent is working",
        ["ChatGPT"] = "The ChatGPT app, while it is working on something",
        ["Screenshots"] = "Your newest screenshots, ready to drag into any app",
        ["Clipboard"] = "Recent text copies, click one to paste it",
        ["Ports"] = "Open development ports, with a way to end the program",
        ["Power"] = "Lock, sleep, sign out, restart and shut down",
    };

    string _message = "";
    bool _capturing;

    public SettingsStore Store => SettingsStore.Current;
    public IReadOnlyList<SettingItem> GlassChoices { get; }
    public IReadOnlyList<SettingItem> TabItems { get; }
    public SettingItem AutoHide { get; }
    public string Version { get; } = typeof(SettingsModel).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    public string HotkeyText => Store.Hotkey.Replace("+", "  +  ");
    public bool IsCapturing { get => _capturing; private set { _capturing = value; Raise(nameof(IsCapturing)); } }
    public string Message { get => _message; private set { _message = value; Raise(nameof(Message)); } }
    public ICommand ChangeHotkey { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsModel(IEnumerable<TabEntry> tabs)
    {
        GlassChoices =
        [
            Glass("Liquid", "Almost see-through: only the edges bend the picture behind", 0, 0.06),
            Glass("Clear", "Light glass with a soft blur", 1, 0.22),
            Glass("Frosted", "Frosted glass, the everyday look", 2, 0.45),
            Glass("Dark", "Dark smoked glass, the easiest to read", 3, 0.82),
        ];
        TabItems = [.. tabs.Select(t => new SettingItem(t.Name, TabNotes.GetValueOrDefault(t.Name, ""), () => !Store.IsHidden(t.Name), () => Store.SetHidden(t.Name, !Store.IsHidden(t.Name)), t))];
        AutoHide = new SettingItem("Hide in fullscreen", "Step out of the way while a fullscreen game or video is in front", () => Store.HideInFullscreen, () => Store.HideInFullscreen = !Store.HideInFullscreen);
        ChangeHotkey = new RelayCommand(() => { Message = ""; IsCapturing = true; });

        Store.PropertyChanged += (_, _) =>
        {
            foreach (var item in GlassChoices.Concat(TabItems).Append(AutoHide)) item.Refresh();
            Raise(nameof(HotkeyText));
        };
    }

    SettingItem Glass(string name, string description, int index, double preview) => new(name, description, () => Store.Glass == index, () => Store.Glass = index, null, preview);

    /// <summary>A key was pressed while waiting for the new shortcut. Escape cancels; it needs Ctrl, Alt, Shift or Win and must be free.</summary>
    public void Capture(Key key, ModifierKeys modifiers, IntPtr window)
    {
        if (key == Key.Escape) { IsCapturing = false; Message = ""; return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None) return;
        if (modifiers == ModifierKeys.None) { Message = "Start with Ctrl, Alt, Shift or the Windows key."; return; }

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        var combo = string.Join('+', parts);

        if (combo != Store.Hotkey && !Hotkey.CanRegister(window, combo)) { Message = $"{combo.Replace("+", " + ")} is already used by another program."; return; }
        Store.Hotkey = combo;
        IsCapturing = false;
        Message = "Saved.";
    }

    void Raise(string name) => PropertyChanged?.Invoke(this, new(name));
}
