using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Arqon.Core.Steering;

namespace Arqon.Core.Tests.Steering;

public class DriverSetupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-driver-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static readonly byte[] Dll = Encoding.ASCII.GetBytes("fake dll");
    private static readonly byte[] Sys = Encoding.ASCII.GetBytes("fake sys");
    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

    private static byte[] Zip(params (string Name, byte[] Data)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, data) in files)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(data);
            }
        return ms.ToArray();
    }

    private static readonly byte[] GoodZip = Zip(("WinDivert-2.2.2-A/x64/WinDivert.dll", Dll), ("WinDivert-2.2.2-A/x64/WinDivert64.sys", Sys),
        ("WinDivert-2.2.2-A/x86/WinDivert.dll", [1, 2]), ("WinDivert-2.2.2-A/x86/WinDivert64.sys", [3, 4]));

    private sealed class Server(byte[] body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
    }

    private WinDivertInstaller Installer(byte[] zip, string? archive = null, string? dll = null, string? sys = null, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new HttpClient(new Server(zip, status)), _dir, "https://github.com/x/y.zip", archive ?? Sha(zip), dll ?? Sha(Dll), sys ?? Sha(Sys));

    [Fact]
    public async Task The_genuine_archive_installs_the_two_64_bit_files_and_nothing_else()
    {
        await Installer(GoodZip).InstallAsync();

        Assert.Equal(Dll, await File.ReadAllBytesAsync(Path.Combine(_dir, "WinDivert.dll")));
        Assert.Equal(Sys, await File.ReadAllBytesAsync(Path.Combine(_dir, "WinDivert64.sys")));
        Assert.Equal(["WinDivert.dll", "WinDivert64.sys"], Directory.GetFileSystemEntries(_dir).Select(e => Path.GetFileName(e)!).Order().ToArray());
    }

    [Fact]
    public async Task An_archive_with_another_hash_installs_nothing()
    {
        var ex = await Assert.ThrowsAsync<DriverSetupException>(() => Installer(GoodZip, archive: new string('A', 64)).InstallAsync());

        Assert.Contains("SHA-256", ex.Message);
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public async Task A_file_inside_the_archive_that_is_not_the_expected_one_installs_nothing()
    {
        var tampered = Zip(("A/x64/WinDivert.dll", Encoding.ASCII.GetBytes("evil")), ("A/x64/WinDivert64.sys", Sys));

        await Assert.ThrowsAsync<DriverSetupException>(() => Installer(tampered, dll: Sha(Dll)).InstallAsync());

        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public async Task An_archive_without_the_files_is_refused()
    {
        var empty = Zip(("readme.txt", [1]));

        var ex = await Assert.ThrowsAsync<DriverSetupException>(() => Installer(empty).InstallAsync());

        Assert.Contains("does not contain", ex.Message);
    }

    [Fact]
    public async Task A_download_error_is_reported_in_plain_words()
    {
        var ex = await Assert.ThrowsAsync<DriverSetupException>(() => Installer(GoodZip, status: HttpStatusCode.NotFound).InstallAsync());

        Assert.Contains("download", ex.Message);
    }

    [Fact]
    public async Task Files_from_an_earlier_install_are_replaced()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, "WinDivert.dll"), "older");

        await Installer(GoodZip).InstallAsync();

        Assert.Equal(Dll, await File.ReadAllBytesAsync(Path.Combine(_dir, "WinDivert.dll")));
    }

    // ---- the whole setup: exclusion first, then install, then the checks

    private DriverSetup Setup(Func<string, bool> exclude, Func<Task> install, Func<string, bool>? valid = null, List<string>? log = null) =>
        new(_dir, f => { log?.Add("exclude"); return exclude(f); },
            async (_, _) => { log?.Add("install"); await install(); },
            valid ?? (_ => true));

    private Task PlaceFiles()
    {
        File.WriteAllText(Path.Combine(_dir, WinDivertLocator.DllName), "dll");
        File.WriteAllText(Path.Combine(_dir, WinDivertLocator.SysName), "sys");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Defender_is_told_before_the_driver_is_downloaded()
    {
        var log = new List<string>();

        var result = await Setup(_ => true, PlaceFiles, log: log).RunAsync();

        Assert.Equal(["exclude", "install"], log);
        Assert.True(result.ExclusionAdded);
    }

    [Fact]
    public async Task The_setup_still_runs_when_the_exclusion_could_not_be_added()
    {
        var result = await Setup(_ => false, PlaceFiles).RunAsync();

        Assert.False(result.ExclusionAdded);
    }

    [Fact]
    public async Task Files_that_vanish_after_the_install_are_reported_as_an_antivirus_removal()
    {
        var ex = await Assert.ThrowsAsync<DriverSetupException>(() => Setup(_ => false, () => Task.CompletedTask).RunAsync());

        Assert.Contains("antivirus", ex.Message);
        Assert.Contains(_dir, ex.Message);
    }

    [Fact]
    public async Task A_driver_that_windows_does_not_accept_is_removed_again()
    {
        await Assert.ThrowsAsync<DriverSetupException>(() => Setup(_ => true, PlaceFiles, valid: _ => false).RunAsync());

        Assert.False(File.Exists(Path.Combine(_dir, WinDivertLocator.SysName)));
        Assert.False(File.Exists(Path.Combine(_dir, WinDivertLocator.DllName)));
    }

    [Fact]
    public void The_locator_needs_both_files()
    {
        Directory.CreateDirectory(_dir);
        Assert.False(WinDivertLocator.HasDriver(_dir));
        File.WriteAllText(Path.Combine(_dir, WinDivertLocator.DllName), "x");
        Assert.False(WinDivertLocator.HasDriver(_dir));
        File.WriteAllText(Path.Combine(_dir, WinDivertLocator.SysName), "x");
        Assert.True(WinDivertLocator.HasDriver(_dir));
        Assert.Equal(_dir, WinDivertLocator.Find(_dir));
    }

    [Fact]
    public void The_pinned_hashes_are_well_formed()
    {
        foreach (var h in new[] { WinDivertInstaller.ArchiveSha256, WinDivertInstaller.DllSha256, WinDivertInstaller.SysSha256 })
        {
            Assert.Equal(64, h.Length);
            Assert.All(h, c => Assert.True(Uri.IsHexDigit(c)));
        }
    }
}
