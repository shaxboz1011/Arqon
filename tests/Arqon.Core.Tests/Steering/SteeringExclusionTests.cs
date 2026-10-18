using System.Net;
using Arqon.Core.Monitoring;
using Arqon.Core.Policy;
using Arqon.Core.Settings;
using Arqon.Core.Steering;

namespace Arqon.Core.Tests.Steering;

public class SteeringExclusionTests
{
    private sealed class NoSteerer : ISteerer
    {
        public bool IsAvailable => true;
        public void Start() { }
        public void Stop() { }
        public event Action<string>? Faulted { add { } remove { } }
    }

    private static LinkSample S(string id, double latency) =>
        new(id, DateTimeOffset.UnixEpoch, latency, 0, 0, 1_000_000, 0);

    private static FlowKey Flow(int i) =>
        new(6, IPAddress.Parse("192.168.1.20"), (ushort)(40000 + i), IPAddress.Parse("93.184.216.34"), 443);

    private static (SteeringCoordinator C, Func<DateTimeOffset> Now, Action<TimeSpan> Advance) Make()
    {
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        var c = new SteeringCoordinator(new NoSteerer(), new FlowTable(() => now), new RuleEngine(ArqonSettings.Default()),
            () => [S("wifi", 20), S("phone", 30)], () => now);
        return (c, () => now, d => now += d);
    }

    [Fact]
    public void An_excluded_link_gets_no_new_flows()
    {
        var (c, _, _) = Make();
        c.Exclude("phone", TimeSpan.FromSeconds(60));

        for (var i = 0; i < 50; i++) Assert.Equal("wifi", c.PickLink(Flow(i), "chrome.exe"));
    }

    [Fact]
    public void An_excluded_link_is_not_chosen_for_games_either()
    {
        var (c, _, _) = Make();
        c.Exclude("wifi", TimeSpan.FromSeconds(60));

        Assert.Equal("phone", c.PickLink(Flow(1), "valorant.exe"));
    }

    [Fact]
    public void An_excluded_link_comes_back_after_the_exclusion_ends()
    {
        var (c, _, advance) = Make();
        c.Exclude("phone", TimeSpan.FromSeconds(60));
        advance(TimeSpan.FromSeconds(61));

        var links = Enumerable.Range(0, 50).Select(i => c.PickLink(Flow(i), "chrome.exe")).ToHashSet();

        Assert.Contains("phone", links);
    }

    [Fact]
    public void Excluding_every_link_leaves_routing_to_windows()
    {
        var (c, _, _) = Make();
        c.Exclude("wifi", TimeSpan.FromSeconds(60));
        c.Exclude("phone", TimeSpan.FromSeconds(60));

        Assert.Null(c.PickLink(Flow(1), "chrome.exe"));
    }
}
