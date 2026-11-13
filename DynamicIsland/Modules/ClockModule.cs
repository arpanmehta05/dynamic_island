using System.ComponentModel;
using System.Windows.Threading;

namespace DynamicIsland.Modules;

/// <summary>Default content: the current time and date. Wakes once per minute, exactly on the minute.</summary>
class ClockModule : INotifyPropertyChanged
{
    readonly DispatcherTimer _timer = new();

    public string Time => DateTime.Now.ToString("h:mm tt");
    public string Date => DateTime.Now.ToString("dddd, d MMMM");
    public string Greeting => DateTime.Now.Hour switch { < 5 => "Up late", < 12 => "Good morning", < 17 => "Good afternoon", < 22 => "Good evening", _ => "Good night" };
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
