namespace Arqon.Core.Relay;

/// <summary>What the scheduler knows about one link of the tunnel.</summary>
public sealed record RelayLinkState(byte Index, double RateBps, double RttMs, bool Up);

/// <summary>
/// Picks the link for each piece so that pieces arrive as close to in order as possible: the one that would deliver it first,
/// counting the link's delay and the bytes already queued on it. A fast link therefore carries more, and a slow one still helps
/// without holding the stream back. Game packets go out on every link that is up.
/// </summary>
public sealed class LinkScheduler
{
    private readonly Dictionary<byte, double> _queuedBytes = new();

    /// <summary>The link for a piece of <paramref name="size"/> bytes, or null when no link is up.</summary>
    public byte? Pick(IReadOnlyList<RelayLinkState> links, int size)
    {
        RelayLinkState? best = null;
        var bestArrival = double.MaxValue;
        foreach (var link in links)
        {
            if (!link.Up || link.RateBps <= 0) continue;
            var queued = _queuedBytes.GetValueOrDefault(link.Index);
            var arrivalMs = link.RttMs / 2 + (queued + size) / link.RateBps * 1000;
            if (arrivalMs < bestArrival)
            {
                bestArrival = arrivalMs;
                best = link;
            }
        }
        if (best is null) return null;
        _queuedBytes[best.Index] = _queuedBytes.GetValueOrDefault(best.Index) + size;
        return best.Index;
    }

    /// <summary>Every link that is up, for a duplicated game packet.</summary>
    public static IReadOnlyList<byte> All(IReadOnlyList<RelayLinkState> links) => links.Where(l => l.Up).Select(l => l.Index).ToList();

    /// <summary>Time passed: each link has sent what its rate allows, so its queue shrinks.</summary>
    public void Drain(IReadOnlyList<RelayLinkState> links, TimeSpan elapsed)
    {
        foreach (var link in links)
            if (_queuedBytes.TryGetValue(link.Index, out var queued))
                _queuedBytes[link.Index] = Math.Max(0, queued - link.RateBps * elapsed.TotalSeconds);
    }
}
