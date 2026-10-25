namespace Arqon.Core.Download;

/// <summary>
/// Hours in which queued downloads may start, such as 01:00–07:00 when a night plan is cheaper. A window that ends before it
/// starts runs over midnight; equal hours mean the whole day. Downloads that are already running are never stopped by it.
/// </summary>
public sealed record DownloadSchedule(bool Enabled, int StartHour, int EndHour)
{
    public static DownloadSchedule Off { get; } = new(false, 1, 7);

    public bool IsOpen(TimeOnly time)
    {
        if (!Enabled) return true;
        int start = Clamp(StartHour), end = Clamp(EndHour), hour = time.Hour;
        if (start == end) return true;
        return start < end ? hour >= start && hour < end : hour >= start || hour < end;
    }

    /// <summary>When the window opens next (now when it is open).</summary>
    public DateTime NextOpen(DateTime now)
    {
        if (IsOpen(TimeOnly.FromDateTime(now))) return now;
        var opens = now.Date.AddHours(Clamp(StartHour));
        return opens > now ? opens : opens.AddDays(1);
    }

    private static int Clamp(int hour) => Math.Clamp(hour, 0, 23);
}
