namespace Arqon.Core.Relay;

/// <summary>
/// Pieces arrive over several links, so out of order and (for duplicated game packets) more than once. This hands them on in
/// sequence order, each exactly once. A missing piece is waited for at most <see cref="MaxWait"/>; after that the buffer skips
/// it, because the links are lossy and a stuck stream is worse than a lost packet (the tunnelled TCP resends it anyway).
/// </summary>
public sealed class ReorderBuffer(TimeSpan maxWait, int capacity = 4096)
{
    private readonly SortedDictionary<ulong, (byte[] Payload, DateTimeOffset At)> _pending = new();

    public TimeSpan MaxWait { get; } = maxWait;

    /// <summary>The next sequence number to hand on.</summary>
    public ulong Next { get; private set; }

    public long Duplicates { get; private set; }
    public long Skipped { get; private set; }

    /// <summary>Takes one piece and returns what can be handed on now, in order.</summary>
    public IReadOnlyList<byte[]> Push(ulong sequence, byte[] payload, DateTimeOffset now)
    {
        if (sequence < Next || _pending.ContainsKey(sequence))
        {
            Duplicates++;
            return Drain(now);
        }
        _pending[sequence] = (payload, now);
        if (_pending.Count > capacity) SkipGap();   // Drain then hands on from the new start
        return Drain(now);
    }

    /// <summary>Called now and then without new data: hands on what waited long enough behind a gap.</summary>
    public IReadOnlyList<byte[]> Flush(DateTimeOffset now) => Drain(now);

    private List<byte[]> Drain(DateTimeOffset now)
    {
        var ready = new List<byte[]>();
        while (true)
        {
            if (_pending.Remove(Next, out var piece))
            {
                ready.Add(piece.Payload);
                Next++;
                continue;
            }
            if (_pending.Count == 0) return ready;
            var (first, oldest) = _pending.First();
            if (now - oldest.At < MaxWait) return ready;
            Skipped += (long)(first - Next);
            Next = first;
        }
    }

    private void SkipGap()
    {
        var first = _pending.Keys.First();
        if (first > Next)
        {
            Skipped += (long)(first - Next);
            Next = first;
        }
    }
}
