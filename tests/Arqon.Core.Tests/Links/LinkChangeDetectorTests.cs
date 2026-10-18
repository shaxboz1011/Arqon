using System.Net;
using Arqon.Core.Links;

namespace Arqon.Core.Tests.Links;

public class LinkChangeDetectorTests
{
    private static LinkInfo L(string id, string name = "") =>
        new(id, name == "" ? id : name, "", IPAddress.Loopback, null, LinkKind.Wifi, 0);

    [Fact]
    public void The_first_look_is_not_a_change()
    {
        var d = new LinkChangeDetector();

        Assert.Empty(d.Update([L("wifi"), L("phone")]));
    }

    [Fact]
    public void A_new_link_is_reported_once()
    {
        var d = new LinkChangeDetector();
        d.Update([L("wifi")]);

        var first = d.Update([L("wifi"), L("phone", "Phone")]);
        var second = d.Update([L("wifi"), L("phone", "Phone")]);

        var change = Assert.Single(first);
        Assert.Equal(LinkChangeKind.Appeared, change.Kind);
        Assert.Equal("Phone", change.Link.Name);
        Assert.Empty(second);
    }

    [Fact]
    public void A_link_must_be_missing_twice_in_a_row_before_it_counts_as_lost()
    {
        var d = new LinkChangeDetector();
        d.Update([L("wifi"), L("phone")]);

        Assert.Empty(d.Update([L("wifi")]));
        var lost = Assert.Single(d.Update([L("wifi")]));
        Assert.Equal(LinkChangeKind.Lost, lost.Kind);
        Assert.Equal("phone", lost.Link.Id);
    }

    [Fact]
    public void A_one_poll_flicker_is_ignored()
    {
        var d = new LinkChangeDetector();
        d.Update([L("wifi"), L("phone")]);

        d.Update([L("wifi")]);
        var back = d.Update([L("wifi"), L("phone")]);

        Assert.Empty(back);
    }

    [Fact]
    public void A_lost_link_that_returns_is_an_appearance_again()
    {
        var d = new LinkChangeDetector();
        d.Update([L("wifi"), L("phone")]);
        d.Update([L("wifi")]);
        d.Update([L("wifi")]);

        var back = Assert.Single(d.Update([L("wifi"), L("phone")]));

        Assert.Equal(LinkChangeKind.Appeared, back.Kind);
    }

    [Fact]
    public void The_lost_link_keeps_its_name_for_the_message()
    {
        var d = new LinkChangeDetector();
        d.Update([L("phone", "Pixel 8")]);
        d.Update([]);

        Assert.Equal("Pixel 8", d.Update([]).Single().Link.Name);
    }
}
