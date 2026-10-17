namespace Arqon.Core.Monitoring;

/// <summary>One measurement of a link. DownBps/UpBps are bytes per second.</summary>
public sealed record LinkSample(
    string LinkId,
    DateTimeOffset At,
    double LatencyMs,
    double JitterMs,
    double Loss,
    double DownBps,
    double UpBps);
