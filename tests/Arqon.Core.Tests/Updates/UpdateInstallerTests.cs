using System.Net;
using System.Security.Cryptography;
using System.Text;
using Arqon.Core.Updates;

namespace Arqon.Core.Tests.Updates;

public class UpdateInstallerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-update-" + Guid.NewGuid().ToString("N"));
    private string Exe => Path.Combine(_dir, "Arqon.exe");

    public UpdateInstallerTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Exe, "old program");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static readonly byte[] NewProgram = Encoding.UTF8.GetBytes(new string('N', 300_000));
    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private sealed class Server(byte[] exe, string shaFile, HttpStatusCode exeStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(".sha256")) return Task.FromResult(new HttpResponseMessage { Content = new StringContent(shaFile) });
            return Task.FromResult(new HttpResponseMessage(exeStatus) { Content = new ByteArrayContent(exe) });
        }
    }

    private static ReleaseInfo Release(bool withFiles = true) => new("v9.9.9", "https://github.com/shaxboz1011/Arqon/releases/tag/v9.9.9", "x",
        withFiles ? "https://github.com/shaxboz1011/Arqon/releases/download/v9.9.9/Arqon.exe" : null,
        withFiles ? "https://github.com/shaxboz1011/Arqon/releases/download/v9.9.9/Arqon.exe.sha256" : null);

    [Theory]
    [InlineData("b65f53efb6c95ebf0120192220556969cd5e266498a7419d1c26ef9fef56192f  Arqon.exe\n", "B65F53EFB6C95EBF0120192220556969CD5E266498A7419D1C26EF9FEF56192F")]
    [InlineData("b65f53efb6c95ebf0120192220556969cd5e266498a7419d1c26ef9fef56192f *Arqon.exe", "B65F53EFB6C95EBF0120192220556969CD5E266498A7419D1C26EF9FEF56192F")]
    [InlineData("B65F53EFB6C95EBF0120192220556969CD5E266498A7419D1C26EF9FEF56192F", "B65F53EFB6C95EBF0120192220556969CD5E266498A7419D1C26EF9FEF56192F")]
    public void A_checksum_file_is_read_in_the_usual_formats(string text, string expected)
    {
        Assert.Equal(expected, UpdateInstaller.ParseSha256(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a hash")]
    [InlineData("abc123")]
    [InlineData("zz5f53efb6c95ebf0120192220556969cd5e266498a7419d1c26ef9fef56192f")]
    public void Anything_else_is_not_a_checksum(string text)
    {
        Assert.Null(UpdateInstaller.ParseSha256(text));
    }

    [Fact]
    public async Task A_download_that_matches_the_checksum_is_kept_next_to_the_program()
    {
        var http = new HttpClient(new Server(NewProgram, Sha(NewProgram) + "  Arqon.exe"));
        var progress = new List<double>();

        var path = await UpdateInstaller.DownloadAsync(http, Release(), Exe, new Progress<double>(progress.Add));

        Assert.Equal(Exe + ".new", path);
        Assert.Equal(NewProgram, await File.ReadAllBytesAsync(path));
        Assert.Equal("old program", File.ReadAllText(Exe));   // nothing swapped yet
    }

    [Fact]
    public async Task A_download_that_does_not_match_is_deleted_and_changes_nothing()
    {
        var http = new HttpClient(new Server(NewProgram, new string('0', 64)));

        var ex = await Assert.ThrowsAsync<UpdateException>(() => UpdateInstaller.DownloadAsync(http, Release(), Exe));

        Assert.Contains("checksum", ex.Message);
        Assert.False(File.Exists(Exe + ".new"));
        Assert.Equal("old program", File.ReadAllText(Exe));
    }

    [Fact]
    public async Task A_release_without_the_files_cannot_be_installed_from()
    {
        var http = new HttpClient(new Server(NewProgram, Sha(NewProgram)));

        await Assert.ThrowsAsync<UpdateException>(() => UpdateInstaller.DownloadAsync(http, Release(withFiles: false), Exe));
    }

    [Fact]
    public async Task A_server_error_is_reported_and_leaves_nothing_behind()
    {
        var http = new HttpClient(new Server(NewProgram, Sha(NewProgram), HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<UpdateException>(() => UpdateInstaller.DownloadAsync(http, Release(), Exe));

        Assert.False(File.Exists(Exe + ".new"));
    }

    [Fact]
    public void The_swap_puts_the_new_program_in_place_and_keeps_the_old_one_aside()
    {
        File.WriteAllText(Exe + ".new", "new program");

        UpdateInstaller.Swap(Exe, Exe + ".new");

        Assert.Equal("new program", File.ReadAllText(Exe));
        Assert.Equal("old program", File.ReadAllText(Exe + ".old"));
        Assert.False(File.Exists(Exe + ".new"));
    }

    [Fact]
    public void The_swap_works_while_the_old_program_is_running()
    {
        // Windows allows renaming a file that is open for execution/reading with delete sharing; this holds it open the same way
        using var holding = new FileStream(Exe, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        File.WriteAllText(Exe + ".new", "new program");

        UpdateInstaller.Swap(Exe, Exe + ".new");

        Assert.Equal("new program", File.ReadAllText(Exe));
    }

    [Fact]
    public void A_second_update_replaces_the_leftover_old_file()
    {
        File.WriteAllText(Exe + ".old", "very old");
        File.WriteAllText(Exe + ".new", "new program");

        UpdateInstaller.Swap(Exe, Exe + ".new");

        Assert.Equal("old program", File.ReadAllText(Exe + ".old"));
    }

    [Fact]
    public void A_failed_swap_leaves_the_old_program_exactly_as_it_was()
    {
        // the new file does not exist, so the second step fails after the first succeeded
        var ex = Assert.Throws<UpdateException>(() => UpdateInstaller.Swap(Exe, Exe + ".new"));

        Assert.Contains("new Arqon.exe", ex.Message);
        Assert.Equal("old program", File.ReadAllText(Exe));
        Assert.False(File.Exists(Exe + ".old"));
    }

    [Fact]
    public void Cleaning_up_removes_what_an_update_left_behind()
    {
        File.WriteAllText(Exe + ".old", "x");
        File.WriteAllText(Exe + ".new", "y");

        UpdateInstaller.CleanUp(Exe);

        Assert.False(File.Exists(Exe + ".old"));
        Assert.False(File.Exists(Exe + ".new"));
        Assert.True(File.Exists(Exe));
    }

    [Fact]
    public void The_release_answer_names_the_program_and_its_checksum()
    {
        var json = """
        {"tag_name":"v9.9.9","name":"x","html_url":"https://github.com/shaxboz1011/Arqon/releases/tag/v9.9.9","assets":[
          {"name":"Arqon.exe.sha256","browser_download_url":"https://github.com/shaxboz1011/Arqon/releases/download/v9.9.9/Arqon.exe.sha256"},
          {"name":"Arqon.exe","browser_download_url":"https://github.com/shaxboz1011/Arqon/releases/download/v9.9.9/Arqon.exe"}]}
        """;

        var release = UpdateChecker.ParseLatest(json)!;

        Assert.Equal("https://github.com/shaxboz1011/Arqon/releases/download/v9.9.9/Arqon.exe", release.ExeUrl);
        Assert.Equal("https://github.com/shaxboz1011/Arqon/releases/download/v9.9.9/Arqon.exe.sha256", release.Sha256Url);
    }

    [Fact]
    public void A_file_link_that_points_anywhere_else_is_ignored()
    {
        var json = """
        {"tag_name":"v9.9.9","name":"x","html_url":"https://github.com/shaxboz1011/Arqon/releases/tag/v9.9.9","assets":[
          {"name":"Arqon.exe","browser_download_url":"https://evil.example/Arqon.exe"},
          {"name":"Arqon.exe.sha256","browser_download_url":"https://github.com/someone-else/Arqon/releases/download/v9/Arqon.exe.sha256"}]}
        """;

        var release = UpdateChecker.ParseLatest(json)!;

        Assert.Null(release.ExeUrl);
        Assert.Null(release.Sha256Url);
    }
}
