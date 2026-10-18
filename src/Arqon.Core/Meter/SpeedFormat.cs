using System.Globalization;

namespace Arqon.Core.Meter;

public readonly record struct SpeedText(string Value, string Unit);

/// <summary>How speeds are shown: megabits per second (what a tariff is sold in) or megabytes per second (what downloads show).</summary>
public enum SpeedUnit { Bits, Bytes }

/// <summary>Speed text and unit conversion. All units are decimal, so 80 Mbit/s is exactly 10 MB/s.</summary>
public static class SpeedFormat
{
    private static double Factor(SpeedUnit unit) => unit == SpeedUnit.Bits ? 8 : 1;

    /// <summary>Bytes per second to the "mega" unit of <paramref name="unit"/> (Mbit/s or MB/s).</summary>
    public static double ToMega(double bytesPerSecond, SpeedUnit unit) => bytesPerSecond * Factor(unit) / 1_000_000.0;

    /// <summary>The inverse of <see cref="ToMega"/>: a typed value such as "25.5" back to bytes per second.</summary>
    public static double FromMega(double mega, SpeedUnit unit) => mega * 1_000_000.0 / Factor(unit);

    /// <summary>Short text with the unit chosen by size (Kb/s, Mb/s, Gb/s or KB/s, MB/s, GB/s).</summary>
    public static SpeedText Of(double bytesPerSecond, SpeedUnit unit = SpeedUnit.Bits)
    {
        var kilo = double.IsFinite(bytesPerSecond) && bytesPerSecond > 0 ? bytesPerSecond * Factor(unit) / 1000 : 0;
        var (k, m, g) = unit == SpeedUnit.Bits ? ("Kb/s", "Mb/s", "Gb/s") : ("KB/s", "MB/s", "GB/s");

        if (Math.Round(kilo) < 1000) return new SpeedText(kilo.ToString("0", CultureInfo.InvariantCulture), k);

        var mega = kilo / 1000;
        if (Math.Round(mega, 1) < 1000) return new SpeedText(Number(mega), m);

        return new SpeedText(Number(mega / 1000), g);
    }

    private static string Number(double v) => v.ToString(v >= 99.95 ? "0" : "0.0", CultureInfo.InvariantCulture);
}
