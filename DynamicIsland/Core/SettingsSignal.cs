namespace DynamicIsland.Core;

/// <summary>
/// "Open the settings window" from outside the running island: a second launch (<c>DynamicIsland.exe --settings</c>, the Start menu entry)
/// raises a named event and exits; the running island waits for it. No polling: the wait is handled by the thread pool.
/// </summary>
static class SettingsSignal
{
    const string Name = "DynamicIsland.OpenSettings";
    static EventWaitHandle? _listening; // kept for the life of the process

    public static void Raise()
    {
        try { using var signal = EventWaitHandle.OpenExisting(Name); signal.Set(); }
        catch (WaitHandleCannotBeOpenedException) { /* the island isn't running (yet) */ }
    }

    public static void Listen(Action onSignal)
    {
        _listening = new EventWaitHandle(false, EventResetMode.AutoReset, Name);
        ThreadPool.RegisterWaitForSingleObject(_listening, (_, _) => onSignal(), null, Timeout.Infinite, false);
    }
}
