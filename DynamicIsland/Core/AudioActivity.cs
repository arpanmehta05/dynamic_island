using System.Runtime.InteropServices;

namespace DynamicIsland.Core;

/// <summary>
/// Is a given program producing sound right now? Reads Windows' audio sessions directly (the same data the volume mixer shows).
/// It deliberately never registers for notifications: a library that did so (and never let go) piled up listeners inside VLC's audio
/// session and crashed VLC the moment it was paused. Every COM object is released straight away.
/// </summary>
static class AudioActivity
{
    const int eRender = 0, eMultimedia = 1, CLSCTX_ALL = 23, AudioSessionStateActive = 1;

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorClass { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr groupId, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr groupId, uint flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    // IAudioSessionControl's methods come first in the vtable, then the "2" additions: the order matters, the unused ones are placeholders.
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, IntPtr context);
        [PreserveSig] int GetIconPath(IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, IntPtr context);
        [PreserveSig] int GetGroupingParam(IntPtr group);
        [PreserveSig] int SetGroupingParam(IntPtr group, IntPtr context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);   // never called
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int GetSessionIdentifier(IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(IntPtr id);
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    /// <summary>Whether the program's session is active, and its current sound level (0..1).</summary>
    public static (bool Active, float Peak) Probe(int pid)
    {
        var active = false; var level = 0f;
        IMMDeviceEnumerator? enumerator = null; IMMDevice? device = null; IAudioSessionManager2? manager = null; IAudioSessionEnumerator? list = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
            if (enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out device) != 0) return (false, 0f);

            var iid = typeof(IAudioSessionManager2).GUID;
            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var managerObject) != 0) return (false, 0f);
            manager = (IAudioSessionManager2)managerObject;
            if (manager.GetSessionEnumerator(out list) != 0 || list.GetCount(out var count) != 0) return (false, 0f);

            for (var i = 0; i < count; i++)
            {
                IAudioSessionControl2? session = null;
                try
                {
                    if (list.GetSession(i, out session) != 0) continue;
                    if (session.GetProcessId(out var owner) != 0 || owner != pid) continue;
                    if (session.GetState(out var state) == 0 && state == AudioSessionStateActive) active = true;
                    if (session is IAudioMeterInformation meter && meter.GetPeakValue(out var peak) == 0) level = Math.Max(level, peak);
                }
                finally { if (session is not null) Marshal.ReleaseComObject(session); }
            }
        }
        catch { /* no audio device, or the session went away */ }
        finally
        {
            foreach (var com in new object?[] { list, manager, device, enumerator }) if (com is not null) Marshal.ReleaseComObject(com);
        }
        return (active, level);
    }
}
