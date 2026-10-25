using Arqon.Core.Download;

namespace Arqon.Core.Tests.Download;

public class ArqonLinkTests
{
    [Theory]
    [InlineData("https://example.com/files/big file.iso?x=1&y=2")]
    [InlineData("http://example.com/a.torrent")]
    [InlineData("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Debian")]
    public void A_built_link_gives_back_what_it_carries(string url)
    {
        Assert.Equal(url, ArqonLink.Unwrap(ArqonLink.Build(url)));
    }

    [Theory]
    [InlineData("arqon:add?url=https%3A%2F%2Fexample.com%2Fa.zip")]
    [InlineData("ARQON://ADD/?URL=https%3A%2F%2Fexample.com%2Fa.zip")]
    [InlineData("\"arqon://add?from=menu&url=https%3A%2F%2Fexample.com%2Fa.zip\"")]
    public void Spelling_case_slashes_and_other_parameters_do_not_matter(string link)
    {
        Assert.Equal("https://example.com/a.zip", ArqonLink.Unwrap(link));
    }

    [Theory]
    [InlineData("arqon://add?url=C%3A%5CWindows%5Cwin.ini")]
    [InlineData("arqon://add?url=file%3A%2F%2F%2FC%3A%2Fx.torrent")]
    [InlineData("arqon://add?url=ftp%3A%2F%2Fexample.com%2Fa")]
    [InlineData("arqon://delete?url=https%3A%2F%2Fexample.com%2Fa.zip")]
    [InlineData("arqon://add")]
    [InlineData("arqon://add?name=x")]
    [InlineData("https://example.com/a.zip")]
    [InlineData(null)]
    public void Anything_but_a_web_or_magnet_link_to_add_is_refused(string? link)
    {
        Assert.Null(ArqonLink.Unwrap(link));
    }

    [Fact]
    public void The_command_line_turns_an_arqon_link_into_the_link_inside_and_drops_a_bad_one()
    {
        var kept = ExternalRequests.FromArguments([ArqonLink.Build("https://example.com/a.zip"), "arqon://add?url=C%3A%5Cx.torrent"]);

        Assert.Equal(["https://example.com/a.zip"], kept);
    }
}
