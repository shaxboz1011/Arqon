using System.Collections.Concurrent;
using Arqon.Core.Download;
using Arqon.Core.Monitoring;

namespace Arqon.Core.Guard;

/// <summary>
/// Applies <see cref="LatencyGuard"/> to every link: only the link a game is using is ever capped, so the other
/// link keeps downloading at full speed.
/// </summary>
public sealed class GameGuard(LinkThrottle throttle, Func<bool> gameRunning)
{
    private readonly ConcurrentDictionary<string, LatencyGuard> _guards = new();

    /// <summary>Master switch (the "game protection" setting). Switching off lifts every cap on the next sample.</summary>
    public bool Enabled { get; set; } = true;

    public bool IsLimiting(string linkId) => _guards.TryGetValue(linkId, out var g) && g.Limit is not null;

    public long? LimitFor(string linkId) => _guards.TryGetValue(linkId, out var g) ? g.Limit : null;

    public void OnSample(LinkSample sample, bool isGameLink)
    {
        var guard = _guards.GetOrAdd(sample.LinkId, _ => new LatencyGuard());
        var active = Enabled && isGameLink && gameRunning();
        throttle.Set(sample.LinkId, guard.Update(sample.LatencyMs, sample.Loss, sample.DownBps, active));
    }
}
