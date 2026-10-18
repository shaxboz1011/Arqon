using System.Net;
using Arqon.Core.Monitoring;
using Arqon.Core.Policy;
using Arqon.Core.Settings;
using Arqon.Core.Steering;

namespace Arqon.Core.Tests.Steering;

public class SteeringCoordinatorWiringTests
{
    private static LinkSample S(string id, double latency) =>
        new(id, DateTimeOffset.UnixEpoch, latency, 0, 0, 1_000_000, 0);

    private static FlowKey Flow(int i) =>
        new(6, IPAddress.Parse("192.168.1.20"), (ushort)(40000 + i), IPAddress.Parse("93.184.216.34"), 443);

    [Fact]
    public void Works_without_a_steerer_so_it_can_be_built_before_the_driver_layer()
    {
        var c = new SteeringCoordinator(null, new FlowTable(() => DateTimeOffset.UtcNow),
            new RuleEngine(ArqonSettings.Default()), () => [S("wifi", 20)]);

        Assert.Equal("wifi", c.PickLink(Flow(1), "chrome.exe"));
    }

    [Fact]
    public void Rule_changes_apply_to_the_next_new_flow()
    {
        var settings = ArqonSettings.Default();
        var c = new SteeringCoordinator(null, new FlowTable(() => DateTimeOffset.UtcNow),
            () => new RuleEngine(settings), () => [S("wifi", 20), S("phone", 30)]);

        settings = settings with { Rules = [new AppRule("chrome.exe", AppKind.Normal, LinkChoice.Only, "phone", null)] };

        Assert.Equal("phone", c.PickLink(Flow(1), "chrome.exe"));
    }
}
