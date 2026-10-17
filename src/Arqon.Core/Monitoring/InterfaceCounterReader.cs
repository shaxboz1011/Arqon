using System.Net.NetworkInformation;
using Arqon.Core.Links;

namespace Arqon.Core.Monitoring;

public sealed class InterfaceCounterReader : ICounterReader
{
    public (long Rx, long Tx) Read(LinkInfo link)
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id == link.Id);
        if (nic is null) return (0, 0);
        var stats = nic.GetIPStatistics();
        return (stats.BytesReceived, stats.BytesSent);
    }
}
