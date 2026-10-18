namespace Arqon.Core.Links;

public enum LinkChangeKind { Appeared, Lost }

public sealed record LinkChange(LinkChangeKind Kind, LinkInfo Link);

/// <summary>
/// Turns repeated link listings into "phone connected" / "phone disconnected" events. A link must be missing from two
/// listings in a row to count as lost, so a momentary adapter flicker does not raise a false alarm.
/// </summary>
public sealed class LinkChangeDetector
{
    private const int MissesBeforeLost = 2;

    private readonly Dictionary<string, LinkInfo> _known = new();
    private readonly Dictionary<string, int> _misses = new();
    private bool _started;

    public IReadOnlyList<LinkChange> Update(IReadOnlyList<LinkInfo> current)
    {
        var changes = new List<LinkChange>();
        var present = current.ToDictionary(l => l.Id);

        foreach (var link in current)
        {
            var isNew = !_known.ContainsKey(link.Id);
            _known[link.Id] = link;
            _misses.Remove(link.Id);
            if (isNew && _started) changes.Add(new LinkChange(LinkChangeKind.Appeared, link));
        }

        foreach (var (id, link) in _known.Where(k => !present.ContainsKey(k.Key)).ToList())
        {
            var misses = _misses.GetValueOrDefault(id) + 1;
            if (misses < MissesBeforeLost) { _misses[id] = misses; continue; }

            _known.Remove(id);
            _misses.Remove(id);
            changes.Add(new LinkChange(LinkChangeKind.Lost, link));
        }

        _started = true;
        return changes;
    }
}
