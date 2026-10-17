using System.Net;
using Arqon.Core.Monitoring;
using Arqon.Core.Policy;
using Arqon.Core.Settings;
using Arqon.Core.Steering;

namespace Arqon.Core.Tests.Steering;

public class SteeringCoordinatorTests
{
    private static LinkSample S(string id, double latency, double downBps = 1_000_000) =>
        new(id, DateTimeOffset.UnixEpoch, latency, 0, 0, downBps, 0);

    private static FlowKey Flow(int i, byte protocol = 6) =>
        new(protocol, IPAddress.Parse("192.168.1.20"), (ushort)(40000 + i % 20000), IPAddress.Parse("93.184.216.34"), 443);

    private sealed class FakeSteerer : ISteerer
    {
        public bool IsAvailable => true;
        public bool Stopped;
        public void Start() { }
        public void Stop() => Stopped = true;
        public event Action<string>? Faulted;
        public void Fault(string why) => Faulted?.Invoke(why);
    }

    private sealed class Clock
    {
        public DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    }

    private static (SteeringCoordinator Coordinator, Clock Clock, FakeSteerer Steerer) Make(
        Func<IReadOnlyList<LinkSample>> samples, params AppRule[] rules)
    {
        var clock = new Clock();
        var steerer = new FakeSteerer();
        var table = new FlowTable(() => clock.Now);
        var engine = new RuleEngine(ArqonSettings.Default() with { Rules = rules.ToList() });
        return (new SteeringCoordinator(steerer, table, engine, samples), clock, steerer);
    }

    [Fact]
    public void New_flows_are_split_by_measured_speed()
    {
        var (c, _, _) = Make(() => [S("wifi", 20, 6_000_000), S("phone", 40, 4_000_000)]);

        var counts = new Dictionary<string, int>();
        for (var i = 0; i < 1000; i++)
        {
            var link = c.PickLink(Flow(i), "chrome.exe")!;
            counts[link] = counts.GetValueOrDefault(link) + 1;
        }

        Assert.InRange(counts["wifi"], 550, 650);
        Assert.InRange(counts["phone"], 350, 450);
    }

    [Fact]
    public void An_existing_flow_never_moves_even_when_weights_change()
    {
        var weights = new[] { 6_000_000.0, 4_000_000.0 };
        var (c, _, _) = Make(() => [S("wifi", 20, weights[0]), S("phone", 40, weights[1])]);

        var first = Enumerable.Range(0, 50).Select(i => c.PickLink(Flow(i), "chrome.exe")).ToList();
        weights[0] = 1;
        weights[1] = 9_000_000;
        var again = Enumerable.Range(0, 50).Select(i => c.PickLink(Flow(i), "chrome.exe")).ToList();

        Assert.Equal(first, again);
    }

    [Fact]
    public void Game_flows_go_to_the_best_scoring_link()
    {
        var (c, _, _) = Make(() => [S("wifi", 18), S("phone", 45)]);
        for (var i = 0; i < 20; i++)
            Assert.Equal("wifi", c.PickLink(Flow(i, 17), "valorant.exe"));
    }

    [Fact]
    public void Only_rule_pins_the_flow_to_that_link_and_falls_back_when_it_is_missing()
    {
        var rule = new AppRule("steam.exe", AppKind.Downloader, LinkChoice.Only, "phone", null);
        var (c, _, _) = Make(() => [S("wifi", 18), S("phone", 45)], rule);
        Assert.Equal("phone", c.PickLink(Flow(1), "steam.exe"));

        var (c2, _, _) = Make(() => [S("wifi", 18)], rule);
        Assert.Equal("wifi", c2.PickLink(Flow(1), "steam.exe"));
    }

    [Fact]
    public void No_samples_means_no_decision_so_windows_routes_normally()
    {
        var (c, _, _) = Make(() => []);
        Assert.Null(c.PickLink(Flow(1), "chrome.exe"));
    }

    [Fact]
    public void Idle_flows_expire_after_60s_for_tcp_and_30s_for_udp()
    {
        var clock = new Clock();
        var table = new FlowTable(() => clock.Now);
        table.Set(Flow(1, 6), "wifi");
        table.Set(Flow(2, 17), "wifi");

        clock.Now = clock.Now.AddSeconds(31);
        Assert.Equal(1, table.RemoveExpired());
        Assert.True(table.TryGet(Flow(1, 6), out _));
        Assert.False(table.TryGet(Flow(2, 17), out _));

        clock.Now = clock.Now.AddSeconds(61);
        Assert.Equal(1, table.RemoveExpired());
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void A_fault_stops_the_steerer_and_is_reported()
    {
        var (c, _, steerer) = Make(() => []);

        steerer.Fault("driver crashed");

        Assert.True(steerer.Stopped);
        Assert.Equal("driver crashed", c.LastFault);
    }
}
