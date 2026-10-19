using System.Net;
using Arqon.Core.Links;
using Arqon.Core.Speed;

namespace Arqon.Core.Tests.Speed;

public class LinkSpeedTestTests
{
    private static LinkInfo Link(string id) => new(id, id, "", IPAddress.Loopback, null, LinkKind.Wifi, 0);

    /// <summary>A test server that never ends a download: zeros for as long as they are read, optionally slowed down.</summary>
    private sealed class Endless(int delayMs = 0) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => Fill(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) => Fill(buffer);
        private int Fill(Span<byte> buffer)
        {
            if (delayMs > 0) Thread.Sleep(delayMs);
            buffer.Clear();
            return buffer.Length;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Handler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }

    private static HttpClient Client(int delayMs) =>
        new(new Handler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Endless(delayMs)) }));

    [Fact]
    public async Task Every_link_is_measured_on_its_own()
    {
        var fast = Link("fast");
        var slow = Link("slow");
        var test = new LinkSpeedTest(l => l.Id == "fast" ? Client(0) : Client(5), "http://test/file", connectionsPerLink: 1);

        var result = await test.RunAsync([fast, slow], TimeSpan.FromMilliseconds(600));

        Assert.True(result.BytesPerSecond["fast"] > 0);
        Assert.True(result.BytesPerSecond["slow"] > 0);
        Assert.True(result.BytesPerSecond["fast"] > result.BytesPerSecond["slow"], "the link with no delay must measure faster");
        Assert.Empty(result.Errors);
        Assert.Equal(result.BytesPerSecond["fast"] + result.BytesPerSecond["slow"], result.Total);
    }

    [Fact]
    public async Task The_test_ends_when_the_time_is_up_even_though_the_download_never_ends()
    {
        var test = new LinkSpeedTest(_ => Client(1), "http://test/file", connectionsPerLink: 2);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        await test.RunAsync([Link("a")], TimeSpan.FromMilliseconds(300));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task A_link_that_cannot_connect_is_reported_with_its_error_and_zero_speed()
    {
        var broken = new HttpClient(new Handler(() => throw new HttpRequestException("no route")));
        var test = new LinkSpeedTest(l => l.Id == "bad" ? broken : Client(0), "http://test/file", connectionsPerLink: 1);

        var result = await test.RunAsync([Link("good"), Link("bad")], TimeSpan.FromMilliseconds(300));

        Assert.True(result.BytesPerSecond["good"] > 0);
        Assert.Equal(0, result.BytesPerSecond["bad"]);
        Assert.Equal("no route", result.Errors["bad"]);
        Assert.False(result.Errors.ContainsKey("good"));
    }

    [Fact]
    public async Task A_server_error_counts_as_a_failure_of_that_link()
    {
        var failing = new HttpClient(new Handler(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var test = new LinkSpeedTest(_ => failing, "http://test/file", connectionsPerLink: 1);

        var result = await test.RunAsync([Link("a")], TimeSpan.FromMilliseconds(200));

        Assert.True(result.Errors.ContainsKey("a"));
    }

    [Fact]
    public async Task Cancelling_ends_the_test_early_without_an_error()
    {
        using var cts = new CancellationTokenSource(150);
        var test = new LinkSpeedTest(_ => Client(1), "http://test/file", connectionsPerLink: 1);

        var result = await test.RunAsync([Link("a")], TimeSpan.FromSeconds(30), cts.Token);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Megabits_are_bytes_times_eight_over_a_million()
    {
        var result = new SpeedTestResult(new Dictionary<string, double> { ["a"] = 1_250_000, ["b"] = 625_000 }, new Dictionary<string, string>());

        Assert.Equal(10, result.Mbit("a"), 6);
        Assert.Equal(15, result.TotalMbit, 6);
        Assert.Equal(0, result.Mbit("missing"));
    }
}
