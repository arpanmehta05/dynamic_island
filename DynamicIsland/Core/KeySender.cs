using System.Runtime.InteropServices;

namespace DynamicIsland.Core;

/// <summary>Sends a Ctrl+V to whichever window has keyboard focus. The island never takes focus, so that is the app you were typing in.</summary>
static class KeySender
{
    const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 2;
    const ushort VK_CONTROL = 0x11, VK_V = 0x56;

    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);

    static INPUT Key(ushort vk, bool up) => new() { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } } };

    public static void CtrlV()
    {
        var inputs = new[] { Key(VK_CONTROL, false), Key(VK_V, false), Key(VK_V, true), Key(VK_CONTROL, true) };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
