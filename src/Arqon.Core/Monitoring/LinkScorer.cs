namespace Arqon.Core.Monitoring;

public static class LinkScorer
{
    private const double Dead = 1_000_000;

    /// <summary>Lower is better. Latency plus penalties for jitter and packet loss.</summary>
    public static double Score(LinkSample s) =>
        s.Loss >= 1 ? Dead : s.LatencyMs + 4 * s.JitterMs + 200 * s.Loss;
}
