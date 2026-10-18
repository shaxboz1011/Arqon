using System.Net;
using System.Net.Sockets;
using MonoTorrent.Connections;
using ReusableTasks;

namespace Arqon.Core.Torrents;

/// <summary>
/// Opens the torrent engine's outgoing peer connections. Each one is bound to the address of the next link, so the
/// connection leaves through that adapter: a torrent's many peer connections end up on Wi-Fi and on the phone alike,
/// without needing system mode.
/// </summary>
public sealed class LinkBoundSocketConnector(LinkRoundRobin links) : ISocketConnector
{
    public async ReusableTask<Socket> ConnectAsync(Uri uri, CancellationToken token)
    {
        // MonoTorrent hands out peers as ipv4://1.2.3.4:6881 or ipv6://[::1]:6881
        var address = IPAddress.Parse(uri.DnsSafeHost);
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            // IPv6 peers are left to Windows: the links are identified by their IPv4 addresses.
            if (address.AddressFamily == AddressFamily.InterNetwork && links.Next() is { } link)
                socket.Bind(new IPEndPoint(link.LocalAddress, 0));

            await socket.ConnectAsync(new IPEndPoint(address, uri.Port), token).ConfigureAwait(false);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
