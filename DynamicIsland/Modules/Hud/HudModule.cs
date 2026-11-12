using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Core;

namespace DynamicIsland.Modules.Hud;

/// <summary>
/// The notch's small status layer: an iPhone-style bar that replaces the clock for a moment when the volume or the screen brightness
/// changes, and a dot while a program is using the microphone (orange) or the camera (green).
/// </summary>
sealed class HudModule : INotifyPropertyChanged
{
    public const double BarLength = 70;

    readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    readonly VolumeWatcher? _volume;
    readonly BrightnessWatcher _brightness;
    readonly PrivacyWatcher _privacy;
    bool _showing, _mic, _camera;
    string _glyph = "";
    double _level;

    public bool Showing { get => _showing; private set => Set(ref _showing, value); }
    public string Glyph { get => _glyph; private set => Set(ref _glyph, value); }
    public double BarWidth { get => _level * BarLength; }
    public bool Microphone { get => _mic; private set => Set(ref _mic, value); }
    public bool Camera { get => _camera; private set => Set(ref _camera, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public HudModule()
    {
        _hide.Tick += (_, _) => { _hide.Stop(); Showing = false; };

        try
        {
            _volume = new VolumeWatcher();
            _volume.Changed += (level, muted) => OnUi(() => Show(muted ? "" : "", muted ? 0 : level));
        }
        catch { /* no audio device: no volume pop-up */ }

        _brightness = new BrightnessWatcher();
        _brightness.Changed += percent => OnUi(() => Show("", percent / 100.0));

        _privacy = new PrivacyWatcher();
        Microphone = _privacy.MicrophoneInUse;
        Camera = _privacy.CameraInUse;
        _privacy.Changed += (mic, camera) => OnUi(() => { Microphone = mic; Camera = camera; });
    }

    void Show(string glyph, double level)
    {
        Glyph = glyph;
        _level = Math.Clamp(level, 0, 1);
        PropertyChanged?.Invoke(this, new(nameof(BarWidth)));
        Showing = true;
        _hide.Stop();
        _hide.Start();
    }

    static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }
}
