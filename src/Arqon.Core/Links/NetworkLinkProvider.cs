using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Arqon.Core.Links;

/// <summary>Lists connected adapters that have an IPv4 address and a gateway.</summary>
public sealed class NetworkLinkProvider : ILinkProvider
{
    public IReadOnlyList<LinkInfo> GetLinks()
    {
        var result = new List<LinkInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;

            var props = nic.GetIPProperties();
            var address = props.UnicastAddresses
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IsLinkLocal(a));
            if (address is null) continue;

            var gateway = props.GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(g => g.AddressFamily == AddressFamily.InterNetwork && !g.Equals(IPAddress.Any));
            if (gateway is null) continue;

            var kind = LinkClassifier.Classify(nic.Description, nic.NetworkInterfaceType);
            if (kind == LinkKind.Other) continue;

            result.Add(new LinkInfo(nic.Id, nic.Name, nic.Description, address, gateway, kind, nic.Speed));
        }
        return result;
    }

    private static bool IsLinkLocal(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return b[0] == 169 && b[1] == 254;
    }
}
