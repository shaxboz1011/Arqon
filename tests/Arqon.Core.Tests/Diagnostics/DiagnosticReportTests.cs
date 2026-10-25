using System.Net;
using Arqon.Core.Diagnostics;
using Arqon.Core.Links;
using Arqon.Core.Monitoring;
using Arqon.Core.Settings;

namespace Arqon.Core.Tests.Diagnostics;

public class DiagnosticReportTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 25, 22, 0, 0, TimeSpan.FromHours(5));

    private static DiagnosticInfo Info(IReadOnlyList<string>? crashes = null, IReadOnlyList<string>? steering = null, ArqonSettings? settings = null) =>
        new("0.3.0", "Windows 10.0.19045", "10.0.0", Administrator: false, Demo: false,
            Links:
            [
                new LinkInfo("w", "Wi-Fi", "Intel Wi-Fi 6", IPAddress.Parse("192.168.1.23"), IPAddress.Parse("192.168.1.1"), LinkKind.Wifi, 866_000_000),
                new LinkInfo("p", "Ethernet 3", "Remote NDIS", IPAddress.Parse("2a02:1234:5678::9abc"), null, LinkKind.Mobile, 0),
            ],
            Latest: new Dictionary<string, LinkSample> { ["w"] = new("w", At, 23.4, 2.1, 0.01, 1_048_576, 51_200) },
            SystemMode: "Off",
            Settings: settings ?? ArqonSettings.Default() with { DownloadDirectory = @"C:\Users\Ali\Downloads\Secret" },
            Crashes: crashes ?? [],
            SteeringLog: steering ?? [],
            At: At,
            UserProfile: @"C:\Users\Ali");

    [Fact]
    public void The_report_lists_version_connections_and_their_last_measurement()
    {
        var report = DiagnosticReport.Build(Info());

        Assert.StartsWith("Arqon diagnostic report", report);
        Assert.Contains("Version 0.3.0 · Windows 10.0.19045 · .NET 10.0.0", report);
        Assert.Contains("Connections (2):", report);
        Assert.Contains("Wi-Fi [Wifi]  Intel Wi-Fi 6  address 192.168.*.*  gateway yes  link 866 Mbit/s", report);
        Assert.Contains("ping 23 ms, jitter 2 ms, loss 1%, down 1024 KB/s, up 50 KB/s", report);
        Assert.Contains("Ethernet 3 [Mobile]  Remote NDIS  address 2a02:1234:…  gateway no", report);
    }

    [Fact]
    public void Nothing_that_names_the_user_is_in_the_report()
    {
        var report = DiagnosticReport.Build(Info(
            crashes: [@"----- 2026-10-25  ui" + "\n" + @"at Arqon.X in C:\USERS\ali\src\X.cs:line 3"],
            steering: [@"driver loaded from C:\Users\Ali\AppData\Roaming\Arqon\driver"]));

        Assert.DoesNotContain("Ali", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168.1.23", report);
        Assert.DoesNotContain("Secret", report);
        Assert.Contains(@"%USERPROFILE%\src\X.cs", report);
        Assert.Contains(@"%USERPROFILE%\AppData\Roaming\Arqon\driver", report);
    }

    [Fact]
    public void Empty_sections_say_so()
    {
        var report = DiagnosticReport.Build(Info());

        Assert.Contains("Recent crashes (0):\r\n  none".Replace("\r\n", Environment.NewLine), report);
        Assert.Contains("System mode log (last 0 lines):" + Environment.NewLine + "  empty", report);
    }

    [Fact]
    public void Settings_that_matter_for_a_bug_are_summarised()
    {
        var report = DiagnosticReport.Build(Info(settings: ArqonSettings.Default() with { DailyLimitMb = 1024, SeedTorrents = true }));

        Assert.Contains("language uz, game protection on, safe game mode on, system mode wanted off, parallel downloads 3, seeding on, update check on, daily limit 1024 MB, app rules 0", report);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C:")]
    public void Without_a_usable_user_folder_nothing_is_replaced(string? profile)
    {
        Assert.Equal(@"C:\Users\x", DiagnosticReport.Redact(@"C:\Users\x", profile));
    }
}
