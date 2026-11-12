using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland.Core;

/// <summary>Reads another process's command line and working folder, and checks whether we are allowed to end it.</summary>
static class ProcessInfo
{
    const uint PROCESS_TERMINATE = 0x1, PROCESS_VM_READ = 0x10, PROCESS_QUERY_INFORMATION = 0x400, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const int ProcessBasicInformation = 0, ProcessCommandLineInformation = 60;

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_BASIC_INFORMATION { public IntPtr ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, InheritedFromUniqueProcessId; }

    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr h, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr h, int cls, ref PROCESS_BASIC_INFORMATION info, int size, out int returned);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr h, int cls, IntPtr buffer, int size, out int returned);

    /// <summary>True when this program may end the process (same user, not elevated/system): the ones worth listing for "kill".</summary>
    public static bool CanTerminate(int pid)
    {
        var h = OpenProcess(PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return false;
        CloseHandle(h);
        return true;
    }

    public static string CommandLine(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return "";
        try
        {
            NtQueryInformationProcess(h, ProcessCommandLineInformation, IntPtr.Zero, 0, out var needed);
            if (needed <= 0 || needed > 64 * 1024) return "";
            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (NtQueryInformationProcess(h, ProcessCommandLineInformation, buffer, needed, out _) != 0) return "";
                var length = Marshal.ReadInt16(buffer);                         // UNICODE_STRING.Length (bytes)
                var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);              // UNICODE_STRING.Buffer
                return text == IntPtr.Zero ? "" : Marshal.PtrToStringUni(text, length / 2);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch { return ""; }
        finally { CloseHandle(h); }
    }

    /// <summary>The folder the process was started in (for a dev server, its project). Empty when it can't be read.</summary>
    public static string WorkingDirectory(int pid)
    {
        if (!Environment.Is64BitProcess) return "";
        var h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
        if (h == IntPtr.Zero) return "";
        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            if (NtQueryInformationProcess(h, ProcessBasicInformation, ref pbi, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _) != 0) return "";

            // PEB -> ProcessParameters (+0x20) -> CurrentDirectory.DosPath (UNICODE_STRING at +0x38: Length, then Buffer at +0x40)
            var parameters = ReadPointer(h, pbi.PebBaseAddress + 0x20);
            if (parameters == IntPtr.Zero) return "";
            var head = new byte[2];
            if (!ReadProcessMemory(h, parameters + 0x38, head, 2, out _)) return "";
            int length = BitConverter.ToUInt16(head, 0);
            var buffer = ReadPointer(h, parameters + 0x40);
            if (length == 0 || length > 2048 || buffer == IntPtr.Zero) return "";

            var bytes = new byte[length];
            if (!ReadProcessMemory(h, buffer, bytes, length, out _)) return "";
            var path = Encoding.Unicode.GetString(bytes).TrimEnd('\\');
            return path.Length >= 2 && path[1] == ':' ? path : ""; // sanity check: a real drive path
        }
        catch { return ""; }
        finally { CloseHandle(h); }
    }

    static IntPtr ReadPointer(IntPtr process, IntPtr address)
    {
        var bytes = new byte[IntPtr.Size];
        return ReadProcessMemory(process, address, bytes, bytes.Length, out _) ? new IntPtr(BitConverter.ToInt64(bytes, 0)) : IntPtr.Zero;
    }
}
