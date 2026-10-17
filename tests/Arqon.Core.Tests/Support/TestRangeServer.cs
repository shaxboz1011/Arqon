using System.Net;
using System.Net.Sockets;

namespace Arqon.Core.Tests.Support;

/// <summary>Tiny HTTP server on 127.0.0.1 that serves a deterministic payload and honours Range requests.</summary>
public sealed class TestRangeServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private long _served;
    private int _requests;

    public byte[] Payload { get; }
    public bool SupportsRanges { get; set; } = true;
    public int DelayMs { get; set; }

    /// <summary>Return true to answer 503 for this client address; gets the number of GET requests seen so far.</summary>
    public Func<IPAddress, int, bool>? FailFor { get; set; }

    public Uri Url { get; }
    public long BytesServed => Interlocked.Read(ref _served);

    public TestRangeServer(int sizeBytes)
    {
        Payload = new byte[sizeBytes];
        new Random(1).NextBytes(Payload);

        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Url = new Uri($"http://127.0.0.1:{port}/file.bin");
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    public void ResetCounters() => Interlocked.Exchange(ref _served, 0);

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private async Task Handle(HttpListenerContext c)
    {
        try
        {
            var isHead = c.Request.HttpMethod == "HEAD";
            var seen = isHead ? _requests : Interlocked.Increment(ref _requests);
            var resp = c.Response;

            if (FailFor?.Invoke(c.Request.RemoteEndPoint.Address, seen) == true)
            {
                resp.StatusCode = 503;
                resp.Close();
                return;
            }

            if (DelayMs > 0 && !isHead) await Task.Delay(DelayMs);

            long start = 0, end = Payload.Length - 1;
            var range = c.Request.Headers["Range"];
            if (SupportsRanges)
            {
                resp.AddHeader("Accept-Ranges", "bytes");
                if (range is not null && range.StartsWith("bytes="))
                {
                    var parts = range["bytes=".Length..].Split('-');
                    start = long.Parse(parts[0]);
                    end = Math.Min(long.Parse(parts[1]), Payload.Length - 1);
                    resp.StatusCode = 206;
                    resp.AddHeader("Content-Range", $"bytes {start}-{end}/{Payload.Length}");
                }
            }

            var count = (int)(end - start + 1);
            resp.ContentLength64 = count;
            if (isHead)
            {
                resp.Close();
                return;
            }

            await resp.OutputStream.WriteAsync(Payload.AsMemory((int)start, count));
            Interlocked.Add(ref _served, count);
            resp.Close();
        }
        catch
        {
            // client went away (cancelled download) – expected in tests
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Close();
    }
}
