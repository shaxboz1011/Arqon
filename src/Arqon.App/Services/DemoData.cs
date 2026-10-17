using System.Diagnostics;
using System.Net;
using Arqon.Core.Links;
using Arqon.Core.Monitoring;
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
