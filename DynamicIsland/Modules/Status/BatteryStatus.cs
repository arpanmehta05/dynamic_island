using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using Windows.Devices.Power;

namespace DynamicIsland.Modules.Status;

/// <summary>
/// The laptop's battery: percentage and whether it is plugged in. Driven by Windows' battery report and power events, never polled.
/// While the charger is connected (or the battery is low and unplugged) the pill widens and shows a little battery with the percentage;
/// otherwise the battery is not shown at all.
/// </summary>
sealed class BatteryStatus : INotifyPropertyChanged
{
    const int LowAt = 20, CriticalAt = 10;

    readonly Battery _battery = Battery.AggregateBattery; // held so its events keep coming
    bool _plugged, _ready;
    int _percent, _lowestAnnounced = 100;

    public bool HasBattery { get; private set; }
    public bool Plugged { get => _plugged; private set { if (_plugged == value) return; _plugged = value; Raise(nameof(Plugged), nameof(Accent), nameof(ShowIndicator)); } }
    public int Percent { get => _percent; private set { if (_percent == value) return; _percent = value; Raise(nameof(Percent), nameof(IndicatorText), nameof(FillWidth), nameof(ShowIndicator)); } }
    public bool ShowIndicator => HasBattery && (Plugged || Percent <= LowAt);
    public string IndicatorText => $"{Percent}%";
    /// <summary>Width of the fill inside the pill's little battery (its inner width is 19.6).</summary>
    public double FillWidth => 19.6 * Percent / 100;
    public string Accent => Plugged ? "#3DDC84" : "#FF5A5F";

    /// <summary>The charger was just connected.</summary>
    public event Action? PluggedIn;
    /// <summary>The battery dropped to 20% (and again to 10%) while unplugged.</summary>
    public event Action<int>? RanLow;
    public event PropertyChangedEventHandler? PropertyChanged;

    public BatteryStatus()
    {
        Refresh();
        _ready = true;
        _battery.ReportUpdated += (_, _) => OnUi(Refresh);
        SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode == PowerModes.StatusChange) OnUi(Refresh); };
    }

    void Refresh()
    {
        var power = System.Windows.Forms.SystemInformation.PowerStatus;
        var charge = power.BatteryChargeStatus; // Unknown is every bit set, so it has to be compared whole
        HasBattery = charge != System.Windows.Forms.BatteryChargeStatus.Unknown && !charge.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery);
        if (!HasBattery) { Plugged = false; return; }

        var wasPlugged = Plugged;
        Percent = (int)Math.Round(Math.Clamp(power.BatteryLifePercent, 0, 1) * 100);
        Plugged = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
        Raise(nameof(HasBattery), nameof(ShowIndicator));

        if (!_ready) return;
        if (Plugged && !wasPlugged) { _lowestAnnounced = 100; PluggedIn?.Invoke(); }
        else if (Plugged || Percent > LowAt) _lowestAnnounced = 100;
        else if (Percent <= CriticalAt && _lowestAnnounced > CriticalAt) { _lowestAnnounced = CriticalAt; RanLow?.Invoke(Percent); }
        else if (Percent <= LowAt && _lowestAnnounced > LowAt) { _lowestAnnounced = LowAt; RanLow?.Invoke(Percent); }
    }

    static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    void Raise(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new(n));
    }
}
