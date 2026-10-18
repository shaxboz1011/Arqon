using Arqon.Core.Torrents;

namespace Arqon.Core.Tests.Torrents;

public sealed class TorrentSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-source-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private const string Hash = "c12fe1c06bba254a9dc9f519b335aa7c1367a88a";

    [Fact]
    public void A_saved_copy_is_named_after_the_torrent_and_keeps_the_bytes()
    {
        var path = TorrentSource.SaveCopy([1, 2, 3], Hash, _dir);

        Assert.Equal(Path.Combine(_dir, Hash + ".torrent"), path);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
    }

    [Fact]
    public void Saving_the_same_torrent_again_just_replaces_the_copy()
    {
        TorrentSource.SaveCopy([1], Hash, _dir);
        var path = TorrentSource.SaveCopy([9, 9], Hash, _dir);

        Assert.Equal([9, 9], File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void The_hash_of_a_stored_torrent_comes_from_its_magnet_link_or_from_the_name_of_its_saved_copy()
    {
        Assert.Equal(Hash, TorrentSource.HashOf($"magnet:?xt=urn:btih:{Hash.ToUpperInvariant()}&dn=x"));
        Assert.Equal(Hash, TorrentSource.HashOf(Path.Combine(_dir, Hash + ".torrent")));
        Assert.Null(TorrentSource.HashOf("magnet:?xt=urn:btih:zzz"));
    }
}
