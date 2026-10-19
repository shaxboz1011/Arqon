using Arqon.Core.Torrents;

namespace Arqon.Core.Tests.Torrents;

public sealed class SeedPolicyTests
{
    [Fact]
    public void Seeding_goes_on_until_the_chosen_share_ratio_is_uploaded()
    {
        Assert.False(SeedPolicy.ReachedTarget(uploaded: 999, size: 1000, ratio: 1.0));
        Assert.True(SeedPolicy.ReachedTarget(uploaded: 1000, size: 1000, ratio: 1.0));
        Assert.True(SeedPolicy.ReachedTarget(uploaded: 2500, size: 1000, ratio: 2.0));
        Assert.False(SeedPolicy.ReachedTarget(uploaded: 1999, size: 1000, ratio: 2.0));
    }

    [Fact]
    public void A_ratio_of_zero_means_no_limit()
    {
        Assert.False(SeedPolicy.ReachedTarget(uploaded: long.MaxValue / 2, size: 1000, ratio: 0));
    }

    [Fact]
    public void An_empty_torrent_never_reaches_a_target_so_it_cannot_stop_by_accident()
    {
        Assert.False(SeedPolicy.ReachedTarget(uploaded: 10, size: 0, ratio: 1.0));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void A_nonsense_ratio_counts_as_no_limit(double ratio)
    {
        Assert.False(SeedPolicy.ReachedTarget(uploaded: 5000, size: 1000, ratio));
    }
}
