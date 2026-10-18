using Arqon.Core.Links;

namespace Arqon.Core.Torrents;

/// <summary>
/// Hands out the links in turn, one per new peer connection, so a torrent spreads its many connections over every
/// network. A link that looks down is skipped while another one works.
/// </summary>
public sealed class LinkRoundRobin(Func<IReadOnlyList<LinkInfo>> links, Func<LinkInfo, bool> isAlive)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _counts = new();
    private int _turn;

    /// <summary>The link for the next connection, or null when there are no links (Windows then picks the route).</summary>
    public LinkInfo? Next()
    {
        var all = links();
        if (all.Count == 0) return null;
        var usable = all.Where(isAlive).ToList();
        if (usable.Count == 0) usable = all.ToList();   // better a doubtful link than no connection at all

        lock (_gate)
        {
            var link = usable[_turn++ % usable.Count];
            _counts[link.Id] = _counts.GetValueOrDefault(link.Id) + 1;
            return link;
        }
    }

    /// <summary>How many connections each link received so far.</summary>
    public IReadOnlyDictionary<string, int> Counts
    {
        get { lock (_gate) return new Dictionary<string, int>(_counts); }
    }
}
