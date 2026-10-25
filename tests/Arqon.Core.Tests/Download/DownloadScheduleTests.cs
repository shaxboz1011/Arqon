using Arqon.Core.Download;

namespace Arqon.Core.Tests.Download;

public class DownloadScheduleTests
{
    private static TimeOnly T(int hour, int minute = 0) => new(hour, minute);

    [Fact]
    public void Switched_off_it_is_always_open()
    {
        Assert.True(DownloadSchedule.Off.IsOpen(T(12)));
        Assert.True(new DownloadSchedule(false, 1, 2).IsOpen(T(12)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    [InlineData(23, false)]
    public void A_window_within_one_day(int hour, bool open)
    {
        Assert.Equal(open, new DownloadSchedule(true, 1, 7).IsOpen(T(hour, 30)));
    }

    [Theory]
    [InlineData(22, false)]
    [InlineData(23, true)]
    [InlineData(0, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public void A_window_over_midnight(int hour, bool open)
    {
        Assert.Equal(open, new DownloadSchedule(true, 23, 7).IsOpen(T(hour)));
    }

    [Fact]
    public void Equal_hours_mean_the_whole_day()
    {
        Assert.True(new DownloadSchedule(true, 5, 5).IsOpen(T(17)));
    }

    [Fact]
    public void The_next_opening_is_today_or_tomorrow()
    {
        var s = new DownloadSchedule(true, 1, 7);

        Assert.Equal(new DateTime(2026, 10, 26, 1, 0, 0), s.NextOpen(new DateTime(2026, 10, 25, 22, 15, 0)));
        Assert.Equal(new DateTime(2026, 10, 25, 1, 0, 0), s.NextOpen(new DateTime(2026, 10, 25, 0, 15, 0)));
        var now = new DateTime(2026, 10, 25, 3, 0, 0);
        Assert.Equal(now, s.NextOpen(now));
    }
}
