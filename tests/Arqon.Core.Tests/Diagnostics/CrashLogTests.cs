using Arqon.Core.Diagnostics;

namespace Arqon.Core.Tests.Diagnostics;

public sealed class CrashLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-crash-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static Exception Thrown(string message)
    {
        try { throw new InvalidOperationException(message); }
        catch (InvalidOperationException ex) { return ex; }
    }

    [Fact]
    public void Nothing_recorded_means_no_entries_and_no_time()
    {
        var log = new CrashLog(_dir);

        Assert.Empty(log.Recent(5));
        Assert.Null(log.LastAt());
    }

    [Fact]
    public void A_crash_is_kept_with_its_source_message_and_time()
    {
        var log = new CrashLog(_dir);
        var at = new DateTimeOffset(2026, 10, 25, 21, 30, 5, TimeSpan.FromHours(5));

        log.Record(Thrown("boom"), "ui", at);

        var entry = Assert.Single(log.Recent(5));
        Assert.StartsWith("----- 2026-10-25 21:30:05 +05:00  ui", entry);
        Assert.Contains("InvalidOperationException: boom", entry);
        Assert.Equal(at, log.LastAt());
    }

    [Fact]
    public void Recent_gives_the_newest_entries_oldest_first()
    {
        var log = new CrashLog(_dir);
        for (var i = 1; i <= 4; i++) log.Record(Thrown($"crash {i}"), "task", DateTimeOffset.Now.AddMinutes(i));

        var recent = log.Recent(2);

        Assert.Equal(2, recent.Count);
        Assert.Contains("crash 3", recent[0]);
        Assert.Contains("crash 4", recent[1]);
    }

    [Fact]
    public void The_file_trims_itself_instead_of_growing_without_end()
    {
        var log = new CrashLog(_dir);
        var big = new string('x', 30_000);
        for (var i = 0; i < 40; i++) log.Record(Thrown(big + i), "fatal");

        Assert.True(new FileInfo(log.FilePath).Length < 1024 * 1024);
        Assert.Contains("x39", log.Recent(1)[0]);
        Assert.True(log.Recent(100).Count <= 21);
    }

    [Fact]
    public void A_huge_exception_text_is_cut()
    {
        var log = new CrashLog(_dir);

        log.Record(Thrown(new string('y', 100_000)), "ui");

        var entry = Assert.Single(log.Recent(1));
        Assert.True(entry.Length < 40_000);
        Assert.EndsWith("[cut]", entry);
    }

    [Fact]
    public void An_old_style_log_without_separators_still_reads_as_one_entry()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "crash.log"), "2026-10-01T10:00:00\nSystem.Exception: old\n\n");
        var log = new CrashLog(_dir);

        log.Record(Thrown("new"), "ui");

        var entries = log.Recent(5);
        Assert.Equal(2, entries.Count);
        Assert.Contains("old", entries[0]);
        Assert.Contains("new", entries[1]);
    }
}
