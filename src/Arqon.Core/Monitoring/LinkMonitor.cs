using System.Collections.Concurrent;
using Arqon.Core.Links;

namespace Arqon.Core.Monitoring;

/// <summary>Periodically probes every link and publishes latency, jitter, loss and throughput.</summary>
public sealed class LinkMonitor
{
    private const int ProbeWindow = 20;
    private const int HistoryLength = 120;

    private sealed class State
    {
        public readonly Queue<double?> Probes = new();
        public readonly List<LinkSample> History = new();
        public (long Rx, long Tx, DateTimeOffset At)? LastCounters;
    }

    private readonly ILinkProvider _provider;
    private readonly IProbe _probe;
    private readonly ICounterReader _counters;
    private readonly TimeSpan _interval;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<string, State> _states = new();
    private readonly ConcurrentDictionary<string, LinkSample> _latest = new();
    private CancellationTokenSource? _cts;

    public LinkMonitor(ILinkProvider provider, IProbe probe, ICounterReader counters, TimeSpan interval,
        Func<DateTimeOffset>? clock = null)
    {
        _provider = provider;
        _probe = probe;
        _counters = counters;
        _interval = interval;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public event Action<LinkSample>? SampleReady;

    public IReadOnlyDictionary<string, LinkSample> Latest => _latest;

    public IReadOnlyList<LinkSample> History(string linkId)
    {
        if (!_states.TryGetValue(linkId, out var st)) return [];
        lock (st) return st.History.ToList();
    }

    public async Task TickAsync(CancellationToken ct)
    {
        var links = _provider.GetLinks();
        var ids = links.Select(l => l.Id).ToHashSet();
        foreach (var gone in _states.Keys.Where(k => !ids.Contains(k)).ToList())
        {
            _states.TryRemove(gone, out _);
            _latest.TryRemove(gone, out _);
        }

        var rtts = await Task.WhenAll(links.Select(async l =>
        {
            try { return await _probe.ProbeAsync(l, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        })).ConfigureAwait(false);

        for (var i = 0; i < links.Count; i++)
        {
            var sample = BuildSample(links[i], rtts[i]);
            _latest[sample.LinkId] = sample;
            Publish(sample);
        }
    }

    /// <summary>One misbehaving subscriber must not stop the monitor or starve the other subscribers.</summary>
    private void Publish(LinkSample sample)
    {
        if (SampleReady is null) return;
        foreach (var handler in SampleReady.GetInvocationList().Cast<Action<LinkSample>>())
        {
            try { handler(sample); }
            catch (Exception) { /* subscriber's problem */ }
        }
    }

    private LinkSample BuildSample(LinkInfo link, double? rtt)
    {
        var st = _states.GetOrAdd(link.Id, _ => new State());
        var now = _clock();

        // A link can vanish between listing and reading; then we simply have no throughput for this tick.
        (long Rx, long Tx)? reading = null;
        try { reading = _counters.Read(link); }
        catch (Exception ex) when (ex is not OperationCanceledException) { }

        lock (st)
        {
            st.Probes.Enqueue(rtt);
            while (st.Probes.Count > ProbeWindow) st.Probes.Dequeue();

            var ok = st.Probes.Where(p => p.HasValue).Select(p => p!.Value).ToList();
            var loss = 1.0 - (double)ok.Count / st.Probes.Count;
            var latency = ok.Count > 0 ? ok.Average() : 0;
            var jitter = 0.0;
            if (ok.Count > 1)
            {
                var sum = 0.0;
                for (var i = 1; i < ok.Count; i++) sum += Math.Abs(ok[i] - ok[i - 1]);
                jitter = sum / (ok.Count - 1);
            }

            double down = 0, up = 0;
            if (reading is { } r)
            {
                if (st.LastCounters is { } last)
                {
                    var seconds = (now - last.At).TotalSeconds;
                    if (seconds > 0)
                    {
                        down = Math.Max(0, r.Rx - last.Rx) / seconds;
                        up = Math.Max(0, r.Tx - last.Tx) / seconds;
                    }
                }
                st.LastCounters = (r.Rx, r.Tx, now);
            }

            var sample = new LinkSample(link.Id, now, latency, jitter, loss, down, up);
            st.History.Add(sample);
            if (st.History.Count > HistoryLength) st.History.RemoveAt(0);
            return sample;
        }
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(_interval);
            try
            {
                do
                {
                    try { await TickAsync(token).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // e.g. the adapter list failed once; keep monitoring, the next tick usually works
                    }
                }
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) { }
        });
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }
}
