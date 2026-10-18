using System.Globalization;
using System.Text.Json;
using Arqon.Core.Monitoring;

namespace Arqon.Core.Usage;

public sealed record DayUsage(DateOnly Date, long Down, long Up);

public sealed record LinkUsage(string LinkId, string Name, IReadOnlyList<DayUsage> Days);

public sealed record UsageTotals(long Down, long Up);

public sealed record LinkTotals(string LinkId, string Name, long Down, long Up);

/// <summary>Counts how many bytes each link carried per day, and remembers it across restarts.</summary>
public sealed class UsageTracker
{
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(10);
    private const int KeepDays = 400;

    private sealed class LinkData
    {
        public string Name { get; set; } = "";
        public Dictionary<string, long[]> Days { get; set; } = new();
    }

    private sealed class FileData
    {
        public Dictionary<string, LinkData> Links { get; set; } = new();
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arqon", "usage.json");

    private readonly string? _path;
    private readonly TimeZoneInfo _zone;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkData> _links = new();
    private readonly Dictionary<string, DateTimeOffset> _lastSample = new();

    public UsageTracker(string? path, TimeZoneInfo? zone = null, Func<DateTimeOffset>? clock = null)
    {
        _path = path;
        _zone = zone ?? TimeZoneInfo.Local;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Load();
    }

    /// <summary>Feeds one monitor sample: its speed is applied to the time since the link's previous sample.</summary>
    public void OnSample(LinkSample sample, string linkName)
    {
        lock (_gate)
        {
            var had = _lastSample.TryGetValue(sample.LinkId, out var previous);
            _lastSample[sample.LinkId] = sample.At;
            if (!had) { Touch(sample.LinkId, linkName); return; }

            var elapsed = sample.At - previous;
            if (elapsed <= TimeSpan.Zero || elapsed > MaxGap) { Touch(sample.LinkId, linkName); return; }

            var seconds = elapsed.TotalSeconds;
            AddLocked(sample.LinkId, linkName, DayOf(sample.At), Bytes(sample.DownBps, seconds), Bytes(sample.UpBps, seconds));
        }
    }

    public void Add(string linkId, string linkName, DateOnly day, long down, long up)
    {
        lock (_gate) AddLocked(linkId, linkName, day, down, up);
    }

    /// <summary>Every known link with one entry per day for the last <paramref name="days"/> days, oldest first.</summary>
    public IReadOnlyList<LinkUsage> Last(int days)
    {
        var today = DayOf(_clock());
        lock (_gate)
        {
            return _links
                .OrderBy(kv => kv.Value.Name, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new LinkUsage(kv.Key, kv.Value.Name,
                    Enumerable.Range(0, days).Select(i =>
                    {
                        var date = today.AddDays(i - (days - 1));
                        return kv.Value.Days.TryGetValue(Key(date), out var v)
                            ? new DayUsage(date, v[0], v[1])
                            : new DayUsage(date, 0, 0);
                    }).ToList()))
                .ToList();
        }
    }

    public UsageTotals Today() => Sum(d => d == DayOf(_clock()));

    public UsageTotals ThisMonth()
    {
        var today = DayOf(_clock());
        return Sum(d => d.Year == today.Year && d.Month == today.Month);
    }

    /// <summary>Per link, largest first; links with no traffic in the period are left out.</summary>
    public IReadOnlyList<LinkTotals> TodayByLink() => SumByLink(d => d == DayOf(_clock()));

    public IReadOnlyList<LinkTotals> MonthByLink()
    {
        var today = DayOf(_clock());
        return SumByLink(d => d.Year == today.Year && d.Month == today.Month);
    }

    public void Save()
    {
        if (_path is null) return;
        string json;
        lock (_gate)
        {
            var oldest = DayOf(_clock()).AddDays(-KeepDays);
            foreach (var link in _links.Values)
                foreach (var key in link.Days.Keys.Where(k => Parse(k) is { } d && d < oldest).ToList())
                    link.Days.Remove(key);
            json = JsonSerializer.Serialize(new FileData { Links = _links });
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Statistics are not worth interrupting the user for; the next save tries again.
        }
    }

    private UsageTotals Sum(Func<DateOnly, bool> include)
    {
        long down = 0, up = 0;
        lock (_gate)
        {
            foreach (var link in _links.Values)
                foreach (var (key, v) in link.Days)
                    if (Parse(key) is { } d && include(d)) { down += v[0]; up += v[1]; }
        }
        return new UsageTotals(down, up);
    }

    private IReadOnlyList<LinkTotals> SumByLink(Func<DateOnly, bool> include)
    {
        lock (_gate)
        {
            return _links
                .Select(kv =>
                {
                    long down = 0, up = 0;
                    foreach (var (key, v) in kv.Value.Days)
                        if (Parse(key) is { } d && include(d)) { down += v[0]; up += v[1]; }
                    return new LinkTotals(kv.Key, kv.Value.Name, down, up);
                })
                .Where(t => t.Down + t.Up > 0)
                .OrderByDescending(t => t.Down + t.Up)
                .ToList();
        }
    }

    private void Touch(string linkId, string name)
    {
        if (!_links.TryGetValue(linkId, out var link)) _links[linkId] = link = new LinkData();
        link.Name = name;
        link.Days.TryAdd(Key(DayOf(_clock())), [0, 0]);
    }

    private void AddLocked(string linkId, string name, DateOnly day, long down, long up)
    {
        Touch(linkId, name);
        var days = _links[linkId].Days;
        var key = Key(day);
        if (!days.TryGetValue(key, out var v)) days[key] = v = [0, 0];
        v[0] += Math.Max(0, down);
        v[1] += Math.Max(0, up);
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            var data = JsonSerializer.Deserialize<FileData>(File.ReadAllText(_path));
            if (data?.Links is null) return;
            foreach (var (id, link) in data.Links)
            {
                if (link?.Days is null) continue;
                var clean = new LinkData { Name = link.Name ?? id };
                foreach (var (key, v) in link.Days)
                    if (Parse(key) is not null && v is { Length: 2 }) clean.Days[key] = [Math.Max(0, v[0]), Math.Max(0, v[1])];
                _links[id] = clean;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _links.Clear();
        }
    }

    private DateOnly DayOf(DateTimeOffset moment) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, _zone).DateTime);

    private static long Bytes(double bytesPerSecond, double seconds) =>
        double.IsFinite(bytesPerSecond) && bytesPerSecond > 0 ? (long)(bytesPerSecond * seconds) : 0;

    private static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly? Parse(string key) =>
        DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
