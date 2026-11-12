using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DynamicIsland.Core;

/// <summary>
/// Is any program using the microphone or the camera right now? Windows records this for its own privacy indicator in the registry
/// (CapabilityAccessManager); the registry tells us when it changes, so there is no polling.
/// </summary>
sealed class PrivacyWatcher : IDisposable
{
    const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
    const int REG_NOTIFY_CHANGE_NAME = 1, REG_NOTIFY_CHANGE_LAST_SET = 4;

    [DllImport("advapi32.dll")]
    static extern int RegNotifyChangeKeyValue(IntPtr key, bool watchSubtree, int filter, IntPtr eventHandle, bool asynchronous);

    readonly string[] _names = ["microphone", "webcam"];
    readonly ManualResetEvent _stop = new(false);
    bool _mic, _camera;

    /// <summary>(microphone in use, camera in use). Raised on a background thread, only when something changed.</summary>
    public event Action<bool, bool>? Changed;
    public bool MicrophoneInUse => _mic;
    public bool CameraInUse => _camera;

    public PrivacyWatcher()
    {
        _mic = InUse(_names[0]);
        _camera = InUse(_names[1]);
        new Thread(Watch) { IsBackground = true, Name = "PrivacyWatcher" }.Start();
    }

    void Watch()
    {
        var keys = _names.Select(n => Registry.CurrentUser.OpenSubKey(Root + n)).ToArray();
        var events = keys.Select(_ => new AutoResetEvent(false)).ToArray();
        try
        {
            while (true)
            {
                for (var i = 0; i < keys.Length; i++)
                    if (keys[i] is not null) RegNotifyChangeKeyValue(keys[i]!.Handle.DangerousGetHandle(), true, REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET, events[i].SafeWaitHandle.DangerousGetHandle(), true);

                if (WaitHandle.WaitAny([.. events, _stop]) == events.Length) return;
                Thread.Sleep(150); // a start/stop writes several values: let them settle

                var (mic, camera) = (InUse(_names[0]), InUse(_names[1]));
                if (mic == _mic && camera == _camera) continue;
                (_mic, _camera) = (mic, camera);
                Changed?.Invoke(mic, camera);
            }
        }
        finally { foreach (var k in keys) k?.Dispose(); foreach (var e in events) e.Dispose(); }
    }

    /// <summary>A program is using it while it has a "last used start" and no "last used stop".</summary>
    static bool InUse(string capability)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(Root + capability);
            return root is not null && (AnyInUse(root) || root.OpenSubKey("NonPackaged") is { } desktop && Using(desktop, AnyInUse));
        }
        catch { return false; }
    }

    static bool Using(RegistryKey key, Func<RegistryKey, bool> check) { using (key) return check(key); }

    static bool AnyInUse(RegistryKey parent)
    {
        foreach (var name in parent.GetSubKeyNames())
        {
            using var app = parent.OpenSubKey(name);
            if (app?.GetValue("LastUsedTimeStart") is long start && start > 0 && app.GetValue("LastUsedTimeStop") is long stop && stop == 0) return true;
        }
        return false;
    }

    public void Dispose() => _stop.Set();
}
