using Arqon.Core.Download;

namespace Arqon.Core.Tests.Download;

public class DownloadLinkDetectorTests
{
    [Theory]
    [InlineData("https://example.com/files/setup.exe")]
    [InlineData("http://example.com/a/b/movie.MKV")]
    [InlineData("https://example.com/archive.tar.gz")]
    [InlineData("https://cdn.example.com/Game%20Installer.msi?token=abc&x=1")]
    [InlineData("  https://example.com/disk.iso  ")]
    [InlineData("https://example.com/book.pdf")]
    public void Recognises_direct_links_to_files(string text)
    {
        Assert.NotNull(DownloadLinkDetector.Detect(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello world")]
    [InlineData("https://example.com/")]
    [InlineData("https://example.com/page.html")]
    [InlineData("https://example.com/watch?v=abc")]
    [InlineData("ftp://example.com/file.zip")]
    [InlineData("magnet:?xt=urn:btih:abc")]
    [InlineData("file:///C:/setup.exe")]
    [InlineData("https://example.com/setup.exe and some words")]
    [InlineData("https://example.com/a.zip\nhttps://example.com/b.zip")]
    public void Ignores_everything_else(string? text)
    {
        Assert.Null(DownloadLinkDetector.Detect(text));
    }

    [Fact]
    public void Returns_the_trimmed_address()
    {
        Assert.Equal("https://example.com/a.zip", DownloadLinkDetector.Detect("  https://example.com/a.zip \r\n")!.ToString());
    }

    [Fact]
    public void Absurdly_long_text_is_ignored()
    {
        Assert.Null(DownloadLinkDetector.Detect("https://example.com/" + new string('a', 5000) + ".zip"));
    }
}
