using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Arqon.Core.Links;

namespace Arqon.Core.Monitoring;

/// <summary>Measures TCP connect time over a specific link by binding the socket to the link's address.</summary>
public sealed class TcpConnectProbe : IProbe
{
    private static readonly IPEndPoint[] Targets =
    [
        new(IPAddress.Parse("1.1.1.1"), 443),
        new(IPAddress.Parse("8.8.8.8"), 443),
    ];

    private readonly TimeSpan _timeout;

    public TcpConnectProbe(TimeSpan? timeout = null) => _timeout = timeout ?? TimeSpan.FromMilliseconds(1500);

    public async Task<double?> ProbeAsync(LinkInfo link, CancellationToken ct)
    {
        foreach (var target in Targets)
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.Bind(new IPEndPoint(link.LocalAddress, 0));
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);
                var sw = Stopwatch.StartNew();
                await socket.ConnectAsync(target, cts.Token).ConfigureAwait(false);
                return sw.Elapsed.TotalMilliseconds;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // try the next target
            }
        }
        return null;
    }
}
