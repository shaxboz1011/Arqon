using Arqon.Core.Monitoring;
using Arqon.Core.Usage;

namespace Arqon.Core.Tests.Usage;

public sealed class UsageTrackerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-usage-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "usage.json");

    public UsageTrackerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static readonly DateTimeOffset Noon = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static LinkSample S(string id, DateTimeOffset at, double down, double up = 0) =>
        new(id, at, 20, 1, 0, down, up);

    private static UsageTracker Make(string? path = null, Func<DateTimeOffset>? clock = null) =>
        new(path, TimeZoneInfo.Utc, clock ?? (() => Noon));

    [Fact]
    public void Adds_up_bytes_from_speed_times_elapsed_time()
    {
        var t = Make();
        t.OnSample(S("wifi", Noon, 1000), "Wi-Fi");
        t.OnSample(S("wifi", Noon.AddSeconds(2), 1000, 500), "Wi-Fi");
        t.OnSample(S("wifi", Noon.AddSeconds(3), 3000, 0), "Wi-Fi");

        var today = t.Last(1).Single(l => l.LinkId == "wifi").Days.Single();

        Assert.Equal(1000 * 2 + 3000, today.Down);
        Assert.Equal(500 * 2, today.Up);
    }

    [Fact]
    public void The_first_sample_only_starts_the_clock()
    {
        var t = Make();
        t.OnSample(S("wifi", Noon, 1_000_000), "Wi-Fi");

        Assert.Equal(0, t.Last(1).Single().Days.Single().Down);
    }

    [Fact]
    public void A_long_gap_such_as_sleep_is_not_counted_as_traffic()
    {
        var t = Make();
        t.OnSample(S("wifi", Noon, 1000), "Wi-Fi");
        t.OnSample(S("wifi", Noon.AddHours(8), 1000), "Wi-Fi");

        Assert.True(t.Last(1).Single().Days.Single().Down <= 10_000);
    }

    [Fact]
    public void Each_link_is_counted_separately_and_keeps_its_name()
    {
        var t = Make();
        foreach (var (id, name) in new[] { ("wifi", "Wi-Fi"), ("phone", "Phone") })
        {
            t.OnSample(S(id, Noon, 100), name);
            t.OnSample(S(id, Noon.AddSeconds(1), id == "wifi" ? 400 : 100), name);
        }

        var links = t.Last(1);

        Assert.Equal(["Phone", "Wi-Fi"], links.Select(l => l.Name).Order().ToArray());
        Assert.Equal(400, links.Single(l => l.LinkId == "wifi").Days.Single().Down);
        Assert.Equal(100, links.Single(l => l.LinkId == "phone").Days.Single().Down);
    }

    [Fact]
    public void Bytes_are_filed_under_the_day_they_happened()
    {
        var t = Make(clock: () => Noon.AddDays(1));
        var evening = Noon.AddHours(11).AddMinutes(59).AddSeconds(58);
        t.OnSample(S("wifi", evening, 100), "Wi-Fi");
        t.OnSample(S("wifi", evening.AddSeconds(1), 100), "Wi-Fi");   // 23:59:59
        t.OnSample(S("wifi", evening.AddSeconds(3), 100), "Wi-Fi");   // 00:00:01 next day

        var days = t.Last(2).Single().Days;

        Assert.Equal(2, days.Count);
        Assert.Equal(new DateOnly(2026, 10, 18), days[0].Date);
        Assert.Equal(new DateOnly(2026, 10, 19), days[1].Date);
        Assert.Equal(100, days[0].Down);
        Assert.Equal(200, days[1].Down);
    }

    [Fact]
    public void Last_returns_every_day_in_the_range_even_when_empty_oldest_first()
    {
        var t = Make();
        t.OnSample(S("wifi", Noon, 100), "Wi-Fi");
        t.OnSample(S("wifi", Noon.AddSeconds(1), 100), "Wi-Fi");

        var days = t.Last(7).Single().Days;

        Assert.Equal(7, days.Count);
        Assert.Equal(new DateOnly(2026, 10, 12), days[0].Date);
        Assert.Equal(new DateOnly(2026, 10, 18), days[^1].Date);
        Assert.All(days.Take(6), d => Assert.Equal(0, d.Down));
        Assert.Equal(100, days[^1].Down);
    }

    [Fact]
    public void Totals_cover_today_and_the_current_month()
    {
        var t = Make();
        t.Add("wifi", "Wi-Fi", new DateOnly(2026, 10, 1), 1000, 10);
        t.Add("wifi", "Wi-Fi", new DateOnly(2026, 10, 18), 500, 5);
        t.Add("wifi", "Wi-Fi", new DateOnly(2026, 9, 30), 9999, 99);

        Assert.Equal(500, t.Today().Down);
        Assert.Equal(1500, t.ThisMonth().Down);
        Assert.Equal(15, t.ThisMonth().Up);
    }

    [Fact]
    public void Saves_and_reloads()
    {
        var t = Make(FilePath);
        t.Add("phone", "Phone", new DateOnly(2026, 10, 17), 123456, 789);
        t.Save();

        var again = Make(FilePath);

        var day = again.Last(3).Single().Days.Single(d => d.Date == new DateOnly(2026, 10, 17));
        Assert.Equal(123456, day.Down);
        Assert.Equal(789, day.Up);
        Assert.Equal("Phone", again.Last(1).Single().Name);
    }

    [Fact]
    public void A_corrupt_file_starts_fresh_instead_of_crashing()
    {
        File.WriteAllText(FilePath, "{ this is not json");

        var t = Make(FilePath);

        Assert.Empty(t.Last(1));
    }

    [Fact]
    public void Saving_leaves_no_temporary_files_behind()
    {
        var t = Make(FilePath);
        t.Add("wifi", "Wi-Fi", new DateOnly(2026, 10, 18), 1, 1);
        t.Save();
        t.Save();

        Assert.Equal(["usage.json"], Directory.GetFiles(_dir).Select(f => Path.GetFileName(f)!).ToArray());
    }

    [Fact]
    public void Very_old_days_are_dropped_on_save()
    {
        var t = Make(FilePath);
        t.Add("wifi", "Wi-Fi", new DateOnly(2024, 1, 1), 5, 5);
        t.Add("wifi", "Wi-Fi", new DateOnly(2026, 10, 18), 7, 7);
        t.Save();

        var again = Make(FilePath);

        Assert.Equal(7, again.Last(800).Single().Days.Sum(d => d.Down));
    }

    [Fact]
    public void A_link_that_vanished_keeps_its_history()
    {
        var t = Make(FilePath);
        t.Add("phone", "Phone", new DateOnly(2026, 10, 17), 100, 0);
        t.Save();

        Assert.Contains(Make(FilePath).Last(3), l => l.LinkId == "phone");
    }

    [Fact]
    public void Negative_or_nonsense_speeds_never_reduce_the_totals()
    {
        var t = Make();
        t.OnSample(S("wifi", Noon, 100), "Wi-Fi");
        t.OnSample(S("wifi", Noon.AddSeconds(1), -500, double.NaN), "Wi-Fi");

        var day = t.Last(1).Single().Days.Single();
        Assert.Equal(0, day.Down);
        Assert.Equal(0, day.Up);
    }
}
