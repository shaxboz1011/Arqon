namespace Arqon.Core.Download;

/// <summary>
/// Decides which downloads run and which wait: at most <see cref="Limit"/> at a time, the rest in the order they were added.
/// It only keeps the books. Every method that can free a place returns the ids that should be started now, and the
/// caller starts them. Ids are compared like Windows paths (ignoring case).
/// </summary>
public sealed class DownloadQueue
{
    private readonly object _gate = new();
    private readonly List<string> _waiting = [];
    private readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);

    public DownloadQueue(int limit) => Limit = Math.Max(1, limit);

    public int Limit { get; private set; }

    /// <summary>While held (outside the download schedule) nothing starts by itself; "Start now" still works.</summary>
    public bool Held { get; private set; }

    /// <summary>Holds or releases the line. Releasing returns what to start now.</summary>
    public IReadOnlyList<string> SetHeld(bool held)
    {
        lock (_gate)
        {
            Held = held;
            return Fill();
        }
    }

    /// <summary>Waiting downloads, next one first.</summary>
    public IReadOnlyList<string> Waiting
    {
        get { lock (_gate) return _waiting.ToList(); }
    }

    public IReadOnlyCollection<string> Running
    {
        get { lock (_gate) return _running.ToList(); }
    }

    public bool IsRunning(string id)
    {
        lock (_gate) return _running.Contains(id);
    }

    public bool IsWaiting(string id)
    {
        lock (_gate) return IndexOf(id) >= 0;
    }

    /// <summary>1 for the download that starts next, 2 for the one after it, and so on; 0 when it is not waiting.</summary>
    public int PositionOf(string id)
    {
        lock (_gate) return IndexOf(id) + 1;
    }

    /// <summary>Puts a download at the end of the line. Returns what to start now (possibly the download itself).</summary>
    public IReadOnlyList<string> Add(string id)
    {
        lock (_gate)
        {
            if (_running.Contains(id) || IndexOf(id) >= 0) return [];
            _waiting.Add(id);
            return Fill();
        }
    }

    /// <summary>A waiting download starts now, ahead of the line and even when the limit is reached.</summary>
    public IReadOnlyList<string> StartNow(string id)
    {
        lock (_gate)
        {
            var index = IndexOf(id);
            if (index < 0) return [];
            var actual = _waiting[index];
            _waiting.RemoveAt(index);
            _running.Add(actual);
            return [actual];
        }
    }

    /// <summary>A running download ended (done, failed or paused): its place goes to the next in line.</summary>
    public IReadOnlyList<string> Finished(string id)
    {
        lock (_gate)
        {
            _running.Remove(id);
            return Fill();
        }
    }

    /// <summary>Takes a download out of the books, whether it was running or waiting.</summary>
    public IReadOnlyList<string> Remove(string id)
    {
        lock (_gate)
        {
            var index = IndexOf(id);
            if (index >= 0) _waiting.RemoveAt(index);
            _running.Remove(id);
            return Fill();
        }
    }

    /// <summary>
    /// Raising the limit starts waiting downloads at once. Lowering it never stops a running download: nothing new starts
    /// until enough of them have finished.
    /// </summary>
    public IReadOnlyList<string> SetLimit(int limit)
    {
        lock (_gate)
        {
            Limit = Math.Max(1, limit);
            return Fill();
        }
    }

    private int IndexOf(string id) => _waiting.FindIndex(w => string.Equals(w, id, StringComparison.OrdinalIgnoreCase));

    private List<string> Fill()
    {
        var started = new List<string>();
        while (!Held && _running.Count < Limit && _waiting.Count > 0)
        {
            var next = _waiting[0];
            _waiting.RemoveAt(0);
            _running.Add(next);
            started.Add(next);
        }
        return started;
    }
}
