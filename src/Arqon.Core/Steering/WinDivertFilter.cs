using System.Net;
using System.Net.Sockets;

namespace Arqon.Core.Steering;

public static class WinDivertFilter
{
    /// <summary>
    /// Only what steering needs reaches user mode: outbound packets from the default address, and inbound packets
    /// addressed to the other links (which are the only targets a flow can be moved to). Null when there is nothing to steer.
    /// </summary>
    public static string? Build(IPAddress defaultAddress, IEnumerable<IPAddress> otherLinks)
    {
        var targets = otherLinks
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(defaultAddress))
            .Select(a => a.ToString())
            .Distinct()
            .ToList();
        if (targets.Count == 0) return null;

        var inbound = string.Join(" or ", targets.Select(t => $"ip.DstAddr == {t}"));
        return $"ip and (tcp or udp) and !loopback and ((outbound and ip.SrcAddr == {defaultAddress}) or (inbound and ({inbound})))";
    }
}
