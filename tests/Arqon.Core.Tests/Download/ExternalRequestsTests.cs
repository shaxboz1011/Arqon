using Arqon.Core.Download;

namespace Arqon.Core.Tests.Download;

public sealed class ExternalRequestsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-inbox-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void A_request_posted_by_a_second_copy_is_taken_once_by_the_first()
    {
        var inbox = new ExternalRequests(_dir);
        inbox.Post("magnet:?xt=urn:btih:c12fe1c06bba254a9dc9f519b335aa7c1367a88a");
        inbox.Post(@"C:\Users\me\Downloads\a.torrent");

        var taken = inbox.TakeAll();

        Assert.Equal(2, taken.Count);
        Assert.Contains(@"C:\Users\me\Downloads\a.torrent", taken);
        Assert.Empty(inbox.TakeAll());   // gone after they were taken
    }

    [Fact]
    public void An_empty_inbox_is_fine_even_when_the_folder_does_not_exist()
    {
        Assert.Empty(new ExternalRequests(Path.Combine(_dir, "nothing")).TakeAll());
    }

    [Theory]
    [InlineData("magnet:?xt=urn:btih:c12fe1c06bba254a9dc9f519b335aa7c1367a88a")]
    [InlineData(@"C:\Users\me\a.torrent")]
    [InlineData("https://example.com/file.torrent")]
    [InlineData("https://example.com/file.zip")]
    public void Command_line_arguments_that_are_downloads_are_kept(string arg)
    {
        Assert.Equal([arg], ExternalRequests.FromArguments([arg]));
    }

    [Fact]
    public void Switches_and_anything_else_on_the_command_line_are_ignored()
    {
        var kept = ExternalRequests.FromArguments(["--tray", "--demo", "--restarted", "hello", @"C:\Windows\System32\cmd.exe", "ftp://x/y"]);

        Assert.Empty(kept);
    }

    [Fact]
    public void Oversized_requests_are_cut_instead_of_filling_the_disk()
    {
        var inbox = new ExternalRequests(_dir);
        inbox.Post("magnet:?xt=urn:btih:" + new string('a', 100_000));

        Assert.True(inbox.TakeAll().Single().Length <= 8192);
    }
}
