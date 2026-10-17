using System.Diagnostics;

namespace Arqon.Core.Download;

/// <summary>Token bucket shared by all workers of a job. Bursts are limited to about 100 ms of traffic.</summary>
public sealed class RateLimiter
{
    private readonly double _rate;
    private readonly double _capacity;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private double _tokens;
    private double _lastSeconds;

    public RateLimiter(long bytesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bytesPerSecond, 0);
        _rate = bytesPerSecond;
        _capacity = Math.Max(64 * 1024, bytesPerSecond / 10.0);
        _tokens = _capacity;
    }

    public async ValueTask AcquireAsync(int bytes, CancellationToken ct)
    {
        double waitSeconds;
        lock (_gate)
        {
            var now = _clock.Elapsed.TotalSeconds;
            _tokens = Math.Min(_capacity, _tokens + (now - _lastSeconds) * _rate);
            _lastSeconds = now;
            _tokens -= bytes;
            waitSeconds = _tokens < 0 ? -_tokens / _rate : 0;
        }
        if (waitSeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(waitSeconds), ct).ConfigureAwait(false);
    }
}
