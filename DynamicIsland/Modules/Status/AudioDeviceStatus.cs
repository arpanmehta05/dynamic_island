using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace DynamicIsland.Modules.Status;

public enum AudioKind { None, Earbuds, Headphones, Wired }

/// <summary>
/// The audio device you are wearing: Bluetooth earbuds / headphones that are connected (with their name and battery level when they report
/// one) or wired headphones plugged into the default output. Found through Windows' connection events and default-output changes.
/// </summary>
sealed partial class AudioDeviceStatus : INotifyPropertyChanged
{
    // The battery level Windows keeps on a paired Bluetooth device's hands-free node (the "Battery" shown in Bluetooth settings)
    const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2", FormFactorKey = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e} 0";
    const int FormHeadphones = 3, FormHeadset = 5;

    sealed record Found(AudioKind Kind, string Name, int? Battery);

    readonly DispatcherTimer _debounce = new();
    readonly DeviceWatcher _connected, _batteries, _outputs;
    BluetoothDevice? _worn; // the connected device being watched: its own status event is the quickest sign that it went away
    bool _ready, _busy, _again;
    AudioKind _kind;
    string _name = "";
    int? _battery;

    public AudioKind Kind { get => _kind; private set { if (_kind == value) return; _kind = value; Raise(nameof(Kind), nameof(IsConnected), nameof(Icon)); } }
    public bool IsConnected => _kind != AudioKind.None;
    public string Name { get => _name; private set { if (_name == value) return; _name = value; Raise(nameof(Name)); } }
    public int? Battery { get => _battery; private set { if (_battery == value) return; _battery = value; Raise(nameof(Battery)); } }
    public Geometry? Icon => IconFor(_kind);

    public static Geometry? IconFor(AudioKind kind) => kind switch
    {
        AudioKind.Earbuds => Logos.Earbuds,
        AudioKind.Headphones => Logos.Headphones,
        AudioKind.Wired => Logos.WiredHeadphones,
        _ => null,
    };

    /// <summary>A device just connected (not raised for what was already connected at startup).</summary>
    public event Action<string, AudioKind, int?>? DeviceConnected;
    /// <summary>The device you were wearing went away (switched off, out of range, unplugged).</summary>
    public event Action<string, AudioKind>? DeviceDisconnected;
    public event PropertyChangedEventHandler? PropertyChanged;

    public AudioDeviceStatus()
    {
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RefreshAsync(); };

