using Arqon.Core.Usage;

namespace Arqon.Core.Tests.Usage;

public class DailyLimitTests
{
    private static readonly DateOnly Monday = new(2026, 10, 19);
    private const long Gb = 1_000_000_000;

    [Theory]
    [InlineData(500, 0, LimitLevel.None)]
    [InlineData(500, -1, LimitLevel.None)]
    [InlineData(0, 1000, LimitLevel.Ok)]
    [InlineData(799, 1000, LimitLevel.Ok)]
    [InlineData(800, 1000, LimitLevel.Near)]
    [InlineData(999, 1000, LimitLevel.Near)]
    [InlineData(1000, 1000, LimitLevel.Reached)]
    [InlineData(5000, 1000, LimitLevel.Reached)]
    public void The_level_follows_the_share_of_the_limit_used(long used, long limit, LimitLevel level)
    {
        Assert.Equal(level, DailyLimit.LevelOf(used, limit));
    }

    [Fact]
    public void Nothing_is_said_below_eighty_percent_or_without_a_limit()
    {
        var limit = new DailyLimit();

        Assert.Null(limit.Check(Monday, 1 * Gb, 2 * Gb));
        Assert.Null(limit.Check(Monday, 100 * Gb, 0));
    }

    [Fact]
    public void Each_warning_is_given_once_a_day()
    {
        var limit = new DailyLimit();

        Assert.Equal(LimitLevel.Near, limit.Check(Monday, 17 * Gb / 10, 2 * Gb));
        Assert.Null(limit.Check(Monday, 18 * Gb / 10, 2 * Gb));
        Assert.Equal(LimitLevel.Reached, limit.Check(Monday, 2 * Gb, 2 * Gb));
        Assert.Null(limit.Check(Monday, 3 * Gb, 2 * Gb));
    }

    [Fact]
    public void Jumping_straight_past_the_limit_gives_only_the_reached_warning()
    {
        var limit = new DailyLimit();

        Assert.Equal(LimitLevel.Reached, limit.Check(Monday, 3 * Gb, 2 * Gb));
        Assert.Null(limit.Check(Monday, 3 * Gb, 2 * Gb));
    }

    [Fact]
    public void A_new_day_starts_again()
    {
        var limit = new DailyLimit();
        limit.Check(Monday, 2 * Gb, 2 * Gb);

        Assert.Equal(LimitLevel.Near, limit.Check(Monday.AddDays(1), 17 * Gb / 10, 2 * Gb));
    }

    [Fact]
    public void Raising_the_limit_lets_the_warnings_come_again_only_for_higher_levels()
    {
        var limit = new DailyLimit();
        limit.Check(Monday, 2 * Gb, 2 * Gb);   // reached

        Assert.Null(limit.Check(Monday, 2 * Gb, 5 * Gb));   // now fine: nothing to say
        Assert.Null(limit.Check(Monday, 41 * Gb / 10, 5 * Gb));   // near the new limit, but "near" was already below what was told today
    }
}
