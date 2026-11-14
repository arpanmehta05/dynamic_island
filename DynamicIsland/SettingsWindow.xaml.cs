using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DynamicIsland.Core;
using DynamicIsland.Modules.Settings;

namespace DynamicIsland;

/// <summary>The settings app: a normal window with room for every setting, opened from the tray icon or the Start menu. All logic is in <see cref="SettingsModel"/>.</summary>
public partial class SettingsWindow : Window
{
    readonly SettingsModel _model;

    internal SettingsWindow(SettingsModel model)
    {
        InitializeComponent();
        DataContext = _model = model;
        SourceInitialized += (_, _) => WindowStyler.DarkTitleBar(this);
    }

    // While "Press the new shortcut" is showing, keys go to the model (a normal window can take typing, unlike the island)
    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_model.IsCapturing) return;
        e.Handled = true;
        _model.Capture(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers, new WindowInteropHelper(this).Handle);
    }
}
