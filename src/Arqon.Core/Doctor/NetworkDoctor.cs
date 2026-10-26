using System.Net;
using System.Net.Sockets;
using Arqon.Core.Links;

namespace Arqon.Core.Doctor;

public enum FindingLevel { Ok, Warning, Problem }

/// <summary>One result of the check. <see cref="Key"/> names the text to show; <see cref="Args"/> fill it in.</summary>
public sealed record Finding(FindingLevel Level, string Key, IReadOnlyList<string> Args, string? LinkId = null);

/// <summary>What one link answered.</summary>
public sealed record LinkCheck(string LinkId, string Name, bool Internet, bool CaptivePortal, string? ExitAddress, string? Error);

public sealed record DoctorReport(IReadOnlyList<LinkCheck> Links, IReadOnlyList<Finding> Findings)
{
    public FindingLevel Worst => Findings.Count == 0 ? FindingLevel.Ok : Findings.Max(f => f.Level);
}

/// <summary>
/// "Why is it not faster?" in one click. For every link it asks Cloudflare (the same company whose 1.1.1.1 the link monitor
/// already pings) two small questions: does an empty page come back unchanged (no sign-in page of a hotel or café network in
/// the way), and from which public address does the link reach the internet. Two links with the same public address share one
/// line, so combining them cannot add speed. It also checks that names resolve and that Windows does not switch the second
/// network off. It runs only when the user asks.
/// </summary>
public sealed class NetworkDoctor(
    Func<LinkInfo, HttpClient> clientFor,
    Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
    Func<bool>? windowsPolicyNeedsFix = null)
{
    public static readonly Uri PortalProbe = new("http://cp.cloudflare.com/generate_204");
    public static readonly Uri TraceProbe = new("https://1.1.1.1/cdn-cgi/trace");
    public const string DnsProbeName = "github.com";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve = resolve ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));

    public async Task<DoctorReport> RunAsync(IReadOnlyList<LinkInfo> links, CancellationToken ct = default)
    {
        var checks = await Task.WhenAll(links.Select(l => CheckLinkAsync(l, ct))).ConfigureAwait(false);
        var findings = new List<Finding>();

        if (links.Count == 0) findings.Add(new Finding(FindingLevel.Problem, "NoLinks", []));
        else if (links.Count == 1) findings.Add(new Finding(FindingLevel.Warning, "OneLink", [links[0].Name]));

        foreach (var c in checks)
        {
            if (c.CaptivePortal) findings.Add(new Finding(FindingLevel.Problem, "Portal", [c.Name], c.LinkId));
            else if (!c.Internet) findings.Add(new Finding(FindingLevel.Problem, "NoInternet", [c.Name, c.Error ?? "?"], c.LinkId));
            else findings.Add(new Finding(FindingLevel.Ok, "LinkOk", [c.Name], c.LinkId));
        }

        foreach (var group in checks.Where(c => c.Internet && c.ExitAddress is not null).GroupBy(c => c.ExitAddress).Where(g => g.Count() > 1))
            findings.Add(new Finding(FindingLevel.Warning, "SameExit", [string.Join(", ", group.Select(c => c.Name)), Mask(group.Key!)]));

        if (checks.Any(c => c.Internet)) findings.Add(await CheckDnsAsync(ct).ConfigureAwait(false));

        if (windowsPolicyNeedsFix?.Invoke() == true) findings.Add(new Finding(FindingLevel.Problem, "WindowsPolicy", []));

        return new DoctorReport(checks, findings);
    }

    private async Task<LinkCheck> CheckLinkAsync(LinkInfo link, CancellationToken ct)
    {
        using var client = clientFor(link);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            // A portal answers the empty page with its own sign-in page or a redirect; the real one is "204 No Content".
            using (var portal = await client.GetAsync(PortalProbe, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
            {
                if (portal.StatusCode != HttpStatusCode.NoContent)
                    return new LinkCheck(link.Id, link.Name, Internet: false, CaptivePortal: true, null, $"HTTP {(int)portal.StatusCode}");
            }
            var trace = await client.GetStringAsync(TraceProbe, timeout.Token).ConfigureAwait(false);
            return new LinkCheck(link.Id, link.Name, true, false, ExitAddressOf(trace), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return new LinkCheck(link.Id, link.Name, false, false, null, ex is OperationCanceledException ? "timeout" : ex.Message);
        }
    }

    private async Task<Finding> CheckDnsAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            var addresses = await _resolve(DnsProbeName, timeout.Token).ConfigureAwait(false);
            return addresses.Length > 0 ? new Finding(FindingLevel.Ok, "DnsOk", []) : new Finding(FindingLevel.Problem, "Dns", [DnsProbeName]);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return new Finding(FindingLevel.Problem, "Dns", [DnsProbeName]);
        }
    }

    /// <summary>The "ip=" line of a Cloudflare trace answer.</summary>
    public static string? ExitAddressOf(string trace) =>
        trace.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("ip=", StringComparison.Ordinal))?[3..] is { Length: > 0 } ip
        && IPAddress.TryParse(ip, out _) ? ip : null;

    /// <summary>Only the first half of a public address is shown, so a copied result does not give it away.</summary>
    public static string Mask(string address) =>
        IPAddress.TryParse(address, out var ip) ? Diagnostics.DiagnosticReport.MaskAddress(ip) : "…";
}