        _connected = DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected));
        _connected.Added += (_, _) => Later();
        _connected.Removed += (_, _) => Later(0);
        _connected.Updated += (_, _) => Later();
        _connected.Start();

        // an audio output appearing or disappearing (the earbuds' sound endpoint goes the moment they drop)
        _outputs = DeviceInformation.CreateWatcher(MediaDevice.GetAudioRenderSelector());
        _outputs.Added += (_, _) => Later();
        _outputs.Removed += (_, _) => Later(0);
        _outputs.Updated += (_, _) => Later(60);
        _outputs.Start();

        // a hands-free node's battery value changed
        _batteries = DeviceInformation.CreateWatcher("System.Devices.DeviceInstanceId:~~\"0000111e-0000-1000\"", [BatteryKey], DeviceInformationKind.Device);
        _batteries.Updated += (_, _) => Later();
        _batteries.Start();

        MediaDevice.DefaultAudioRenderDeviceChanged += (_, _) => Later(60);
        _ = RefreshAsync();
    }

    /// <summary>
    /// Events arrive in bursts (a connecting device raises many): look once, shortly after the last one. Something going away is
    /// looked at at once (0), so that its banner is not late.
    /// </summary>
    void Later(int ms = 350) => Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        _debounce.Stop();
        if (ms == 0) { _ = RefreshAsync(); return; }
        _debounce.Interval = TimeSpan.FromMilliseconds(ms);
        _debounce.Start();
    });

    /// <summary>The watched device's own connection status changed: if it dropped, say so straight away, then look for what else is connected.</summary>
    void OnWornStatus(BluetoothDevice device, object _)
    {
        if (device.ConnectionStatus == BluetoothConnectionStatus.Connected) return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (Kind is AudioKind.Earbuds or AudioKind.Headphones) Apply(null);
            _ = RefreshAsync();
        });
    }

    void Unwatch()
    {
        if (_worn is null) return;
        _worn.ConnectionStatusChanged -= OnWornStatus;
        _worn.Dispose();
        _worn = null;
    }

    async Task RefreshAsync()
    {
        if (_busy) { _again = true; return; }
        _busy = true;
        try
        {
            do
            {
                _again = false;
                var found = await FindAsync();
                await Application.Current.Dispatcher.InvokeAsync(() => Apply(found));
            } while (_again);
        }
        catch (Exception ex) { App.Log("audio device", ex); }
        finally { _busy = false; }
    }

    /// <summary>Development: DI_DEMO_AUDIO=earbuds|headphones|wired pretends that kind of device is connected.</summary>
    static readonly AudioKind? Demo = Enum.TryParse<AudioKind>(Environment.GetEnvironmentVariable("DI_DEMO_AUDIO"), true, out var demo) ? demo : null;

    void Apply(Found? found)
    {
        if (Demo is { } d) found = new Found(d, d == AudioKind.Wired ? "Wired headphones" : "Demo device", 72);
        var arrived = found is not null && (Kind == AudioKind.None || Name != found.Name);
        var left = found is null && Kind != AudioKind.None ? (Name, Kind) : default((string, AudioKind)?);
        Kind = found?.Kind ?? AudioKind.None;
        Name = found?.Name ?? "";
        Battery = found?.Battery;
        if (arrived && _ready) DeviceConnected?.Invoke(found!.Name, found.Kind, found.Battery);
        if (left is { } gone && _ready) DeviceDisconnected?.Invoke(gone.Item1, gone.Item2);
        _ready = true;
    }

    async Task<Found?> FindAsync()
    {
        foreach (var info in await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected)))
        {
            var bt = await BluetoothDevice.FromIdAsync(info.Id);
            // the list can lag behind: the device itself knows whether it is connected right now
            if (bt is null || bt.ConnectionStatus != BluetoothConnectionStatus.Connected) { bt?.Dispose(); continue; }
            var cod = bt.ClassOfDevice;
            var minor = (cod.RawValue >> 2) & 0x3F; // audio minor classes: 1 wearable headset, 2 hands-free, 5 loudspeaker, 6 headphones...
            if (cod.MajorClass != BluetoothMajorClass.AudioVideo || minor == 5) { bt.Dispose(); continue; } // a phone or a speaker is not what you wear

            var earbuds = minor == 1 || Earbud().IsMatch(bt.Name);
            var found = new Found(earbuds ? AudioKind.Earbuds : AudioKind.Headphones, bt.Name, await BatteryOf(bt.BluetoothAddress));
            if (_worn?.BluetoothAddress == bt.BluetoothAddress) bt.Dispose();
            else { Unwatch(); _worn = bt; bt.ConnectionStatusChanged += OnWornStatus; }
            return found;
        }
        Unwatch();

        var id = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
        if (string.IsNullOrEmpty(id)) return null;
        var output = await DeviceInformation.CreateFromIdAsync(id, [FormFactorKey]);
        return output.Properties.TryGetValue(FormFactorKey, out var form) && Convert.ToInt32(form) is FormHeadphones or FormHeadset
            ? new Found(AudioKind.Wired, Convert.ToInt32(form) == FormHeadset ? "Wired headset" : "Wired headphones", null)
            : null;
    }

    static async Task<int?> BatteryOf(ulong address)
    {
        foreach (var node in await DeviceInformation.FindAllAsync($"System.Devices.DeviceInstanceId:~~\"{address:X12}\"", [BatteryKey], DeviceInformationKind.Device))
            if (node.Properties.TryGetValue(BatteryKey, out var level) && level is not null) return Convert.ToInt32(level);
        return null;
    }

    [GeneratedRegex("bud|pod|ear|tws|fit|air", RegexOptions.IgnoreCase)] private static partial Regex Earbud();

    void Raise(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new(n));
    }
}
