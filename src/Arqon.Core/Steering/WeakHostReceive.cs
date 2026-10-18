using System.Runtime.InteropServices;

namespace Arqon.Core.Steering;

/// <summary>
/// Reads and sets the IPv4 "weak host receive" flag of an interface (IP Helper). Steering needs it: a reply that
/// arrives on one adapter is handed to the stack addressed to another adapter's address, and with the default strong
/// host model Windows silently drops such a packet. The change applies to the running system only, not to the saved
/// configuration, so a reboot undoes it even if Arqon crashed.
/// </summary>
public static class WeakHostReceive
{
    private const ushort AfInet = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct IpInterfaceRow
    {
        public ushort Family;
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public uint MaxReassemblySize;
        public ulong InterfaceIdentifier;
        public uint MinRouterAdvertisementInterval;
        public uint MaxRouterAdvertisementInterval;
        public byte AdvertisingEnabled, ForwardingEnabled, WeakHostSend, WeakHostReceive, UseAutomaticMetric,
            UseNeighborUnreachabilityDetection, ManagedAddressConfigurationSupported, OtherStatefulConfigurationSupported,
            AdvertiseDefaultRoute;
        public uint RouterDiscoveryBehavior;
        public uint DadTransmits;
        public uint BaseReachableTime;
        public uint RetransmitTime;
        public uint PathMtuDiscoveryTimeout;
        public uint LinkLocalAddressBehavior;
        public uint LinkLocalAddressTimeout;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public uint[] ZoneIndices;
        public uint SitePrefixLength;
        public uint Metric;
        public uint NlMtu;
        public byte Connected, SupportsWakeUpPatterns, SupportsNeighborDiscovery, SupportsRouterDiscovery;
        public uint ReachableTime;
        public byte TransmitOffload, ReceiveOffload, DisableDefaultRoutes;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetIpInterfaceEntry(ref IpInterfaceRow row);

    [DllImport("iphlpapi.dll")]
    private static extern uint SetIpInterfaceEntry(ref IpInterfaceRow row);

    /// <summary>Current value, or null when the interface cannot be read.</summary>
    public static bool? Get(uint interfaceIndex)
    {
        var row = NewRow(interfaceIndex);
        return GetIpInterfaceEntry(ref row) == 0 ? row.WeakHostReceive != 0 : null;
    }

    /// <summary>Needs administrator rights. Returns false when Windows refused.</summary>
    public static bool Set(uint interfaceIndex, bool enabled)
    {
        var row = NewRow(interfaceIndex);
        if (GetIpInterfaceEntry(ref row) != 0) return false;

        row.WeakHostReceive = enabled ? (byte)1 : (byte)0;
        row.SitePrefixLength = 0;   // documented requirement for IPv4 rows before SetIpInterfaceEntry
        return SetIpInterfaceEntry(ref row) == 0;
    }

    private static IpInterfaceRow NewRow(uint interfaceIndex) =>
        new() { Family = AfInet, InterfaceIndex = interfaceIndex, ZoneIndices = new uint[16] };
}

/// <summary>Turns weak host receive on for the interfaces steering uses and puts back exactly what it changed.</summary>
public sealed class WeakHostScope : IDisposable
{
    private readonly Func<uint, bool?> _get;
    private readonly Action<uint, bool> _set;
    private readonly HashSet<uint> _changed = [];
    private readonly HashSet<uint> _handled = [];
    private readonly object _gate = new();

    public WeakHostScope() : this(WeakHostReceive.Get, (i, v) => WeakHostReceive.Set(i, v))
    {
    }

    public WeakHostScope(Func<uint, bool?> get, Action<uint, bool> set)
    {
        _get = get;
        _set = set;
    }

    /// <summary>Idempotent. Interfaces that cannot be read, or already have it on, are left alone.</summary>
    public void Enable(IEnumerable<uint> interfaceIndexes)
    {
        lock (_gate)
        {
            foreach (var index in interfaceIndexes)
            {
                if (!_handled.Add(index)) continue;
                if (_get(index) != false) continue;
                _set(index, true);
                _changed.Add(index);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var index in _changed) _set(index, false);
            _changed.Clear();
            _handled.Clear();
        }
    }
}
