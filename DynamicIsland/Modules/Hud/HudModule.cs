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
    bool _showing, _mic, _camera, _isVolume;
    string _glyph = "";
    double _level;

    public bool Showing { get => _showing; private set => Set(ref _showing, value); }
    public string Glyph { get => _glyph; private set => Set(ref _glyph, value); }
    public double BarWidth { get => _level * BarLength; }
    /// <summary>0..1, for the bar that shows while the island is open.</summary>
    public double Level => _level;
    /// <summary>The bar being shown is the volume (the open island's bar can be dragged to change it; brightness can't).</summary>
    public bool IsVolume { get => _isVolume; private set => Set(ref _isVolume, value); }
    public bool Microphone { get => _mic; private set => Set(ref _mic, value); }
    public bool Camera { get => _camera; private set => Set(ref _camera, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public HudModule()
    {
        _hide.Tick += (_, _) => { _hide.Stop(); Showing = false; };

        try
        {
            _volume = new VolumeWatcher();
            _volume.Changed += (level, muted) => OnUi(() => Show(muted ? "" : "", muted ? 0 : level, true));
        }
        catch { /* no audio device: no volume pop-up */ }

        _brightness = new BrightnessWatcher();
        _brightness.Changed += percent => OnUi(() => Show("", percent / 100.0, false));

        _privacy = new PrivacyWatcher();
        Microphone = _privacy.MicrophoneInUse;
        Camera = _privacy.CameraInUse;
        _privacy.Changed += (mic, camera) => OnUi(() => { Microphone = mic; Camera = camera; });
    }

    public void SetVolume(double level) => _volume?.Set((float)level);

    void Show(string glyph, double level, bool volume)
    {
        Glyph = glyph;
        IsVolume = volume;
        _level = Math.Clamp(level, 0, 1);
        PropertyChanged?.Invoke(this, new(nameof(BarWidth)));
        PropertyChanged?.Invoke(this, new(nameof(Level)));
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
