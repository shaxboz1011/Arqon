using System.Diagnostics;
using Arqon.Core.Download;

namespace Arqon.Core.Tests.Download;

public class RateLimiterTests
{
    private static async Task<TimeSpan> Take(RateLimiter limiter, int chunks, int bytes)
    {
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < chunks; i++) await limiter.AcquireAsync(bytes, default);
        return sw.Elapsed;
    }

    [Fact]
    public async Task Unlimited_never_waits()
    {
        var limiter = new RateLimiter();

        Assert.True(await Take(limiter, 20, 10 * 1024 * 1024) < TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task A_limit_set_later_slows_the_next_reads()
    {
        var limiter = new RateLimiter();
        limiter.Limit = 256 * 1024;

        // 3 x 128 KiB against a 64 KiB burst allowance at 256 KiB/s is more than a second of waiting
        Assert.True(await Take(limiter, 3, 128 * 1024) >= TimeSpan.FromSeconds(0.9));
    }

    [Fact]
    public async Task Removing_the_limit_releases_immediately()
    {
        var limiter = new RateLimiter(64 * 1024);
        limiter.Limit = null;

        Assert.True(await Take(limiter, 10, 1024 * 1024) < TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task Raising_the_limit_speeds_up_the_next_reads()
    {
        var limiter = new RateLimiter(64 * 1024);
        limiter.Limit = 64L * 1024 * 1024;

        Assert.True(await Take(limiter, 8, 512 * 1024) < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void A_zero_or_negative_limit_is_rejected()
    {
        var limiter = new RateLimiter();

        Assert.Throws<ArgumentOutOfRangeException>(() => limiter.Limit = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => limiter.Limit = -5);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateLimiter(0));
    }

    [Fact]
    public void The_limit_can_be_read_back()
    {
        var limiter = new RateLimiter(1000) { Limit = 5000 };

        Assert.Equal(5000, limiter.Limit);
    }

    [Fact]
    public void Link_throttles_are_independent_and_created_on_demand()
    {
        var throttle = new LinkThrottle();
        throttle.Set("wifi", 100_000);

        Assert.Equal(100_000, throttle.Get("wifi"));
        Assert.Null(throttle.Get("phone"));
        Assert.Same(throttle.For("wifi"), throttle.For("wifi"));
        Assert.NotSame(throttle.For("wifi"), throttle.For("phone"));
    }
}
