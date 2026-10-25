using System.Net;
using Arqon.Core.Download;
using Arqon.Core.Links;

namespace Arqon.Core.Tests.Download;

public class DownloadLinkPolicyTests
{
    private static readonly LinkInfo Wifi = new("wifi", "Wi-Fi", "", IPAddress.Parse("192.168.1.2"), null, LinkKind.Wifi, 0);
    private static readonly LinkInfo Phone = new("phone", "Phone", "", IPAddress.Parse("192.168.42.2"), null, LinkKind.Mobile, 0);
    private static readonly LinkInfo Cable = new("cable", "Ethernet", "", IPAddress.Parse("10.0.0.2"), null, LinkKind.Ethernet, 0);

    [Fact]
    public void Without_marks_every_link_is_used()
    {
        Assert.Equal([Wifi, Phone], DownloadLinkPolicy.Choose([Wifi, Phone], null, saveMobile: false));
    }

    [Fact]
    public void A_saver_link_is_left_out_while_another_link_is_up()
    {
        Assert.Equal([Wifi, Cable], DownloadLinkPolicy.Choose([Wifi, Phone, Cable], ["PHONE"], saveMobile: false));
    }

    [Fact]
    public void The_last_link_is_used_even_when_it_is_a_saver_link()
    {
        Assert.Equal([Phone], DownloadLinkPolicy.Choose([Phone], ["phone"], saveMobile: true));
        Assert.Equal([Wifi, Phone], DownloadLinkPolicy.Choose([Wifi, Phone], ["phone", "wifi"], saveMobile: false));
    }

    [Fact]
    public void At_the_daily_limit_phone_links_are_saved_when_asked()
    {
        Assert.Equal([Wifi], DownloadLinkPolicy.Choose([Wifi, Phone], [], saveMobile: true));
    }

    [Fact]
    public void No_links_stay_no_links()
    {
        Assert.Empty(DownloadLinkPolicy.Choose([], ["x"], saveMobile: true));
    }
}
