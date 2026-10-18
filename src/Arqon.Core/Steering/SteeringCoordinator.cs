using Arqon.Core.Monitoring;
using Arqon.Core.Policy;

namespace Arqon.Core.Steering;

/// <summary>The packet-level component that actually moves traffic between adapters.</summary>
public interface ISteerer
{
    /// <summary>False when the driver is missing or the process lacks administrator rights. Never throws.</summary>
    bool IsAvailable { get; }

    /// <summary>Short machine-readable cause when <see cref="IsAvailable"/> is false (e.g. "driver-missing", "not-admin").</summary>
    string? UnavailableReason => null;

    void Start();
    void Stop();

    /// <summary>Raised on any failure; the coordinator then stops steering so Windows routes normally again.</summary>
    event Action<string>? Faulted;
}

/// <summary>Decides which link a new flow should use. The steerer applies the answer; flows already decided never move.</summary>
public sealed class SteeringCoordinator
{
    private const double MinWeight = 1;

    /// <summary>Every usable link weighs at least this fraction of the fastest one, so an idle link keeps being measured.</summary>
    private const double ExploreShare = 0.25;

    private static readonly TimeSpan PeakHalfLife = TimeSpan.FromSeconds(120);
    private readonly Dictionary<string, (double Bps, DateTimeOffset At)> _peak = new();

    private readonly ISteerer? _steerer;
    private readonly FlowTable _table;
    private readonly Func<RuleEngine> _rules;
    private readonly Func<IReadOnlyList<LinkSample>> _samples;
    private readonly Dictionary<string, double> _credit = new();
    private readonly Dictionary<string, DateTimeOffset> _excluded = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private string? _gameLink;

    public SteeringCoordinator(ISteerer? steerer, FlowTable table, RuleEngine rules, Func<IReadOnlyList<LinkSample>> samples,
        Func<DateTimeOffset>? clock = null)
        : this(steerer, table, () => rules, samples, clock)
    {
    }

    /// <param name="steerer">Optional: when given, a fault stops it. Null when something else (e.g. SteeringService) owns that.</param>
    /// <param name="rules">Asked for every new flow, so rule changes in the settings apply immediately.</param>
    public SteeringCoordinator(ISteerer? steerer, FlowTable table, Func<RuleEngine> rules, Func<IReadOnlyList<LinkSample>> samples,
        Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _steerer = steerer;
        _table = table;
        _rules = rules;
        _samples = samples;
        if (_steerer is not null)
            _steerer.Faulted += why =>
            {
                LastFault = why;
                _steerer.Stop();
            };
    }

    public string? LastFault { get; private set; }

    /// <summary>Flows remembered right now (for tests and diagnostics).</summary>
    public int TrackedFlows => _table.Count;

    /// <summary>Forgets idle flows; call periodically or the table grows without bound. Returns how many were removed.</summary>
    public int Cleanup() => _table.RemoveExpired();

    /// <summary>Link id for this flow, or null to leave it to normal Windows routing (no measurements yet).</summary>
    public string? PickLink(FlowKey key, string exeName)
    {
        var rule = _rules().Resolve(exeName);

        // One lock around lookup + decision + store: two packets of the same new flow must get the same link.
        lock (_gate)
        {
            var samples = Usable(_samples());
            if (_table.TryGet(key, out var existing) && (samples.Count == 0 || samples.Any(s => s.LinkId == existing)))
                return existing;
            if (samples.Count == 0) return null;

            // A new flow, or one whose link has disappeared: decide again.
            var link = rule switch
            {
                { Choice: LinkChoice.Only } when rule.LinkId is { } id && samples.Any(s => s.LinkId == id) => id,
                { Choice: LinkChoice.Only } => BestForGame(samples),
                { Kind: AppKind.Game } => BestForGame(samples),
                _ => WeightedRoundRobin(samples),
            };

            if (link is not null) _table.Set(key, link);
            return link;
        }
    }

    /// <summary>Keeps a link out of new decisions for a while, e.g. when it stopped answering steered connections.</summary>
    public void Exclude(string linkId, TimeSpan duration)
    {
        lock (_gate) _excluded[linkId] = _clock() + duration;
    }

    private IReadOnlyList<LinkSample> Usable(IReadOnlyList<LinkSample> samples)
    {
        // A link whose probes all fail (e.g. a phone without mobile data) would stall every flow sent to it.
        // If every link looks dead, keep them all rather than leave the machine without any decision.
        var alive = samples.Where(s => s.Loss < 1).ToList();
        if (alive.Count > 0 && alive.Count < samples.Count) samples = alive;

        if (_excluded.Count == 0) return samples;

        var now = _clock();
        foreach (var (id, until) in _excluded.ToList())
            if (until <= now) _excluded.Remove(id);
        return _excluded.Count == 0 ? samples : samples.Where(s => !_excluded.ContainsKey(s.LinkId)).ToList();
    }

    private string? BestForGame(IReadOnlyList<LinkSample> samples) =>
        _gameLink = LinkSelector.PickBestForGame(samples, _gameLink);

    /// <summary>
    /// Smooth weighted round-robin: each link gets new flows in proportion to the best speed it showed recently.
    /// Weighting by the current speed would starve an idle link for good (it carries nothing, so it never measures
    /// any speed), hence the fading peak and the exploration share.
    /// </summary>
    private string WeightedRoundRobin(IReadOnlyList<LinkSample> samples)
    {
        var now = _clock();
        var weights = new double[samples.Count];
        for (var i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            var peak = s.DownBps;
            if (_peak.TryGetValue(s.LinkId, out var seen))
                peak = Math.Max(peak, seen.Bps * Math.Pow(0.5, (now - seen.At).TotalSeconds / PeakHalfLife.TotalSeconds));
            _peak[s.LinkId] = (peak, now);
            weights[i] = peak;
        }

        var floor = Math.Max(MinWeight, weights.Max() * ExploreShare);
        var total = 0.0;
        for (var i = 0; i < samples.Count; i++)
        {
            var w = Math.Max(floor, weights[i]);
            _credit[samples[i].LinkId] = _credit.GetValueOrDefault(samples[i].LinkId) + w;
            total += w;
        }

        var best = samples.MaxBy(s => _credit[s.LinkId])!;
        _credit[best.LinkId] -= total;
        return best.LinkId;
    }
}
