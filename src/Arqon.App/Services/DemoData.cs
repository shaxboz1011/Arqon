using System.Diagnostics;
using System.Net;
using Arqon.Core.Links;
using Arqon.Core.Monitoring;
using Arqon.Core.Usage;
using Arqon.Core.Windows;

namespace Arqon.App.Services;

// Demo mode (--demo): two pretend links so the whole UI can be explored and screenshotted without a phone attached.

internal sealed class DemoLinkProvider : ILinkProvider
{
    public static readonly LinkInfo Wifi = new("wifi", "Wi-Fi", "Demo Wi-Fi adapter",
        IPAddress.Parse("192.168.1.20"), IPAddress.Parse("192.168.1.1"), LinkKind.Wifi, 144_000_000);

    public static readonly LinkInfo Phone = new("phone", "Telefon (USB)", "Demo USB tethering",
        IPAddress.Parse("192.168.42.129"), IPAddress.Parse("192.168.42.129"), LinkKind.Mobile, 100_000_000);

    public IReadOnlyList<LinkInfo> GetLinks() => [Wifi, Phone];
}

internal sealed class DemoProbe : IProbe
{
    private readonly Random _rng = new(7);

    public Task<double?> ProbeAsync(LinkInfo link, CancellationToken ct)
    {
        lock (_rng)
        {
            if (link.Id == "wifi") return Task.FromResult<double?>(18 + _rng.NextDouble() * 5);
            if (_rng.NextDouble() < 0.004) return Task.FromResult<double?>(null);
            return Task.FromResult<double?>(33 + _rng.NextDouble() * 12);
        }
    }
}

internal sealed class DemoCounters : ICounterReader
{
    private readonly Stopwatch _t = Stopwatch.StartNew();

    public (long Rx, long Tx) Read(LinkInfo link)
    {
        var t = _t.Elapsed.TotalSeconds;
        var (down, amp, w) = link.Id == "wifi" ? (5_200_000.0, 1_800_000.0, 0.55) : (3_900_000.0, 1_200_000.0, 0.8);
        var rx = down * t + amp * Math.Sin(w * t) / w;
        return ((long)rx, (long)(rx * 0.08));
    }
}

internal sealed class DemoWcmPolicy : IWcmPolicy
{
    private bool? _value = true;
    public bool? GetMinimizeConnections() => _value;
    public void SetMinimizeConnections(bool value) => _value = value;
}

internal static class DemoUsage
{
    /// <summary>Two weeks of believable history so the Usage page has something to show in demo mode.</summary>
    public static UsageTracker Seeded()
    {
        var tracker = new UsageTracker(null);
        var rng = new Random(3);
        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var i = 13; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var wifi = (long)((1.2 + rng.NextDouble() * 3.5 + (weekend ? 2.5 : 0)) * 1_000_000_000);
            var phone = (long)((0.2 + rng.NextDouble() * 1.8 + (weekend ? 1.5 : 0)) * 1_000_000_000);
            tracker.Add("wifi", "Wi-Fi", day, wifi, wifi / 14);
            tracker.Add("phone", "Telefon (USB)", day, phone, phone / 12);
        }
        return tracker;
    }
}
