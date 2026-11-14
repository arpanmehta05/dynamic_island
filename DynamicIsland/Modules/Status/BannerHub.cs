using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;

namespace DynamicIsland.Modules.Status;

/// <summary>What the banner says: a title, a quiet second line, an icon in an accent colour and, when there is one, a level bar.</summary>
sealed record BannerInfo(string Title, string Subtitle, Geometry Icon, string Accent, int? Percent, bool Charging);

/// <summary>
/// The pill's short announcements: the charger was connected, the battery is low, headphones just connected. One at a time, for a few seconds.
/// <see cref="Current"/> keeps its last content so the banner can fade out with the text it had.
/// </summary>
sealed class BannerHub : INotifyPropertyChanged
{
    readonly DispatcherTimer _dismiss = new() { Interval = TimeSpan.FromSeconds(4.2) };
    BannerInfo? _current;

    public BannerInfo? Current { get => _current; private set { _current = value; PropertyChanged?.Invoke(this, new(nameof(Current))); PropertyChanged?.Invoke(this, new(nameof(HasPercent))); PropertyChanged?.Invoke(this, new(nameof(IconFill))); } }
    /// <summary>Only the bolt is a solid shape; the other icons are line drawings.</summary>
    public string IconFill => _current is { } c && ReferenceEquals(c.Icon, Logos.Bolt) ? c.Accent : "Transparent";
    public bool HasPercent => _current?.Percent is not null;
    public bool Showing { get; private set; }

    public event Action<bool>? ShowingChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public BannerHub(BatteryStatus battery, AudioDeviceStatus audio)
    {
        _dismiss.Tick += (_, _) => { _dismiss.Stop(); Set(false); };

        battery.PluggedIn += () => Show(new(battery.Percent >= 100 ? "Fully charged" : "Charging", "Charger connected", Logos.Bolt, battery.Accent, battery.Percent, true));
        battery.RanLow += percent => Show(new("Battery low", percent <= 10 ? "Plug in the charger now" : "Plug in the charger soon", Logos.Battery, "#FF5A5F", percent, false));
        audio.DeviceConnected += (name, kind, level) => Show(new(name, "Connected", AudioDeviceStatus.IconFor(kind) ?? Logos.Headphones, "#8B7CFF", level, false));

        // Dev: DI_DEMO=charge|low|device|headphones|wired shows that banner four seconds after startup
        if (Environment.GetEnvironmentVariable("DI_DEMO") is { } demo)
        {
            var once = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            once.Tick += (_, _) =>
            {
                once.Stop();
                Show(demo switch
                {
                    "low" => new("Battery low", "Plug in the charger soon", Logos.Battery, "#FF5A5F", 18, false),
                    "device" => new("CMF Buds 2a", "Connected", Logos.Earbuds, "#8B7CFF", 82, false),
                    "headphones" => new("WH-1000XM5", "Connected", Logos.Headphones, "#8B7CFF", 64, false),
                    "wired" => new("Wired headphones", "Connected", Logos.WiredHeadphones, "#8B7CFF", null, false),
                    _ => new("Charging", "Charger connected", Logos.Bolt, "#3DDC84", 64, true),
                });
            };
            once.Start();
        }
    }

    void Show(BannerInfo info)
    {
        Current = info;
        _dismiss.Stop();
        _dismiss.Start();
        Set(true);
    }

    void Set(bool showing)
    {
        if (Showing == showing) return;
        Showing = showing;
        PropertyChanged?.Invoke(this, new(nameof(Showing)));
        ShowingChanged?.Invoke(showing);
    }
}
