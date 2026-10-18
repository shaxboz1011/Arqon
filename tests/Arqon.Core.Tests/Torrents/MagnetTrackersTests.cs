using Arqon.Core.Torrents;
using MonoTorrent;

namespace Arqon.Core.Tests.Torrents;

public class MagnetTrackersTests
{
    private const string Hash = "c12fe1c06bba254a9dc9f519b335aa7c1367a88a";

    private static MagnetLink Parse(string text) => MagnetLink.Parse(text);

    [Fact]
    public void A_magnet_without_trackers_gets_the_public_ones_so_its_peers_can_be_found()
    {
        var link = MagnetTrackers.Complete(Parse($"magnet:?xt=urn:btih:{Hash}&dn=Ubuntu"));

        Assert.NotEmpty(link.AnnounceUrls);
        Assert.Contains(link.AnnounceUrls, u => u.StartsWith("udp://tracker.opentrackr.org"));
    }

    [Fact]
    public void The_torrent_itself_is_never_changed_only_its_trackers_are_added()
    {
        var original = Parse($"magnet:?xt=urn:btih:{Hash}&dn=Ubuntu&xl=12345");

        var link = MagnetTrackers.Complete(original);

        Assert.Equal(original.InfoHashes, link.InfoHashes);
        Assert.Equal("Ubuntu", link.Name);
        Assert.Equal(12345, link.Size);
    }

    [Fact]
    public void The_trackers_of_the_magnet_come_first_and_nothing_is_listed_twice()
    {
        var own = "udp://tracker.example.org:6969/announce";
        var link = MagnetTrackers.Complete(Parse(
            $"magnet:?xt=urn:btih:{Hash}&tr={Uri.EscapeDataString(own)}&tr={Uri.EscapeDataString("udp://tracker.opentrackr.org:1337/announce")}"));

        Assert.Equal(own, link.AnnounceUrls[0]);
        Assert.Equal(link.AnnounceUrls.Count, link.AnnounceUrls.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Completing_twice_gives_the_same_result()
    {
        var once = MagnetTrackers.Complete(Parse($"magnet:?xt=urn:btih:{Hash}"));
        var twice = MagnetTrackers.Complete(once);

        Assert.Equal(once.AnnounceUrls, twice.AnnounceUrls);
    }
}
