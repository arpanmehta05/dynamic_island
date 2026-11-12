using System.Management;

namespace DynamicIsland.Core;

/// <summary>Tells you when the laptop screen brightness changes (brightness keys, Settings), through Windows' own WMI event. Does nothing on screens without brightness control.</summary>
sealed class BrightnessWatcher : IDisposable
{
    readonly ManagementEventWatcher? _watcher;

    /// <summary>Brightness 0..100. Raised on a background thread.</summary>
    public event Action<int>? Changed;

    public BrightnessWatcher()
    {
        try
        {
            _watcher = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _watcher.EventArrived += (_, e) => Changed?.Invoke(Convert.ToInt32(e.NewEvent["Brightness"]));
            _watcher.Start();
        }
        catch { _watcher = null; } // desktop monitor: no brightness events, nothing to show
    }

    public void Dispose() { _watcher?.Stop(); _watcher?.Dispose(); }
}
