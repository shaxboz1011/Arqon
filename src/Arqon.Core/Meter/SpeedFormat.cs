using System.Globalization;

namespace Arqon.Core.Meter;

public readonly record struct SpeedText(string Value, string Unit);

/// <summary>Short speed text for the taskbar meter, in bits per second like the rest of the app.</summary>
public static class SpeedFormat
{
    public static SpeedText Of(double bytesPerSecond)
    {
        var kilobits = double.IsFinite(bytesPerSecond) && bytesPerSecond > 0 ? bytesPerSecond * 8 / 1000 : 0;

        if (Math.Round(kilobits) < 1000) return new SpeedText(kilobits.ToString("0", CultureInfo.InvariantCulture), "Kb/s");

        var megabits = kilobits / 1000;
        if (Math.Round(megabits, 1) < 1000) return new SpeedText(Number(megabits), "Mb/s");

        return new SpeedText(Number(megabits / 1000), "Gb/s");
    }

    private static string Number(double v) => v.ToString(v >= 99.95 ? "0" : "0.0", CultureInfo.InvariantCulture);
}
