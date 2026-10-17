using System.Net;
using System.Net.Sockets;
using Arqon.Core.Links;

namespace Arqon.Core.Download;

public static class BoundHttp
{
    /// <summary>HttpClient whose every connection is bound to the link's local address, so traffic leaves through that adapter.</summary>
    public static HttpClient Create(LinkInfo link)
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 64,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.None,
            ConnectCallback = async (ctx, ct) =>
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    socket.Bind(new IPEndPoint(link.LocalAddress, 0));
                    await socket.ConnectAsync(ctx.DnsEndPoint, ct).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>HttpClient that lets Windows pick the route (used when no links are known).</summary>
    public static HttpClient CreateDefault() =>
        new(new SocketsHttpHandler { MaxConnectionsPerServer = 64 }, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
}
