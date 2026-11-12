using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Modules.Agents;

namespace DynamicIsland.Modules.Power;

/// <summary>One power control. Destructive ones need a second tap to confirm.</summary>
sealed class PowerAction : INotifyPropertyChanged
{
    bool _armed;

    public required string Name { get; init; }
    public required string Glyph { get; init; }
    public required bool NeedsConfirm { get; init; }
    public required Action Run { get; init; }
    public DateTime ArmedAt { get; private set; }

    public bool IsArmed
    {
        get => _armed;
        set
        {
            if (_armed == value) return;
            _armed = value;
            if (value) ArmedAt = DateTime.UtcNow;
            PropertyChanged?.Invoke(this, new(nameof(IsArmed)));
            PropertyChanged?.Invoke(this, new(nameof(Label)));
        }
    }

    public string Label => IsArmed ? "Confirm?" : Name;
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Lock, sleep, sign out, restart and shut down. Lock and sleep act at once; sign out, restart and shut down first turn red and ask
/// for a second tap (ignored for a moment after the first, so a double-click can't confirm by accident; it disarms itself after 3 seconds).
/// </summary>
sealed class PowerEntry : TabEntry
{
    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("user32.dll")] static extern bool ExitWindowsEx(uint flags, uint reason);
    [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvent);

    readonly DispatcherTimer _disarm = new() { Interval = TimeSpan.FromSeconds(3) };

    public override ViewKind Kind => ViewKind.Power;
    public override string Name => "Power";
    public override string Glyph => ""; // Segoe Fluent Icons: power button
    public override string GlyphFont => "Segoe Fluent Icons, Segoe MDL2 Assets";
    public override string Accent => "#FF6B6B";

    public IReadOnlyList<PowerAction> Actions { get; }
    public ICommand Invoke { get; }

    public PowerEntry()
    {
        IsRunning = true;
        if (Environment.GetEnvironmentVariable("DI_POWER_DRYRUN") is { Length: > 0 } log) File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss.fff} dryrun-active\n"); // proves test mode before any confirm is clicked
        Actions =
        [
            new() { Name = "Lock",      Glyph = "", NeedsConfirm = false, Run = () => Do("lock",     () => LockWorkStation()) },
            new() { Name = "Sleep",     Glyph = "", NeedsConfirm = false, Run = () => Do("sleep",    () => SetSuspendState(false, false, false)) },
            new() { Name = "Sign out",  Glyph = "", NeedsConfirm = true,  Run = () => Do("signout",  () => ExitWindowsEx(0, 0)) },
            new() { Name = "Restart",   Glyph = "", NeedsConfirm = true,  Run = () => Do("restart",  () => Shutdown("/r /t 0")) },
            new() { Name = "Shut down", Glyph = "", NeedsConfirm = true,  Run = () => Do("shutdown", () => Shutdown("/s /t 0")) },
        ];

        Invoke = new ParamCommand(p => { if (p is PowerAction a) Click(a); });
        _disarm.Tick += (_, _) => DisarmAll();
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Visible) && !Visible) DisarmAll(); }; // closing the island cancels any pending confirm
    }

    void Click(PowerAction a)
    {
        if (!a.NeedsConfirm) { DisarmAll(); a.Run(); return; }

        if (a.IsArmed)
        {
            if ((DateTime.UtcNow - a.ArmedAt).TotalMilliseconds < 400) return; // a double-click must not confirm
            DisarmAll();
            a.Run();
            return;
        }

        DisarmAll();
        a.IsArmed = true;
        _disarm.Start();
    }

    void DisarmAll()
    {
        _disarm.Stop();
        foreach (var a in Actions) a.IsArmed = false;
    }

    static void Shutdown(string args) =>
        Process.Start(new ProcessStartInfo("shutdown.exe", args) { CreateNoWindow = true, UseShellExecute = false });

    /// <summary>
    /// Runs the action. DI_POWER_DRYRUN=&lt;file&gt; (development only) makes it append the action's name to that file instead, so the
    /// whole confirm flow can be tested without actually shutting the PC down.
    /// </summary>
    static void Do(string name, Action act)
    {
        if (Environment.GetEnvironmentVariable("DI_POWER_DRYRUN") is { Length: > 0 } log) File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss.fff} {name}\n");
        else act();
    }
}
