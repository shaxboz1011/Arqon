using System.Net;
using Arqon.Core.Steering;

namespace Arqon.Core.Tests.Steering;

public class WinDivertFilterTests
{
    private static IPAddress Ip(string s) => IPAddress.Parse(s);

    [Fact]
    public void Captures_the_default_address_going_out_and_the_other_links_coming_in()
    {
        var filter = WinDivertFilter.Build(Ip("192.168.1.5"), [Ip("192.168.42.129"), Ip("10.8.0.2")]);

        Assert.Equal(
            "ip and (tcp or udp) and !loopback and ((outbound and ip.SrcAddr == 192.168.1.5) or " +
            "(inbound and (ip.DstAddr == 192.168.42.129 or ip.DstAddr == 10.8.0.2)))",
            filter);
    }

    [Fact]
    public void With_no_other_link_there_is_nothing_to_capture()
    {
        Assert.Null(WinDivertFilter.Build(Ip("192.168.1.5"), []));
    }

    [Fact]
    public void The_default_address_is_never_listed_as_a_target()
    {
        var filter = WinDivertFilter.Build(Ip("192.168.1.5"), [Ip("192.168.1.5"), Ip("192.168.42.129")]);

        Assert.DoesNotContain("ip.DstAddr == 192.168.1.5", filter);
    }

    [Fact]
    public void Ipv6_addresses_are_ignored()
    {
        Assert.Null(WinDivertFilter.Build(Ip("192.168.1.5"), [Ip("fe80::1")]));
    }
}
