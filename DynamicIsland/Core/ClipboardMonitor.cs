using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DynamicIsland.Core;

/// <summary>One clipboard listener shared by everything that cares about copies (text history, screenshots). Event-driven: no polling.</summary>
sealed class ClipboardMonitor
{
    const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)] static extern bool AddClipboardFormatListener(IntPtr hwnd);

    /// <summary>Raised once, right after listening starts (look at what is on the clipboard now).</summary>
    public event Action? Started;
    /// <summary>Raised whenever something new is copied.</summary>
    public event Action? Changed;

    public void Attach(HwndSource source)
    {
        AddClipboardFormatListener(source.Handle);
        source.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == WM_CLIPBOARDUPDATE) Changed?.Invoke();
            return IntPtr.Zero;
        });
        Started?.Invoke();
    }
}
