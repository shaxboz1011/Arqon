using System.Net;
using Arqon.Core.Links;
using Arqon.Core.Monitoring;

namespace Arqon.Core.Tests.Monitoring;

public class LinkMonitorTests
{
    private static readonly LinkInfo Wifi =
        new("wifi", "Wi-Fi", "Intel Wi-Fi", IPAddress.Parse("192.168.1.10"), IPAddress.Parse("192.168.1.1"), LinkKind.Wifi, 100_000_000);

    private sealed class FakeProvider(params LinkInfo[] links) : ILinkProvider
    {
        public IReadOnlyList<LinkInfo> GetLinks() => links;
    }

    private sealed class ScriptedProbe(params double?[] results) : IProbe
    {
        private int _i;
        public Task<double?> ProbeAsync(LinkInfo link, CancellationToken ct) =>
            Task.FromResult(results[Math.Min(_i++, results.Length - 1)]);
    }

    private sealed class FakeCounters : ICounterReader
    {
        public long Rx, Tx;
        public (long Rx, long Tx) Read(LinkInfo link) => (Rx, Tx);
    }

    private sealed class FakeClock
    {
        public DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        public DateTimeOffset Get() => Now;
    }

    [Fact]
    public async Task Computes_latency_jitter_and_loss_from_probe_window()
    {
        var clock = new FakeClock();
        var monitor = new LinkMonitor(new FakeProvider(Wifi), new ScriptedProbe(20, 22, 18, null),
            new FakeCounters(), TimeSpan.FromSeconds(1), clock.Get);

        for (var i = 0; i < 4; i++)
        {
            await monitor.TickAsync(CancellationToken.None);
            clock.Now = clock.Now.AddSeconds(1);
        }

        var s = monitor.Latest["wifi"];
        Assert.Equal(20.0, s.LatencyMs, 3);
        Assert.Equal(3.0, s.JitterMs, 3);
        Assert.Equal(0.25, s.Loss, 3);
    }

    [Fact]
    public async Task Computes_throughput_in_bytes_per_second_from_counter_deltas()
    {
        var clock = new FakeClock();
        var counters = new FakeCounters();
        var monitor = new LinkMonitor(new FakeProvider(Wifi), new ScriptedProbe(20),
            counters, TimeSpan.FromSeconds(1), clock.Get);

        await monitor.TickAsync(CancellationToken.None);
        clock.Now = clock.Now.AddSeconds(2);
        counters.Rx += 4_000_000;
        counters.Tx += 1_000_000;
        await monitor.TickAsync(CancellationToken.None);

        var s = monitor.Latest["wifi"];
        Assert.Equal(2_000_000, s.DownBps, 1);
        Assert.Equal(500_000, s.UpBps, 1);
    }

    [Fact]
    public async Task Raises_SampleReady_and_keeps_bounded_history()
    {
        var clock = new FakeClock();
        var monitor = new LinkMonitor(new FakeProvider(Wifi), new ScriptedProbe(20),
            new FakeCounters(), TimeSpan.FromSeconds(1), clock.Get);
        var raised = 0;
        monitor.SampleReady += _ => raised++;

        for (var i = 0; i < 130; i++)
        {
            await monitor.TickAsync(CancellationToken.None);
            clock.Now = clock.Now.AddSeconds(1);
        }

        Assert.Equal(130, raised);
        Assert.Equal(120, monitor.History("wifi").Count);
    }

    [Fact]
    public async Task Drops_state_for_links_that_disappear()
    {
        var clock = new FakeClock();
        var links = new List<LinkInfo> { Wifi };
        var provider = new MutableProvider(links);
        var monitor = new LinkMonitor(provider, new ScriptedProbe(20), new FakeCounters(), TimeSpan.FromSeconds(1), clock.Get);

        await monitor.TickAsync(CancellationToken.None);
        links.Clear();
        await monitor.TickAsync(CancellationToken.None);

        Assert.Empty(monitor.Latest);
    }

    private sealed class MutableProvider(List<LinkInfo> links) : ILinkProvider
    {
        public IReadOnlyList<LinkInfo> GetLinks() => links.ToList();
    }
}
