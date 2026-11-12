using System.Net;
using System.Runtime.InteropServices;

namespace DynamicIsland.Core;

/// <summary>Every TCP port something on this PC is listening on, with the owning process id.</summary>
static class TcpListeners
{
    const int AF_INET = 2, AF_INET6 = 23, TCP_TABLE_OWNER_PID_LISTENER = 3;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);

    /// <param name="Exposed">Reachable from other devices (listening on every address) rather than only from this PC.</param>
    public readonly record struct Listener(int Port, int Pid, bool Exposed);

    public static List<Listener> Get()
    {
        var all = new List<Listener>();
        Read(AF_INET, rowSize: 24, portOffset: 8, pidOffset: 20, anyAddress: row => Marshal.ReadInt32(row, 4) == 0, all);
        Read(AF_INET6, rowSize: 56, portOffset: 20, pidOffset: 52, anyAddress: row => IsAllZero(row, 0, 16), all);
        // the same port is often listed for both IPv4 and IPv6: keep one
        return all.DistinctBy(l => (l.Port, l.Pid)).OrderBy(l => l.Port).ToList();
    }

    static void Read(int family, int rowSize, int portOffset, int pidOffset, Func<IntPtr, bool> anyAddress, List<Listener> into)
    {
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0); // asks how big the table is
        if (size <= 0) return;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0) != 0) return;
            var count = Marshal.ReadInt32(buffer);
            for (var i = 0; i < count; i++)
            {
                var row = buffer + 4 + i * rowSize;
                var port = (ushort)IPAddress.NetworkToHostOrder((short)Marshal.ReadInt32(row, portOffset)); // stored big-endian in the low 16 bits
                into.Add(new Listener(port, Marshal.ReadInt32(row, pidOffset), anyAddress(row)));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    static bool IsAllZero(IntPtr p, int offset, int length)
    {
        for (var i = 0; i < length; i++) if (Marshal.ReadByte(p, offset + i) != 0) return false;
        return true;
    }
}
