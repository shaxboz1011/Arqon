using Arqon.Core.Guard;

namespace Arqon.Core.Tests.Guard;

public class LatencyGuardTests
{
    private const double Fast = 10_000_000;   // 10 MB/s

    private static LatencyGuard Warm(bool active = true)
    {
        var g = new LatencyGuard();
        for (var i = 0; i < 30; i++) g.Update(20 + i % 3, 0, 2_000_000, active);
        return g;
    }

    [Fact]
    public void Does_nothing_while_the_network_is_calm()
    {
        var g = Warm();

        for (var i = 0; i < 20; i++) Assert.Null(g.Update(21, 0, Fast, true));
    }

    [Fact]
    public void Does_nothing_before_it_has_learned_the_normal_latency()
    {
        var g = new LatencyGuard();

        Assert.Null(g.Update(500, 0, Fast, true));
    }

    [Fact]
    public void A_latency_jump_while_a_game_runs_caps_the_download_below_its_current_speed()
    {
        var g = Warm();

        var limit = g.Update(150, 0, Fast, true);

        Assert.NotNull(limit);
        Assert.InRange(limit!.Value, 256 * 1024, (long)(Fast * 0.8));
    }

    [Fact]
    public void Packet_loss_alone_also_triggers_the_cap()
    {
        var g = Warm();

        Assert.NotNull(g.Update(21, 0.10, Fast, true));
    }

    [Fact]
    public void Keeps_backing_off_while_it_stays_bad_but_never_to_zero()
    {
        var g = Warm();
        long? limit = null;
        long previous = long.MaxValue;

        for (var i = 0; i < 40; i++)
        {
            limit = g.Update(200, 0, Fast, true);
            Assert.True(limit <= previous);
            previous = limit!.Value;
        }

        Assert.Equal(256 * 1024, limit);
    }

    [Fact]
    public void Gives_bandwidth_back_gradually_once_latency_recovers()
    {
        var g = Warm();
        var capped = g.Update(150, 0, Fast, true)!.Value;

        long? afterRecovery = null;
        for (var i = 0; i < 6; i++) afterRecovery = g.Update(21, 0, capped, true);

        Assert.True(afterRecovery is null || afterRecovery > capped);
    }

    [Fact]
    public void Releases_the_cap_entirely_after_a_long_calm_period()
    {
        var g = Warm();
        g.Update(150, 0, Fast, true);

        long? limit = 1;
        for (var i = 0; i < 200 && limit is not null; i++) limit = g.Update(21, 0, Fast, true);

        Assert.Null(limit);
    }

    [Fact]
    public void Releases_at_once_when_the_game_is_no_longer_on_this_link()
    {
        var g = Warm();
        Assert.NotNull(g.Update(150, 0, Fast, true));

        Assert.Null(g.Update(150, 0, Fast, false));
        Assert.Null(g.Limit);
    }

    [Fact]
    public void Never_caps_when_no_game_is_running()
    {
        var g = Warm(active: false);

        Assert.Null(g.Update(300, 0.2, Fast, false));
    }

    [Fact]
    public void A_dead_link_with_no_replies_does_not_count_as_a_latency_sample()
    {
        var g = Warm();

        // latency 0 with total loss: the probe failed, there is no round-trip time to learn from
        g.Update(0, 1.0, 0, true);
        Assert.Null(g.Update(21, 0, Fast, true));
    }
}
