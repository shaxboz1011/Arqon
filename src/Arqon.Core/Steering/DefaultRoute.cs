using System.Net;
using System.Net.Sockets;

namespace Arqon.Core.Steering;

public static class DefaultRoute
{
    /// <summary>The local address Windows would use to reach the internet. Connecting a UDP socket sends nothing.</summary>
    public static IPAddress? LocalAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53));
            return (socket.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
            return null; // no route at all
        }
    }
}
