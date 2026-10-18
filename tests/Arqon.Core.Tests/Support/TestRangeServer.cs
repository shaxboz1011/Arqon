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

    /// <summary>Advertise Accept-Ranges on HEAD but answer every ranged GET with the whole file (status 200).</summary>
    public bool IgnoreRangeOnGet { get; set; }

    /// <summary>Make HEAD report this Content-Length while GET keeps serving the real size.</summary>
    public long? HeadLengthOverride { get; set; }

    /// <summary>Send a Content-Range whose start is off by one.</summary>
    public bool WrongRangeStart { get; set; }

    public string? ETag { get; set; }

    /// <summary>
    /// Like a real download server's fair-use limit: when one client address already has this many GETs in flight,
    /// the next one is answered 429 (with a Retry-After header when <see cref="RetryAfterSeconds"/> is set).
    /// </summary>
    public int? MaxConcurrentPerClient { get; set; }

    public int? RetryAfterSeconds { get; set; }

    /// <summary>How many requests were answered 429 so far.</summary>
    public int Rejected => Volatile.Read(ref _rejected);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<IPAddress, int> _inFlight = new();
    private int _rejected;

    /// <summary>Return true to never answer this client address at all (a link that is up but has no route to the server).</summary>
    public Func<IPAddress, bool>? HangFor { get; set; }

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
        var client = c.Request.RemoteEndPoint.Address;
        var counted = c.Request.HttpMethod != "HEAD" && MaxConcurrentPerClient is not null;
        try
        {
            var isHead = c.Request.HttpMethod == "HEAD";
            var seen = isHead ? _requests : Interlocked.Increment(ref _requests);
            var resp = c.Response;

            if (HangFor?.Invoke(client) == true) await Task.Delay(Timeout.Infinite, _cts.Token);

            if (counted)
            {
                var inFlight = _inFlight.AddOrUpdate(client, 1, (_, n) => n + 1);
                if (inFlight > MaxConcurrentPerClient)
                {
                    Interlocked.Increment(ref _rejected);
                    resp.StatusCode = 429;
                    if (RetryAfterSeconds is { } seconds) resp.AddHeader("Retry-After", seconds.ToString());
                    resp.Close();
                    return;
                }
            }

            if (FailFor?.Invoke(c.Request.RemoteEndPoint.Address, seen) == true)
            {
                resp.StatusCode = 503;
                resp.Close();
                return;
            }

            if (DelayMs > 0 && !isHead) await Task.Delay(DelayMs);

            if (ETag is not null) resp.AddHeader("ETag", ETag);

            long start = 0, end = Payload.Length - 1;
            var range = c.Request.Headers["Range"];
            if (SupportsRanges || IgnoreRangeOnGet)
            {
                resp.AddHeader("Accept-Ranges", "bytes");
                if (SupportsRanges && !IgnoreRangeOnGet && range is not null && range.StartsWith("bytes="))
                {
                    var parts = range["bytes=".Length..].Split('-');
                    start = long.Parse(parts[0]);
                    end = Math.Min(long.Parse(parts[1]), Payload.Length - 1);
                    resp.StatusCode = 206;
                    var shown = WrongRangeStart ? start + 1 : start;
                    resp.AddHeader("Content-Range", $"bytes {shown}-{end}/{Payload.Length}");
                }
            }

            var count = (int)(end - start + 1);
            resp.ContentLength64 = isHead && HeadLengthOverride is { } overrideLength ? overrideLength : count;
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
        finally
        {
            if (counted) _inFlight.AddOrUpdate(client, 0, (_, n) => n - 1);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Close();
    }
}
