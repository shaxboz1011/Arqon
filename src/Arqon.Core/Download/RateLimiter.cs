using System.Collections.Concurrent;
using System.Diagnostics;

namespace Arqon.Core.Download;

/// <summary>
/// Token bucket shared by all workers that use it. Bursts are limited to about 100 ms of traffic.
/// The limit can change at any time; null means unlimited.
/// </summary>
public sealed class RateLimiter
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private long? _limit;
    private double _capacity;
    private double _tokens;
    private double _lastSeconds;
    private CancellationTokenSource _changed = new();

    public RateLimiter(long? bytesPerSecond = null) => Limit = bytesPerSecond;

    public long? Limit
    {
        get { lock (_gate) return _limit; }
        set
        {
            if (value is <= 0) throw new ArgumentOutOfRangeException(nameof(value), "A limit must be positive, or null for unlimited");
            lock (_gate)
            {
                Refill();
                var wasUnlimited = _limit is null;
                _limit = value;
                _capacity = value is { } bps ? Math.Max(64 * 1024, bps / 10.0) : 0;
                _tokens = wasUnlimited ? _capacity : Math.Min(_tokens, _capacity);
                // Waits that were worked out for the old limit end now: lifting a cap gives the speed back at once.
                var old = _changed;
                _changed = new CancellationTokenSource();
                old.Cancel();
            }
        }
    }

    public ValueTask AcquireAsync(int bytes, CancellationToken ct)
    {
        double waitSeconds;
        CancellationToken changed;
        lock (_gate)
        {
            if (_limit is not { } rate) return ValueTask.CompletedTask;
            Refill();
            _tokens -= bytes;
            waitSeconds = _tokens < 0 ? -_tokens / rate : 0;
            changed = _changed.Token;
        }
        return waitSeconds > 0 ? WaitAsync(TimeSpan.FromSeconds(waitSeconds), changed, ct) : ValueTask.CompletedTask;
    }

    private static async ValueTask WaitAsync(TimeSpan wait, CancellationToken changed, CancellationToken ct)
    {
        using var either = CancellationTokenSource.CreateLinkedTokenSource(changed, ct);
        try
        {
            await Task.Delay(wait, either.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // the limit changed: go on (under a new limit the debt is still in the bucket and the next call waits for it)
        }
    }

    private void Refill()
    {
        var now = _clock.Elapsed.TotalSeconds;
        if (_limit is { } rate) _tokens = Math.Min(_capacity, _tokens + (now - _lastSeconds) * rate);
        _lastSeconds = now;
    }
}

/// <summary>One adjustable limiter per link, so a game can be protected on its link while downloads run flat out on the other.</summary>
public sealed class LinkThrottle
{
    private readonly ConcurrentDictionary<string, RateLimiter> _limiters = new();

    public RateLimiter For(string linkId) => _limiters.GetOrAdd(linkId, _ => new RateLimiter());

    public void Set(string linkId, long? bytesPerSecond) => For(linkId).Limit = bytesPerSecond;

    public long? Get(string linkId) => _limiters.TryGetValue(linkId, out var l) ? l.Limit : null;
}
