using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Arqon.Core.Steering;

/// <summary>Maps a local TCP/UDP port to the owning process using the IP Helper tables.</summary>
public sealed class WindowsProcessResolver : IProcessResolver
{
    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;
    private const int UdpTableOwnerPid = 1;
    private static readonly TimeSpan MinRefresh = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan NameTtl = TimeSpan.FromSeconds(30);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);

    private readonly object _gate = new();
    private readonly Dictionary<ushort, int> _tcp = new();
    private readonly Dictionary<ushort, int> _udp = new();
    private readonly Dictionary<int, (string Name, long At)> _names = new();
    private long _lastRefresh;

    public ProcessInfo? Resolve(byte protocol, ushort localPort)
    {
        if (protocol is not (6 or 17)) return null;

        lock (_gate)
        {
            var table = protocol == 6 ? _tcp : _udp;
            if (!table.TryGetValue(localPort, out var pid))
            {
                // A just-opened socket is missing from a stale snapshot: refresh, but not more than a few times a second.
                if (Stopwatch.GetElapsedTime(_lastRefresh).TotalMilliseconds < MinRefresh.TotalMilliseconds && _lastRefresh != 0)
                    return null;
                Refresh();
                if (!table.TryGetValue(localPort, out pid)) return null;
            }
            return new ProcessInfo(pid, NameOf(pid));
        }
    }

    private void Refresh()
    {
        _lastRefresh = Stopwatch.GetTimestamp();
        Read(_tcp, tcp: true);
        Read(_udp, tcp: false);
    }

    private static void Read(Dictionary<ushort, int> into, bool tcp)
    {
        into.Clear();
        var size = 0;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            // The table can grow between the size query and the read, so retry a few times.
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var rc = tcp
                    ? GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidAll, 0)
                    : GetExtendedUdpTable(buffer, ref size, false, AfInet, UdpTableOwnerPid, 0);
                if (rc == 0) break;
                if (rc != 122) return; // 122 = insufficient buffer; anything else is a real failure
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                buffer = Marshal.AllocHGlobal(size);
            }
            if (buffer == IntPtr.Zero) return;

            var count = Marshal.ReadInt32(buffer);
            var rowSize = tcp ? 24 : 12;
            var portOffset = tcp ? 8 : 4;   // TCP rows start with the connection state
            var pidOffset = tcp ? 20 : 8;
            for (var i = 0; i < count; i++)
            {
                var row = buffer + 4 + i * rowSize;
                var raw = (uint)Marshal.ReadInt32(row, portOffset);
                var port = (ushort)((raw & 0xFF) << 8 | (raw >> 8) & 0xFF);
                into[port] = Marshal.ReadInt32(row, pidOffset);
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    private string NameOf(int pid)
    {
        var now = Stopwatch.GetTimestamp();
        if (_names.TryGetValue(pid, out var cached) && Stopwatch.GetElapsedTime(cached.At, now) < NameTtl) return cached.Name;

        string name;
        try { name = Process.GetProcessById(pid).ProcessName + ".exe"; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { name = ""; }

        _names[pid] = (name, now);
        return name;
    }
}
