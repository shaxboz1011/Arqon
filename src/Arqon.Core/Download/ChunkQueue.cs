namespace Arqon.Core.Download;

/// <summary>Inclusive byte range [Start, End].</summary>
public readonly record struct ByteRange(long Start, long End)
{
    public long Length => End - Start + 1;
}

public static class ChunkPlan
{
    public static IReadOnlyList<ByteRange> Split(long length, long chunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(chunkSize, 0);

        var list = new List<ByteRange>();
        for (long start = 0; start < length; start += chunkSize)
            list.Add(new ByteRange(start, Math.Min(start + chunkSize, length) - 1));
        return list;
    }
}

/// <summary>Thread-safe work queue shared by every link's workers; faster links simply take more chunks.</summary>
public sealed class ChunkQueue
{
    private readonly LinkedList<ByteRange> _items;
    private readonly object _gate = new();

    public ChunkQueue(IEnumerable<ByteRange> ranges) => _items = new LinkedList<ByteRange>(ranges);

    public int Remaining
    {
        get { lock (_gate) return _items.Count; }
    }

    public bool TryTake(out ByteRange range)
    {
        lock (_gate)
        {
            if (_items.First is null)
            {
                range = default;
                return false;
            }
            range = _items.First.Value;
            _items.RemoveFirst();
            return true;
        }
    }

    /// <summary>Puts a range back at the front, e.g. after its link failed.</summary>
    public void Return(ByteRange range)
    {
        lock (_gate) _items.AddFirst(range);
    }
}
