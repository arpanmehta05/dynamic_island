using System.ComponentModel;
using System.Windows.Threading;

namespace DynamicIsland.Modules;

/// <summary>Default content: the current time and date. Wakes once per minute, exactly on the minute.</summary>
class ClockModule : INotifyPropertyChanged
{
    readonly DispatcherTimer _timer = new();

    public string Time => DateTime.Now.ToString("h:mm tt");
    public string Date => DateTime.Now.ToString("dddd, d MMMM");
    public event PropertyChangedEventHandler? PropertyChanged;

    public ClockModule()
    {
        _timer.Tick += (_, _) => { PropertyChanged?.Invoke(this, new(null)); Arm(); };
        Arm();
    }

    void Arm()
    {
        var now = DateTime.Now;
        _timer.Interval = TimeSpan.FromMilliseconds(60_000 - now.Second * 1000 - now.Millisecond + 50);
        _timer.Start();
    }
}
