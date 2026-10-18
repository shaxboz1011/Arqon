namespace Arqon.Core.Guard;

/// <summary>
/// Keeps downloads from inflating the ping of the link a game uses (bufferbloat). Learns the link's normal latency,
/// and while a game is on the link it caps the download speed whenever latency or loss rises, then gives the
/// bandwidth back step by step once things calm down. Pure logic: feed it one sample per tick.
/// </summary>
public sealed class LatencyGuard
{
    private const int Window = 120;
    private const int MinSamples = 10;
    private const double MinRiseMs = 25;
    private const double RiseFactor = 0.5;
    private const double LossLimit = 0.02;
    private const double DecreaseFactor = 0.75;
    private const double RaiseFactor = 1.2;
    private const long RaiseStepBps = 32 * 1024;
    private const long FloorBps = 256 * 1024;
    private const int CalmTicksToRaise = 3;

    private readonly Queue<double> _latencies = new();
    private int _calm;
    private double _peakBps;

    /// <summary>Current download cap in bytes per second; null when downloads are unrestricted.</summary>
    public long? Limit { get; private set; }

    /// <param name="latencyMs">Average round-trip time of the link; ignored when the probe got no answer.</param>
    /// <param name="loss">Fraction of probes lost, 0 to 1.</param>
    /// <param name="downBps">Measured receive speed of the link right now.</param>
    /// <param name="active">True while a game is using this link.</param>
    public long? Update(double latencyMs, double loss, double downBps, bool active)
    {
        if (latencyMs > 0 && loss < 1)
        {
            _latencies.Enqueue(latencyMs);
            while (_latencies.Count > Window) _latencies.Dequeue();
        }

        if (!active)
        {
            Reset();
            return null;
        }
        if (_latencies.Count < MinSamples || loss >= 1) return Limit;   // a dead link has no ping to protect

        var baseline = Baseline();
        var congested = latencyMs > baseline + Math.Max(MinRiseMs, baseline * RiseFactor) || loss > LossLimit;

        if (congested)
        {
            _calm = 0;
            if (Limit is null) _peakBps = Math.Max(downBps, FloorBps);
            var basis = Limit ?? Math.Max(downBps, FloorBps);
            Limit = Math.Max(FloorBps, (long)(basis * DecreaseFactor));
        }
        else if (Limit is { } current && ++_calm >= CalmTicksToRaise)
        {
            _calm = 0;
            var raised = (long)(current * RaiseFactor) + RaiseStepBps;
            if (raised >= _peakBps) Reset();
            else Limit = raised;
        }
        return Limit;
    }

    /// <summary>The lower decile of recent latency: what the link does when nothing is queued.</summary>
    private double Baseline()
    {
        var sorted = _latencies.Order().ToArray();
        return sorted[sorted.Length / 10];
    }

    private void Reset()
    {
        Limit = null;
        _calm = 0;
        _peakBps = 0;
    }
}
