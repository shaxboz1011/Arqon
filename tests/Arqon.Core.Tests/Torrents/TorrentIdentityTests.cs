using Arqon.Core.Torrents;
using MonoTorrent;

namespace Arqon.Core.Tests.Torrents;

public sealed class TorrentIdentityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-identity-" + Guid.NewGuid().ToString("N"));

    public TorrentIdentityTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void A_magnet_link_is_identified_by_its_info_hash_whatever_else_it_carries()
    {
        var plain = TorrentIdentity.OfMagnet("magnet:?xt=urn:btih:c12fe1c06bba254a9dc9f519b335aa7c1367a88a");
        var decorated = TorrentIdentity.OfMagnet("magnet:?xt=urn:btih:C12FE1C06BBA254A9DC9F519B335AA7C1367A88A&dn=Ubuntu&tr=udp%3A%2F%2Ftracker.example%3A80");

        Assert.NotNull(plain);
        Assert.Equal(plain, decorated);   // upper or lower case, with or without a name and trackers
    }

    [Theory]
    [InlineData("")]
    [InlineData("magnet:?xt=urn:btih:nothex")]
    [InlineData("https://example.com")]
    public void Anything_that_is_not_a_magnet_link_has_no_identity(string text)
    {
        Assert.Null(TorrentIdentity.OfMagnet(text));
    }

    [Fact]
    public async Task A_torrent_file_has_an_identity_and_a_name_and_an_unreadable_one_has_neither()
    {
        var data = Path.Combine(_dir, "movie.bin");
        await File.WriteAllBytesAsync(data, new byte[100_000]);
        var file = Path.Combine(_dir, "movie.torrent");
        await File.WriteAllBytesAsync(file, (await new TorrentCreator().CreateAsync(new TorrentFileSource(data))).Encode());
        var junk = Path.Combine(_dir, "junk.torrent");
        await File.WriteAllTextAsync(junk, "this is not a torrent");

        var info = TorrentIdentity.OfFile(file);

        Assert.NotNull(info);
        Assert.Equal("movie.bin", info!.Name);
        Assert.Equal(40, info.Hash.Length);
        Assert.Null(TorrentIdentity.OfFile(junk));
        Assert.Null(TorrentIdentity.OfFile(Path.Combine(_dir, "missing.torrent")));
    }

    [Fact]
    public async Task The_same_torrent_gives_the_same_hash_from_its_file_as_from_its_magnet_link()
    {
        var data = Path.Combine(_dir, "a.bin");
        await File.WriteAllBytesAsync(data, new byte[50_000]);
        var file = Path.Combine(_dir, "a.torrent");
        await File.WriteAllBytesAsync(file, (await new TorrentCreator().CreateAsync(new TorrentFileSource(data))).Encode());

        var fromFile = TorrentIdentity.OfFile(file)!;

        Assert.Equal(fromFile.Hash, TorrentIdentity.OfMagnet($"magnet:?xt=urn:btih:{fromFile.Hash}&dn=a"));
    }
}
