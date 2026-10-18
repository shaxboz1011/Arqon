using Arqon.Core.Torrents;

namespace Arqon.Core.Tests.Torrents;

public class TorrentInputTests
{
    [Theory]
    [InlineData("magnet:?xt=urn:btih:c12fe1c06bba254a9dc9f519b335aa7c1367a88a&dn=ubuntu")]
    [InlineData("MAGNET:?xt=urn:btih:c12fe1c06bba254a9dc9f519b335aa7c1367a88a")]
    [InlineData("  magnet:?xt=urn:btih:c12fe1c06bba254a9dc9f519b335aa7c1367a88a  ")]
    public void Magnet_links_are_recognised(string text)
    {
        Assert.Equal(InputKind.Magnet, TorrentInput.Classify(text));
    }

    [Theory]
    [InlineData("https://releases.ubuntu.com/24.04/ubuntu.iso.torrent")]
    [InlineData("http://example.com/files/Movie.TORRENT")]
    [InlineData("https://example.com/get/file.torrent?token=abc&x=1")]
    public void Links_to_a_torrent_file_are_recognised(string text)
    {
        Assert.Equal(InputKind.TorrentUrl, TorrentInput.Classify(text));
    }

    [Theory]
    [InlineData(@"C:\Users\me\Downloads\ubuntu.torrent")]
    [InlineData("\"C:\\Users\\me\\Downloads\\my file.torrent\"")]
    [InlineData(@"file:///C:/Users/me/Downloads/ubuntu.torrent")]
    public void A_torrent_file_on_this_computer_is_recognised(string text)
    {
        Assert.Equal(InputKind.TorrentFile, TorrentInput.Classify(text));
    }

    [Theory]
    [InlineData("https://example.com/file.zip")]
    [InlineData("http://example.com/")]
    [InlineData("https://example.com/torrent-guide.html")]   // "torrent" in the name is not a .torrent file
    public void Ordinary_links_stay_ordinary_downloads(string text)
    {
        Assert.Equal(InputKind.Http, TorrentInput.Classify(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello")]
    [InlineData("ftp://example.com/a.iso")]
    [InlineData(@"C:\Users\me\notes.txt")]
    [InlineData("relative\\path.torrent")]
    public void Anything_else_is_invalid(string text)
    {
        Assert.Equal(InputKind.Invalid, TorrentInput.Classify(text));
    }

    [Fact]
    public void A_path_copied_from_explorer_loses_its_quotes_and_a_file_url_becomes_a_path()
    {
        Assert.Equal(@"C:\Users\me\a b.torrent", TorrentInput.LocalPath("\"C:\\Users\\me\\a b.torrent\""));
        Assert.Equal(@"C:\Users\me\a b.torrent", TorrentInput.LocalPath("file:///C:/Users/me/a%20b.torrent"));
    }
}
