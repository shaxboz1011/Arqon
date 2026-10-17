namespace Arqon.Core.Monitoring;

public static class LinkSelector
{
    private const double SwitchMargin = 0.20;

    /// <summary>
    /// Best link for latency-sensitive traffic. Keeps <paramref name="current"/> unless another
    /// link scores at least 20 % better, so games do not flip between links on noise.
    /// </summary>
    public static string? PickBestForGame(IReadOnlyList<LinkSample> samples, string? current)
    {
        if (samples.Count == 0) return null;

        var best = samples.MinBy(LinkScorer.Score)!;
        var cur = current is null ? null : samples.FirstOrDefault(s => s.LinkId == current);
        if (cur is null || cur.LinkId == best.LinkId) return best.LinkId;

        return LinkScorer.Score(best) <= LinkScorer.Score(cur) * (1 - SwitchMargin) ? best.LinkId : cur.LinkId;
    }
}
