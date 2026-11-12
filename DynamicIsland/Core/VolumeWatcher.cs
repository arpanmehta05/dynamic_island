using System.Runtime.InteropServices;

namespace DynamicIsland.Core;

/// <summary>
/// Tells you when the system volume or mute changes (volume keys, the tray slider, a headset button), as an event.
/// It follows the default speaker: if you plug in headphones it moves over to them.
/// </summary>
sealed class VolumeWatcher : IDisposable
{
    const int eRender = 0, eMultimedia = 1, CLSCTX_ALL = 23;

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorClass { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback client);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback client);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float db, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr notifyData);
    }

    [ClassInterface(ClassInterfaceType.None)]
    sealed class Callbacks(VolumeWatcher owner) : IAudioEndpointVolumeCallback, IMMNotificationClient
    {
        // AUDIO_VOLUME_NOTIFICATION_DATA: event context (16 bytes), muted (BOOL), master volume (float)
        public int OnNotify(IntPtr data)
        {
            owner.Changed?.Invoke(BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20)), Marshal.ReadInt32(data, 16) != 0);
            return 0;
        }
        public int OnDefaultDeviceChanged(int flow, int role, string id) { if (flow == eRender && role == eMultimedia) owner.Bind(); return 0; }
        public int OnDeviceStateChanged(string id, int state) => 0;
        public int OnDeviceAdded(string id) => 0;
        public int OnDeviceRemoved(string id) => 0;
        public int OnPropertyValueChanged(string id, IntPtr key) => 0;
    }

    readonly Callbacks _callbacks;
    readonly IMMDeviceEnumerator _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
    readonly object _lock = new();
    IAudioEndpointVolume? _endpoint;

    /// <summary>Volume 0..1 and whether it is muted. Raised on a background thread.</summary>
    public event Action<float, bool>? Changed;

    public VolumeWatcher()
    {
        _callbacks = new Callbacks(this);
        _enumerator.RegisterEndpointNotificationCallback(_callbacks);
        Bind();
    }

    void Bind()
    {
        lock (_lock)
        {
            Release();
            try
            {
                if (_enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out var device) != 0) return;
                var iid = typeof(IAudioEndpointVolume).GUID;
                var activated = device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var endpoint);
                Marshal.ReleaseComObject(device);
                if (activated != 0) return;
                _endpoint = (IAudioEndpointVolume)endpoint;
                _endpoint.RegisterControlChangeNotify(_callbacks);
            }
            catch { /* no speakers right now: the next default-device change tries again */ }
        }
    }

    void Release()
    {
        if (_endpoint is null) return;
        try { _endpoint.UnregisterControlChangeNotify(_callbacks); } catch { /* device already gone */ }
        Marshal.ReleaseComObject(_endpoint);
        _endpoint = null;
    }

    public void Dispose()
    {
        lock (_lock) Release();
        _enumerator.UnregisterEndpointNotificationCallback(_callbacks);
    }
}
