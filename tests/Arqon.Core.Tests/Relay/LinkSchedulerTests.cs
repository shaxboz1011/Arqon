using Arqon.Core.Relay;

namespace Arqon.Core.Tests.Relay;

public class LinkSchedulerTests
{
    private static readonly RelayLinkState Fast = new(0, 10_000_000, 20, true);
    private static readonly RelayLinkState Slow = new(1, 2_500_000, 60, true);

    [Fact]
    public void A_faster_link_carries_more_but_the_slower_one_still_helps()
    {
        var s = new LinkScheduler();
        var counts = new int[2];
        for (var i = 0; i < 1000; i++) counts[s.Pick([Fast, Slow], 1400)!.Value]++;

        Assert.InRange(counts[0], 750, 850);   // about 10 / (10 + 2.5) = 80%
        Assert.True(counts[1] > 100);
    }

    [Fact]
    public void Links_that_are_down_or_have_no_rate_are_never_picked()
    {
        var s = new LinkScheduler();

        Assert.Equal((byte)1, s.Pick([Fast with { Up = false }, Slow], 1400));
        Assert.Null(s.Pick([Fast with { Up = false }, Slow with { RateBps = 0 }], 1400));
    }

    [Fact]
    public void Queues_shrink_with_time_so_the_low_delay_link_is_preferred_again()
    {
        var s = new LinkScheduler();
        for (var i = 0; i < 50; i++) s.Pick([Fast, Slow], 1400);

        s.Drain([Fast, Slow], TimeSpan.FromSeconds(10));

        Assert.Equal((byte)0, s.Pick([Fast, Slow], 1400));
    }

    [Fact]
    public void A_game_packet_goes_over_every_link_that_is_up()
    {
        Assert.Equal([0], LinkScheduler.All([Fast, Slow with { Up = false }]));
        Assert.Equal([0, 1], LinkScheduler.All([Fast, Slow]));
    }
}
