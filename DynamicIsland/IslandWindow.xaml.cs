using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
    // just the pill (plus its shadow) when collapsed; when open, the content's own height (a question from Claude can be tall).
    const double CollapsedHeight = 70, TallestHeight = 456, Gap = 6, Side = 20, ShadowRoom = 30;
    static readonly Rect Pill = new(130, Gap, 180, 32);
    static readonly SpringEase OpenSpring = new() { Damping = 0.72, Frequency = 13 }, CloseSpring = new() { Damping = 0.88, Frequency = 12 };

    readonly MediaModule _media = new();
    readonly AgentsHub _agents;
    readonly HudModule _hud = new();
    readonly DispatcherTimer _shrink = new() { Interval = TimeSpan.FromMilliseconds(900) }; // waits for the closing animation
    readonly DispatcherTimer _peek = new() { Interval = TimeSpan.FromSeconds(6) };
    readonly DispatcherTimer _reset = new() { Interval = TimeSpan.FromMilliseconds(220) }; // once the content has faded out
    Hotkey? _hotkey;
    TrayIcon? _tray;
    bool _open;
    double _restHeight = CollapsedHeight;
    Backdrop? _backdrop;
    FullscreenWatcher? _fullscreen;
    bool _hidden, _snipping;
    SnipWatcher? _snip;

    public IslandWindow()
    {
        InitializeComponent();
        _agents = new AgentsHub(_media);
        DataContext = new { Clock = new ClockModule(), Media = _media, Agents = _agents, Hud = _hud };
        _agents.TabChanged += direction => Motion.SlideIn(MainBody, direction);
        _shrink.Tick += (_, _) => { _shrink.Stop(); Height = _restHeight; };
        _reset.Tick += (_, _) => { _reset.Stop(); if (!_open) _agents.ResetTab(); };
        Detail.SizeChanged += (_, _) => Fit(); // another view, or a longer question: the open island follows its content

        SourceInitialized += (_, _) =>
        {
            // DI_CAPTURABLE is only for taking screenshots of the island during development.
            WindowStyler.Apply(this, excludeFromCapture: Environment.GetEnvironmentVariable("DI_CAPTURABLE") is null);
            _backdrop = new Backdrop(this, TallestHeight, SettingsStore.Current.Glass > 0, frozen: Environment.GetEnvironmentVariable("DI_CAPTURABLE") is not null);
            SettingsStore.Current.PropertyChanged += (_, _) => { _backdrop.Frost = SettingsStore.Current.Glass > 0; _backdrop.Poke(); };
            BackdropImage.Source = _backdrop.Bitmap;
            _agents.ClipboardMonitor.Attach(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle));
            var swipe = new SwipeGesture(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle));
            swipe.Swiped += direction => { if (_open) _agents.Step(direction); };
        };

        MouseEnter += (_, _) => { _peek.Stop(); Expand(); };
        MouseLeave += (_, _) => { if (!_agents.NeedsAttention) Collapse(); };

        // Claude Code is asking something: open, and stay open until it is answered.
        _agents.AttentionChanged += attention =>
        {
            if (_hidden) return;
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
            _fullscreen.Changed += _ => ApplyAutoHide();
            _snip = new SnipWatcher(); // Print Screen / Win+Shift+S: step aside from the capture toolbar, whatever the Auto-hide setting
            _snip.Changed += open => { _snipping = open; ApplyAutoHide(); };
            _fullscreen.Activity += () => _backdrop!.Poke();
            _fullscreen.Settled += () => WindowStyler.KeepOnTop(this);
            SettingsStore.Current.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SettingsStore.HideInFullscreen)) ApplyAutoHide(); };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(() => WindowStyler.KeepOnTop(this));
            Microsoft.Win32.SystemEvents.SessionSwitch += (_, _) => Dispatcher.BeginInvoke(() => WindowStyler.KeepOnTop(this));
            Microsoft.Win32.SystemEvents.PowerModeChanged += (_, _) => Dispatcher.BeginInvoke(() => WindowStyler.KeepOnTop(this));
            SetHidden(_fullscreen.IsFullscreen && SettingsStore.Current.HideInFullscreen);
        };
    }

    void TogglePeek()
    {
        if (_hidden) SetHidden(false); // asked for it by hand: bring it back even from a fullscreen hide
        if (_open) { _peek.Stop(); Collapse(); } else { Expand(); _peek.Start(); }
    }

    void Expand()
    {
        _shrink.Stop();
        _reset.Stop();
        if (_open) return;
        _open = true;
        _media.Tracking = true;
        _agents.IsExpanded = true;
        Fit();
        ((Storyboard)Resources["Open"]).Begin(this);
        Motion.BlurIn(Detail);
    }

    /// <summary>Sizes the open island (window, backdrop and glass) to its content.</summary>
    void Fit()
    {
        if (!_open) return;
        Detail.UpdateLayout();
        var height = Gap + Detail.ActualHeight + ShadowRoom;
        Resize(height);
        _backdrop?.Expand(Math.Max(height, Height));
        Morph(new Rect(Side, Gap, Detail.ActualWidth, Detail.ActualHeight), 28, OpenSpring, 0.7);
    }

    /// <summary>Growing happens at once (the glass needs the room now); shrinking waits until the glass has got there.</summary>
    void Resize(double height)
    {
        _restHeight = height;
        _shrink.Stop();
        if (height >= Height) Height = height; else _shrink.Start();
    }

    void Morph(Rect to, double radius, IEasingFunction ease, double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        Shape.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(to, time) { EasingFunction = ease });
        var corner = new DoubleAnimation(radius, time) { EasingFunction = (IEasingFunction)Resources["Smooth"] };
        Shape.BeginAnimation(RectangleGeometry.RadiusXProperty, corner);
        Shape.BeginAnimation(RectangleGeometry.RadiusYProperty, corner);
    }

    void Collapse()
    {
        if (!_open) return;
        _open = false;
        _reset.Start();
        Resize(CollapsedHeight);
        _backdrop?.Collapse();
        _media.Tracking = false;
        _agents.IsExpanded = false;
        Morph(Pill, 16, CloseSpring, 0.6);
        ((Storyboard)Resources["Close"]).Begin(this);
    }

    /// <summary>The island steps away for a screen capture, and for a fullscreen app only when that is switched on in Settings.</summary>
    void ApplyAutoHide()
    {
        var hide = _snipping || (_fullscreen!.IsFullscreen && SettingsStore.Current.HideInFullscreen);
        if (hide != _hidden) SetHidden(hide);
    }

    void SetHidden(bool hidden)
    {
        _hidden = hidden;
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

    // The open island's volume bar: press or drag anywhere on it to set the volume
    void Volume_Set(object sender, MouseEventArgs e)
    {
        var bar = (FrameworkElement)sender;
        _hud.Keep();
        if (e.LeftButton != MouseButtonState.Pressed || !_hud.IsVolume) return;
        bar.CaptureMouse();
        _hud.SetVolume(e.GetPosition(bar).X / bar.ActualWidth);
    }

    void Volume_Release(object sender, MouseButtonEventArgs e) => ((UIElement)sender).ReleaseMouseCapture();

    DateTime _scrubbed;

    // Press or drag along the progress bar to seek (a drag sends a new position at most every 120 ms)
    void Track_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var track = (FrameworkElement)sender;
        track.CaptureMouse();
        _scrubbed = DateTime.UtcNow;
        _media.Seek(e.GetPosition(track).X / track.ActualWidth);
    }

    void Track_MouseMove(object sender, MouseEventArgs e)
    {
        var track = (FrameworkElement)sender;
        if (!track.IsMouseCaptured || DateTime.UtcNow - _scrubbed < TimeSpan.FromMilliseconds(120)) return;
        _scrubbed = DateTime.UtcNow;
        _media.Seek(Math.Clamp(e.GetPosition(track).X / track.ActualWidth, 0, 1));
    }

    void Track_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var track = (FrameworkElement)sender;
        if (track.IsMouseCaptured) _media.Seek(Math.Clamp(e.GetPosition(track).X / track.ActualWidth, 0, 1));
        track.ReleaseMouseCapture();
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
