using System.Collections.Concurrent;
using System.Diagnostics;
using Arqon.Core.Links;

namespace Arqon.Core.Speed;

/// <summary>What a speed test measured: bytes per second for each link (by link id), their sum, and the links that failed.</summary>
public sealed record SpeedTestResult(IReadOnlyDictionary<string, double> BytesPerSecond, IReadOnlyDictionary<string, string> Errors)
{
    public double Total => BytesPerSecond.Values.Sum();

    public double Mbit(string linkId) => BytesPerSecond.TryGetValue(linkId, out var v) ? v * 8 / 1_000_000 : 0;

    public double TotalMbit => Total * 8 / 1_000_000;
}

/// <summary>
/// Measures how fast links really are by downloading a test file through them for a few seconds. Run on one link it gives that
/// link's speed; run on all links at once it gives what combining them gives, so the gain is a measured number, not a promise.
/// </summary>
public sealed class LinkSpeedTest(Func<LinkInfo, HttpClient> clientFor, string url, int connectionsPerLink = 3)
{
    public const string DefaultUrl = "https://speed.cloudflare.com/__down?bytes=100000000";

    /// <param name="links">The links to load at the same time.</param>
    /// <param name="duration">How long to download.</param>
    public async Task<SpeedTestResult> RunAsync(IReadOnlyList<LinkInfo> links, TimeSpan duration, CancellationToken cancel = default)
    {
        var counters = links.ToDictionary(l => l.Id, _ => new long[1]);
        var errors = new ConcurrentDictionary<string, string>();
        var clock = Stopwatch.StartNew();

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        stop.CancelAfter(duration);

        var workers = new List<Task>();
        foreach (var link in links)
        {
            var client = clientFor(link);
            for (var i = 0; i < connectionsPerLink; i++)
            {
                workers.Add(Task.Run(() => Pull(link, client, counters[link.Id], errors, stop.Token)));
            }
        }
        await Task.WhenAll(workers).ConfigureAwait(false);

        var seconds = Math.Max(0.1, clock.Elapsed.TotalSeconds);
        var speeds = links.ToDictionary(l => l.Id, l => Interlocked.Read(ref counters[l.Id][0]) / seconds);
        // a link that failed is reported by its error, and only counts as zero speed when it also moved no data
        return new SpeedTestResult(speeds, errors.Where(e => speeds[e.Key] <= 0).ToDictionary(e => e.Key, e => e.Value));
    }

    private async Task Pull(LinkInfo link, HttpClient client, long[] counter, ConcurrentDictionary<string, string> errors, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                int n;
                while ((n = await body.ReadAsync(buffer, token).ConfigureAwait(false)) > 0) Interlocked.Add(ref counter[0], n);
            }
        }
        catch (OperationCanceledException)
        {
            // the time is up: that is how a test ends
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Net.Sockets.SocketException)
        {
            errors[link.Id] = ex.Message;
        }
    }
}
