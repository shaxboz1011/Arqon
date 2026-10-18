using Arqon.Core;

namespace Arqon.Core.Tests.Support;

public class TtlCacheTests
{
    private sealed class Clock
    {
        public DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public void Reads_the_source_once_within_the_lifetime()
    {
        var clock = new Clock();
        var calls = 0;
        var cache = new TtlCache<int>(() => ++calls, TimeSpan.FromSeconds(2), () => clock.Now);

        Assert.Equal(1, cache.Get());
        clock.Now += TimeSpan.FromSeconds(1);
        Assert.Equal(1, cache.Get());
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Reads_again_after_the_lifetime_ends()
    {
        var clock = new Clock();
        var calls = 0;
        var cache = new TtlCache<int>(() => ++calls, TimeSpan.FromSeconds(2), () => clock.Now);
        cache.Get();

        clock.Now += TimeSpan.FromSeconds(3);

        Assert.Equal(2, cache.Get());
    }

    [Fact]
    public void A_failing_source_returns_the_last_good_value()
    {
        var clock = new Clock();
        var fail = false;
        var cache = new TtlCache<string>(() => fail ? throw new InvalidOperationException() : "ok", TimeSpan.FromSeconds(1), () => clock.Now);
        cache.Get();

        fail = true;
        clock.Now += TimeSpan.FromSeconds(5);

        Assert.Equal("ok", cache.Get());
    }

    [Fact]
    public void A_failing_source_with_no_earlier_value_returns_default()
    {
        var cache = new TtlCache<string?>(() => throw new InvalidOperationException(), TimeSpan.FromSeconds(1));

        Assert.Null(cache.Get());
    }

    [Fact]
    public void Concurrent_readers_do_not_read_the_source_more_than_once_per_lifetime()
    {
        var calls = 0;
        var cache = new TtlCache<int>(() => { Thread.Sleep(20); return Interlocked.Increment(ref calls); }, TimeSpan.FromMinutes(1));

        Parallel.For(0, 16, _ => cache.Get());

        Assert.Equal(1, calls);
    }
}
