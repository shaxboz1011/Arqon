using System.Globalization;

namespace Arqon.App.Infrastructure;

public static class Units
{
    /// <summary>Bytes per second to megabits per second, the unit people compare against their tariff.</summary>
    public static double ToMbit(double bytesPerSecond) => bytesPerSecond * 8 / 1_000_000.0;

    public static string Mbit(double bytesPerSecond) =>
        ToMbit(bytesPerSecond).ToString(ToMbit(bytesPerSecond) >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture);

    public static string Size(long bytes)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return (i == 0 ? v.ToString("0") : v.ToString("0.0", CultureInfo.InvariantCulture)) + " " + u[i];
    }

    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
}
