using System.Globalization;
using Arqon.Core.Meter;

namespace Arqon.App.Infrastructure;

public static class Units
{
    /// <summary>The speed unit chosen in Settings; <see cref="Services.AppServices"/> keeps it in step with the setting.</summary>
    public static SpeedUnit Unit { get; set; } = SpeedUnit.Bits;

    /// <summary>Bytes per second in the chosen unit: Mbit/s (what a tariff is sold in) or MB/s.</summary>
    public static double ToSpeed(double bytesPerSecond) => SpeedFormat.ToMega(bytesPerSecond, Unit);

    /// <summary>The inverse of <see cref="ToSpeed"/>, for values the user types.</summary>
    public static double FromSpeed(double value) => SpeedFormat.FromMega(value, Unit);

    public static string Speed(double bytesPerSecond) =>
        ToSpeed(bytesPerSecond).ToString(ToSpeed(bytesPerSecond) >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture);

    /// <summary>"Mbit/s" or "MB/s" in the current language.</summary>
    public static string SpeedLabel => Loc.Get(Unit == SpeedUnit.Bits ? "S.Unit.Mbit" : "S.Unit.MByte");

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
