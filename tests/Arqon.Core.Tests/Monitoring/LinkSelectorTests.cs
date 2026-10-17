using Arqon.Core.Monitoring;

namespace Arqon.Core.Tests.Monitoring;

public class LinkSelectorTests
{
    private static LinkSample S(string id, double latency, double jitter = 0, double loss = 0) =>
        new(id, DateTimeOffset.UnixEpoch, latency, jitter, loss, 0, 0);

    [Fact]
    public void Score_penalises_jitter_and_loss()
    {
        Assert.Equal(22, LinkScorer.Score(S("a", 22)), 3);
        Assert.Equal(20 + 4 * 5 + 200 * 0.1, LinkScorer.Score(S("a", 20, 5, 0.1)), 3);
    }

    [Fact]
    public void Picks_lowest_score_link()
    {
        Assert.Equal("wifi", LinkSelector.PickBestForGame([S("wifi", 22), S("phone", 40)], null));
    }

    [Fact]
    public void Stays_on_current_link_unless_best_is_at_least_20_percent_better()
    {
        // phone 40 vs wifi 36: only 10 % better -> stay
        Assert.Equal("phone", LinkSelector.PickBestForGame([S("wifi", 36), S("phone", 40)], "phone"));
        // phone 40 vs wifi 30: 25 % better -> switch
        Assert.Equal("wifi", LinkSelector.PickBestForGame([S("wifi", 30), S("phone", 40)], "phone"));
    }

    [Fact]
    public void Single_sample_returns_that_link_and_empty_returns_null()
    {
        Assert.Equal("phone", LinkSelector.PickBestForGame([S("phone", 80)], null));
        Assert.Null(LinkSelector.PickBestForGame([], null));
    }

    [Fact]
    public void Ignores_current_when_it_is_no_longer_present()
    {
        Assert.Equal("wifi", LinkSelector.PickBestForGame([S("wifi", 30)], "gone"));
    }
}
