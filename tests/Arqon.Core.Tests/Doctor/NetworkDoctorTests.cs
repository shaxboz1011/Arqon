using System.Net;
using System.Net.Sockets;
using Arqon.Core.Doctor;
using Arqon.Core.Links;

namespace Arqon.Core.Tests.Doctor;

public class NetworkDoctorTests
{
    private static LinkInfo Link(string id) => new(id, id, "", IPAddress.Loopback, null, LinkKind.Wifi, 0);

    /// <summary>How each pretend link answers: a working one with its public address, a portal, or nothing.</summary>
    private sealed record Behaviour(string? Exit = null, bool Portal = false, bool Dead = false);

    private sealed class Handler(Behaviour b) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (b.Dead) throw new HttpRequestException("No route to host");
            if (request.RequestUri == NetworkDoctor.PortalProbe)
                return Task.FromResult(b.Portal
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Please sign in</html>") }
                    : new HttpResponseMessage(HttpStatusCode.NoContent));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"fl=1\nh=1.1.1.1\nip={b.Exit}\nts=1\nloc=UZ\n"),
            });
        }
    }

    private static NetworkDoctor Doctor(Dictionary<string, Behaviour> links, bool dnsWorks = true, bool policy = false) => new(
        l => new HttpClient(new Handler(links[l.Id])),
        (_, _) => dnsWorks ? Task.FromResult(new[] { IPAddress.Parse("140.82.121.4") }) : throw new SocketException((int)SocketError.HostNotFound),
        () => policy);

    private static IEnumerable<string> Keys(DoctorReport r) => r.Findings.Select(f => f.Key);

    [Fact]
    public async Task Two_working_links_with_their_own_lines_are_all_fine()
    {
        var report = await Doctor(new() { ["wifi"] = new("84.54.1.2"), ["phone"] = new("213.230.9.9") }).RunAsync([Link("wifi"), Link("phone")]);

        Assert.Equal(FindingLevel.Ok, report.Worst);
        Assert.Equal(["LinkOk", "LinkOk", "DnsOk"], Keys(report));
        Assert.Equal("84.54.1.2", report.Links[0].ExitAddress);
    }

    [Fact]
    public async Task Two_links_with_one_public_address_share_a_line_and_are_reported_masked()
    {
        var report = await Doctor(new() { ["wifi"] = new("84.54.1.2"), ["cable"] = new("84.54.1.2") }).RunAsync([Link("wifi"), Link("cable")]);

        var same = Assert.Single(report.Findings, f => f.Key == "SameExit");
        Assert.Equal(FindingLevel.Warning, same.Level);
        Assert.Equal(["wifi, cable", "84.54.*.*"], same.Args);
    }

    [Fact]
    public async Task A_sign_in_page_and_a_dead_link_are_problems_of_their_own_link()
    {
        var report = await Doctor(new() { ["hotel"] = new(Portal: true), ["phone"] = new(Dead: true), ["wifi"] = new("1.2.3.4") })
            .RunAsync([Link("hotel"), Link("phone"), Link("wifi")]);

        Assert.Equal(FindingLevel.Problem, report.Worst);
        Assert.Contains(report.Findings, f => f is { Key: "Portal", LinkId: "hotel" });
        var dead = Assert.Single(report.Findings, f => f.Key == "NoInternet");
        Assert.Equal(["phone", "No route to host"], dead.Args);
        Assert.Contains(report.Findings, f => f is { Key: "LinkOk", LinkId: "wifi" });
    }

    [Fact]
    public async Task Broken_name_lookups_and_the_windows_policy_are_found()
    {
        var report = await Doctor(new() { ["wifi"] = new("1.2.3.4") }, dnsWorks: false, policy: true).RunAsync([Link("wifi")]);

        Assert.Contains("Dns", Keys(report));
        Assert.Contains("WindowsPolicy", Keys(report));
        Assert.Contains("OneLink", Keys(report));
    }

    [Fact]
    public async Task No_links_at_all_is_said_plainly_and_dns_is_not_blamed()
    {
        var report = await Doctor([]).RunAsync([]);

        Assert.Equal(["NoLinks"], Keys(report));
    }

    [Theory]
    [InlineData("fl=1\nip=84.54.1.2\nts=1", "84.54.1.2")]
    [InlineData("ip=2a02:1234::1\n", "2a02:1234::1")]
    [InlineData("ip=not-an-address\n", null)]
    [InlineData("<html></html>", null)]
    public void The_exit_address_is_read_from_the_trace(string trace, string? expected)
    {
        Assert.Equal(expected, NetworkDoctor.ExitAddressOf(trace));
    }
}
