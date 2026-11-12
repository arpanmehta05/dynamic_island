using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Modules;
using DynamicIsland.Modules.Agents;
using DynamicIsland.Modules.Hud;
using DynamicIsland.Modules.Shots;

namespace DynamicIsland;

public partial class IslandWindow : Window
{
    // A layered window repaints its whole surface every frame, so the window is only as tall as it needs to be:
    // just the notch (plus room for the spring and the droplet) when collapsed, the full player + footer when expanded.
    const double CollapsedHeight = 70, ExpandedHeight = 170;

    readonly MediaModule _media = new();
    readonly AgentsHub _agents;
    readonly DispatcherTimer _shrink = new() { Interval = TimeSpan.FromMilliseconds(900) }; // waits for the closing animation
    readonly DispatcherTimer _peek = new() { Interval = TimeSpan.FromSeconds(6) };
    Hotkey? _hotkey;
    TrayIcon? _tray;
    bool _open;
    Backdrop? _backdrop;
    FullscreenWatcher? _fullscreen;

    public IslandWindow()
    {
        InitializeComponent();
        _agents = new AgentsHub(_media);
        DataContext = new { Clock = new ClockModule(), Media = _media, Agents = _agents, Hud = new HudModule() };
        _agents.TabChanged += () => ((Storyboard)Resources["TabSwitch"]).Begin(this);
        _shrink.Tick += (_, _) => { _shrink.Stop(); Height = CollapsedHeight; };

        SourceInitialized += (_, _) =>
        {
            // DI_CAPTURABLE is only for taking screenshots of the island during development.
            WindowStyler.Apply(this, excludeFromCapture: Environment.GetEnvironmentVariable("DI_CAPTURABLE") is null);
            _backdrop = new Backdrop(this, ExpandedHeight);
            BackdropImage.Source = _backdrop.Bitmap;
            _agents.ClipboardMonitor.Attach(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle));
        };

        MouseEnter += (_, _) => { _peek.Stop(); Expand(); };
        MouseLeave += (_, _) => { if (!_agents.NeedsAttention) Collapse(); };

        // Claude Code is asking something: open, and stay open until it is answered.
        _agents.AttentionChanged += attention =>
        {
            if (_fullscreen?.IsFullscreen == true) return;
            if (attention) { _peek.Stop(); Expand(); }
            else { WindowStyler.AllowTyping(this, false); if (!IsMouseOver) Collapse(); }
        };
        Deactivated += (_, _) => WindowStyler.AllowTyping(this, false);

        // Opened from the keyboard: stays for a few seconds, then closes by itself unless the mouse is on it.
        _peek.Tick += (_, _) => { _peek.Stop(); if (!IsMouseOver && !_agents.NeedsAttention) Collapse(); };
        SourceInitialized += (_, _) =>
        {
            _hotkey = new Hotkey(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle));
            _hotkey.Pressed += TogglePeek;
            _tray = new TrayIcon(TogglePeek, Application.Current.Shutdown);
            Application.Current.Exit += (_, _) => _tray.Dispose();
            _hotkey.Register(SettingsStore.Current.Hotkey);
            SettingsStore.Current.HotkeyChanged += () => _hotkey.Register(SettingsStore.Current.Hotkey);
        };

        Loaded += (_, _) =>
        {
            _fullscreen = new FullscreenWatcher(new WindowInteropHelper(this).Handle);
            _fullscreen.Changed += SetHidden;
            _fullscreen.Activity += () => _backdrop!.Poke();
            SetHidden(_fullscreen.IsFullscreen);
        };
    }

    void TogglePeek()
    {
        if (_fullscreen?.IsFullscreen == true) return;
        if (_open) { _peek.Stop(); Collapse(); } else { Expand(); _peek.Start(); }
    }

    void Expand()
    {
        _shrink.Stop();
        if (_open) return;
        _open = true;
        Height = ExpandedHeight;
        if (_backdrop is not null) _backdrop.Expanded = true;
        _media.Tracking = true;
        _agents.IsExpanded = true;
        ((Storyboard)Resources["Open"]).Begin(this);
    }

    void Collapse()
    {
        if (!_open) return;
        _open = false;
        _shrink.Start();
        if (_backdrop is not null) _backdrop.Expanded = false;
        _media.Tracking = false;
        _agents.IsExpanded = false;
        ((Storyboard)Resources["Close"]).Begin(this);
    }

    void SetHidden(bool hidden)
    {
        _backdrop!.Active = !hidden;
        ((Storyboard)Resources[hidden ? "Hide" : "Show"]).Begin(this);
    }

    void Type_MouseDown(object sender, MouseButtonEventArgs e)
    {
        WindowStyler.AllowTyping(this, true);
        Dispatcher.BeginInvoke(() => Keyboard.Focus((IInputElement)sender), DispatcherPriority.Input);
    }

    // "Write my own" just opened its text field: take the keyboard straight away so you can type without another click
    void Type_Shown(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        WindowStyler.AllowTyping(this, true);
        Dispatcher.BeginInvoke(() => Keyboard.Focus((IInputElement)sender), DispatcherPriority.Input);
    }

    void Track_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var track = (FrameworkElement)sender;
        _media.Seek(e.GetPosition(track).X / track.ActualWidth);
    }

    // ---- screenshots: drag a card out of the island and drop it anywhere (Claude, WhatsApp, Explorer, a browser...) ----

    Point _dragStart;
    ShotItem? _dragItem;

    void Shot_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var item = (sender as FrameworkElement)?.DataContext as ShotItem;
        if (item is null) return;
        if (e.ClickCount == 2) // double-click: open it in the default viewer
        {
            try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }); } catch { /* no viewer */ }
            return;
        }
        _dragStart = e.GetPosition(this);
        _dragItem = item;
    }

    void Shot_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null) return;
        var moved = e.GetPosition(this) - _dragStart;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var item = _dragItem;
        _dragItem = null;
        DragDrop.DoDragDrop((DependencyObject)sender, _agents.Screenshots.CreateDragData(item), DragDropEffects.Copy);
    }

    void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
