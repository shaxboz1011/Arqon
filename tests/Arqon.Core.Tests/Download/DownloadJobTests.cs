using System.Net;
using System.Security.Cryptography;
using Arqon.Core.Download;
using Arqon.Core.Links;
using Arqon.Core.Tests.Support;

namespace Arqon.Core.Tests.Download;

public sealed class DownloadJobTests : IDisposable
{
    private const int MiB = 1024 * 1024;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-tests-" + Guid.NewGuid().ToString("N"));

    public DownloadJobTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static LinkInfo Loopback(string id, string address) =>
        new(id, id, id, IPAddress.Parse(address), null, LinkKind.Ethernet, 0);

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private string Dest(string name = "out.bin") => Path.Combine(_dir, name);

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public async Task Downloads_identical_file_over_a_single_link()
    {
        using var server = new TestRangeServer(8 * MiB);
        var job = new DownloadJob();

        await job.RunAsync(new DownloadRequest(server.Url, Dest(), 512 * 1024), [Loopback("a", "127.0.0.2")], null, default);

        Assert.Equal(Hash(server.Payload), HashFile(Dest()));
        Assert.False(File.Exists(Dest() + ".arqpart"));
        Assert.False(File.Exists(Dest() + ".arqstate"));
    }

    [Fact]
    public async Task Falls_back_to_single_stream_when_server_has_no_range_support()
    {
        using var server = new TestRangeServer(4 * MiB) { SupportsRanges = false };

        await new DownloadJob().RunAsync(new DownloadRequest(server.Url, Dest(), 512 * 1024),
            [Loopback("a", "127.0.0.2"), Loopback("b", "127.0.0.3")], null, default);

        Assert.Equal(Hash(server.Payload), HashFile(Dest()));
    }

    [Fact]
    public async Task Splits_bytes_across_two_links()
    {
        using var server = new TestRangeServer(8 * MiB) { DelayMs = 30 };
        DownloadProgress? last = null;

        await new DownloadJob().RunAsync(new DownloadRequest(server.Url, Dest(), 512 * 1024),
            [Loopback("a", "127.0.0.2"), Loopback("b", "127.0.0.3")],
            new SyncProgress<DownloadProgress>(p => last = p), default);

        Assert.Equal(Hash(server.Payload), HashFile(Dest()));
        Assert.NotNull(last);
        Assert.Equal(server.Payload.Length, last!.Done);
        Assert.Equal(server.Payload.Length, last.PerLinkBytes.Values.Sum());
        Assert.True(last.PerLinkBytes["a"] > 0);
        Assert.True(last.PerLinkBytes["b"] > 0);
    }

    [Fact]
    public async Task Finishes_on_the_remaining_link_when_one_link_fails()
    {
        using var server = new TestRangeServer(6 * MiB) { DelayMs = 10 };
        server.FailFor = (ip, _) => ip.Equals(IPAddress.Parse("127.0.0.3"));
        DownloadProgress? last = null;

        await new DownloadJob().RunAsync(new DownloadRequest(server.Url, Dest(), 512 * 1024),
            [Loopback("a", "127.0.0.2"), Loopback("b", "127.0.0.3")],
            new SyncProgress<DownloadProgress>(p => last = p), default);

        Assert.Equal(Hash(server.Payload), HashFile(Dest()));
        Assert.Equal(0, last!.PerLinkBytes.GetValueOrDefault("b"));
    }

    [Fact]
    public async Task Finishes_when_a_link_drops_midway()
    {
        using var server = new TestRangeServer(8 * MiB) { DelayMs = 40 };
        server.FailFor = (ip, seen) => seen > 6 && ip.Equals(IPAddress.Parse("127.0.0.3"));

        await new DownloadJob().RunAsync(new DownloadRequest(server.Url, Dest(), 512 * 1024),
            [Loopback("a", "127.0.0.2"), Loopback("b", "127.0.0.3")], null, default);

        Assert.Equal(Hash(server.Payload), HashFile(Dest()));
    }

    [Fact]
    public async Task Resumes_after_cancel_and_requests_less_than_the_whole_file()
    {
        using var server = new TestRangeServer(16 * MiB) { DelayMs = 200 };
        var links = new[] { Loopback("a", "127.0.0.2"), Loopback("b", "127.0.0.3") };
        var request = new DownloadRequest(server.Url, Dest(), 512 * 1024);

        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<DownloadProgress>(p =>
        {
            if (p.Done >= p.Total * 0.3) cts.Cancel();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new DownloadJob().RunAsync(request, links, progress, cts.Token));
        Assert.False(File.Exists(Dest()));

        server.ResetCounters();
        await new DownloadJob().RunAsync(request, links, null, default);

        Assert.Equal(Hash(server.Payload), HashFile(Dest()));
        Assert.True(server.BytesServed < server.Payload.Length,
            $"second run requested {server.BytesServed} of {server.Payload.Length} bytes");
    }

    [Fact]
    public async Task Speed_limit_slows_the_download()
    {
        using var server = new TestRangeServer(4 * MiB);
        var request = new DownloadRequest(server.Url, Dest(), 1 * MiB, SpeedLimitBps: 2 * MiB);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await new DownloadJob().RunAsync(request, [Loopback("a", "127.0.0.2")], null, default);

        Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(1.5), $"took only {sw.Elapsed}");
        Assert.Equal(Hash(server.Payload), HashFile(Dest()));
    }

    [Fact]
    public async Task Works_with_no_links_by_using_the_default_route()
    {
        using var server = new TestRangeServer(2 * MiB);

        await new DownloadJob().RunAsync(new DownloadRequest(server.Url, Dest(), 512 * 1024), [], null, default);

        Assert.Equal(Hash(server.Payload), HashFile(Dest()));
    }

    [Fact]
    public async Task Fails_with_an_error_when_every_link_is_dead()
    {
        using var server = new TestRangeServer(2 * MiB);
        server.FailFor = (_, _) => true;

        await Assert.ThrowsAnyAsync<Exception>(() => new DownloadJob().RunAsync(
            new DownloadRequest(server.Url, Dest(), 512 * 1024),
            [Loopback("a", "127.0.0.2")], null, default));
        Assert.False(File.Exists(Dest()));
    }
}
