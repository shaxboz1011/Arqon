using Arqon.Core.Download;
using Arqon.Core.Guard;
using Arqon.Core.Monitoring;

namespace Arqon.Core.Tests.Guard;

public class GameGuardTests
{
    private static LinkSample S(string id, double latency, double down = 10_000_000, double loss = 0) =>
        new(id, DateTimeOffset.UnixEpoch, latency, 0, loss, down, 0);

    private static void Warm(GameGuard guard)
    {
        for (var i = 0; i < 30; i++)
        {
            guard.OnSample(S("wifi", 20 + i % 3, 1_000_000), isGameLink: true);
            guard.OnSample(S("phone", 60 + i % 3, 1_000_000), isGameLink: false);
        }
    }

    [Fact]
    public void Caps_downloads_on_the_game_link_when_its_latency_jumps()
    {
        var throttle = new LinkThrottle();
        var guard = new GameGuard(throttle, () => true);
        Warm(guard);

        guard.OnSample(S("wifi", 160), isGameLink: true);

        Assert.NotNull(throttle.Get("wifi"));
        Assert.True(guard.IsLimiting("wifi"));
    }

    [Fact]
    public void Leaves_the_other_link_alone_even_if_it_is_slow()
    {
        var throttle = new LinkThrottle();
        var guard = new GameGuard(throttle, () => true);
        Warm(guard);

        guard.OnSample(S("phone", 400), isGameLink: false);

        Assert.Null(throttle.Get("phone"));
        Assert.False(guard.IsLimiting("phone"));
    }

    [Fact]
    public void Does_nothing_when_no_game_is_running()
    {
        var throttle = new LinkThrottle();
        var guard = new GameGuard(throttle, () => false);
        Warm(guard);

        guard.OnSample(S("wifi", 400), isGameLink: true);

        Assert.Null(throttle.Get("wifi"));
    }

    [Fact]
    public void Switching_it_off_removes_every_cap()
    {
        var throttle = new LinkThrottle();
        var guard = new GameGuard(throttle, () => true);
        Warm(guard);
        guard.OnSample(S("wifi", 160), isGameLink: true);

        guard.Enabled = false;
        guard.OnSample(S("wifi", 160), isGameLink: true);

        Assert.Null(throttle.Get("wifi"));
    }

    [Fact]
    public void The_cap_ends_when_the_game_closes()
    {
        var running = true;
        var throttle = new LinkThrottle();
        var guard = new GameGuard(throttle, () => running);
        Warm(guard);
        guard.OnSample(S("wifi", 160), isGameLink: true);

        running = false;
        guard.OnSample(S("wifi", 160), isGameLink: true);

        Assert.Null(throttle.Get("wifi"));
    }
}
