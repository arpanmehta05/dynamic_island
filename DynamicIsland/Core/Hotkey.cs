using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DynamicIsland.Core;

/// <summary>One system-wide key combination ("Ctrl+Alt+I"). Windows delivers it as a message, so there is no keyboard hook and no polling.</summary>
sealed class Hotkey
{
    const int WM_HOTKEY = 0x0312, HotkeyId = 0x4449, MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, int modifiers, int vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    readonly HwndSource _source;

    public event Action? Pressed;

    public Hotkey(HwndSource source)
    {
        _source = source;
        source.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg != WM_HOTKEY || wParam.ToInt32() != HotkeyId) return IntPtr.Zero;
            handled = true;
            Pressed?.Invoke();
            return IntPtr.Zero;
        });
    }

    /// <summary>Replaces the current key. False when the combination is unknown or another program already owns it.</summary>
    public bool Register(string combo)
    {
        UnregisterHotKey(_source.Handle, HotkeyId);
        return Parse(combo, out var modifiers, out var vk) && RegisterHotKey(_source.Handle, HotkeyId, modifiers, vk);
    }

    /// <summary>Whether nobody owns this combination: tries it on <paramref name="window"/> and lets go again.</summary>
    public static bool CanRegister(IntPtr window, string combo)
    {
        if (!Parse(combo, out var modifiers, out var vk) || !RegisterHotKey(window, HotkeyId + 1, modifiers, vk)) return false;
        UnregisterHotKey(window, HotkeyId + 1);
        return true;
    }

    static bool Parse(string combo, out int modifiers, out int vk)
    {
        modifiers = MOD_NOREPEAT;
        vk = 0;
        var parts = combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !Enum.TryParse<Key>(parts[^1], true, out var key)) return false;
        foreach (var part in parts[..^1])
            modifiers |= part.ToLowerInvariant() switch { "ctrl" => MOD_CONTROL, "alt" => MOD_ALT, "shift" => MOD_SHIFT, "win" => MOD_WIN, _ => 0 };
        vk = KeyInterop.VirtualKeyFromKey(key);
        return true;
    }
}
