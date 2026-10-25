using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Arqon.Core.Links;
using Arqon.Core.Monitoring;
using Arqon.Core.Settings;

namespace Arqon.Core.Diagnostics;

/// <summary>Everything a diagnostic report says. The app fills it from what is running; tests fill it by hand.</summary>
public sealed record DiagnosticInfo(
    string Version,
    string Os,
    string Runtime,
    bool Administrator,
    bool Demo,
    IReadOnlyList<LinkInfo> Links,
    IReadOnlyDictionary<string, LinkSample> Latest,
    string SystemMode,
    ArqonSettings Settings,
    IReadOnlyList<string> Crashes,
    IReadOnlyList<string> SteeringLog,
    DateTimeOffset At,
    string? UserProfile = null);

/// <summary>
/// A plain-text report to paste into a bug report. It is meant to be posted in public, so it leaves out what identifies the user:
/// the user folder is written as <c>%USERPROFILE%</c>, addresses keep only their first half, the download folder is not named.
/// </summary>
public static class DiagnosticReport
{
    public static string Build(DiagnosticInfo info)
    {
        var s = info.Settings;
        var b = new StringBuilder();
        b.AppendLine("Arqon diagnostic report");
        b.AppendLine($"Version {info.Version} · {info.Os} · .NET {info.Runtime}");
        b.AppendLine($"Administrator: {YesNo(info.Administrator)} · Demo: {YesNo(info.Demo)}");
        b.AppendLine($"Created {info.At.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)}");
        b.AppendLine();

        b.AppendLine($"Connections ({info.Links.Count}):");
        if (info.Links.Count == 0) b.AppendLine("  none");
        foreach (var link in info.Links)
        {
            var speed = link.SpeedBps > 0 ? $"  link {link.SpeedBps / 1_000_000.0:0.#} Mbit/s" : "";
            b.AppendLine($"  {link.Name} [{link.Kind}]  {link.Description}  address {MaskAddress(link.LocalAddress)}  gateway {YesNo(link.Gateway is not null)}{speed}");
            if (info.Latest.TryGetValue(link.Id, out var sample))
                b.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"    ping {sample.LatencyMs:0} ms, jitter {sample.JitterMs:0} ms, loss {sample.Loss * 100:0}%, down {sample.DownBps / 1024:0} KB/s, up {sample.UpBps / 1024:0} KB/s"));
        }
        b.AppendLine();

        b.AppendLine($"System mode: {info.SystemMode}");
        b.AppendLine($"Settings: language {s.Language}, game protection {OnOff(s.GameProtection)}, safe game mode {OnOff(s.SafeGameMode)}, " +
                     $"system mode wanted {OnOff(s.SystemMode)}, parallel downloads {s.MaxConcurrentDownloads}, seeding {OnOff(s.SeedTorrents)}, " +
                     $"update check {OnOff(s.CheckUpdates)}, daily limit {(s.DailyLimitMb > 0 ? s.DailyLimitMb + " MB" : "off")}, app rules {s.Rules.Count}");
        b.AppendLine();

        b.AppendLine($"Recent crashes ({info.Crashes.Count}):");
        if (info.Crashes.Count == 0) b.AppendLine("  none");
        foreach (var crash in info.Crashes) b.AppendLine(crash);
        b.AppendLine();

        b.AppendLine($"System mode log (last {info.SteeringLog.Count} lines):");
        if (info.SteeringLog.Count == 0) b.AppendLine("  empty");
        foreach (var line in info.SteeringLog) b.Append("  ").AppendLine(line);

        return Redact(b.ToString().TrimEnd() + Environment.NewLine, info.UserProfile);
    }

    /// <summary>Replaces the user's folder (it holds the Windows user name) wherever it appears, ignoring case.</summary>
    public static string Redact(string text, string? userProfile)
    {
        if (string.IsNullOrWhiteSpace(userProfile)) return text;
        var profile = userProfile.TrimEnd('\\', '/');
        return profile.Length < 3 ? text : text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>192.168.1.23 becomes 192.168.*.*; an IPv6 address keeps its first two groups.</summary>
    public static string MaskAddress(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return $"{bytes[0]}.{bytes[1]}.*.*";
        }
        var v6 = address.GetAddressBytes();
        return $"{v6[0] << 8 | v6[1]:x}:{v6[2] << 8 | v6[3]:x}:…";
    }

    private static string YesNo(bool value) => value ? "yes" : "no";
    private static string OnOff(bool value) => value ? "on" : "off";
}
