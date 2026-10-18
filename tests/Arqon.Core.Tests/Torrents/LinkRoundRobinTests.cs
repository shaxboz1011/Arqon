using System.Net;
using Arqon.Core.Links;
using Arqon.Core.Torrents;

namespace Arqon.Core.Tests.Torrents;

public class LinkRoundRobinTests
{
    private static LinkInfo Link(string id, string address) =>
        new(id, id, id, IPAddress.Parse(address), null, LinkKind.Ethernet, 0);

    [Fact]
    public void New_connections_take_the_links_in_turn()
    {
        var links = new[] { Link("wifi", "10.0.0.2"), Link("phone", "10.0.1.2") };
        var rr = new LinkRoundRobin(() => links, _ => true);

        var picked = Enumerable.Range(0, 6).Select(_ => rr.Next()!.Id).ToList();

        Assert.Equal(["wifi", "phone", "wifi", "phone", "wifi", "phone"], picked);
    }

    [Fact]
    public void A_link_that_is_down_is_skipped_while_another_is_alive()
    {
        var links = new[] { Link("wifi", "10.0.0.2"), Link("phone", "10.0.1.2") };
        var rr = new LinkRoundRobin(() => links, l => l.Id != "phone");

        Assert.All(Enumerable.Range(0, 5), _ => Assert.Equal("wifi", rr.Next()!.Id));
    }

    [Fact]
    public void When_every_link_looks_down_they_are_all_still_used_rather_than_connecting_to_nothing()
    {
        var links = new[] { Link("wifi", "10.0.0.2"), Link("phone", "10.0.1.2") };
        var rr = new LinkRoundRobin(() => links, _ => false);

        Assert.Equal(["wifi", "phone"], new[] { rr.Next()!.Id, rr.Next()!.Id });
    }

    [Fact]
    public void No_links_means_no_choice_so_windows_decides()
    {
        var rr = new LinkRoundRobin(() => [], _ => true);

        Assert.Null(rr.Next());
    }

    [Fact]
    public void A_link_that_appears_later_joins_the_rotation()
    {
        var links = new List<LinkInfo> { Link("wifi", "10.0.0.2") };
        var rr = new LinkRoundRobin(() => links, _ => true);
        Assert.Equal("wifi", rr.Next()!.Id);

        links.Add(Link("phone", "10.0.1.2"));

        var picked = Enumerable.Range(0, 4).Select(_ => rr.Next()!.Id).ToHashSet();
        Assert.Contains("phone", picked);
    }

    [Fact]
    public void It_counts_how_many_connections_each_link_received()
    {
        var links = new[] { Link("wifi", "10.0.0.2"), Link("phone", "10.0.1.2") };
        var rr = new LinkRoundRobin(() => links, _ => true);

        for (var i = 0; i < 5; i++) rr.Next();

        Assert.Equal(3, rr.Counts["wifi"]);
        Assert.Equal(2, rr.Counts["phone"]);
    }
}
