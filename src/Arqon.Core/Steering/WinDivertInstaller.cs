using System.IO.Compression;
using System.Security.Cryptography;

namespace Arqon.Core.Steering;

public sealed class DriverSetupException(string message) : Exception(message);

/// <summary>
/// Downloads the official WinDivert release and installs its two files into a folder. The archive must have exactly the SHA-256 this
/// version of Arqon was built to expect, and each file is checked again after it is unpacked, so nothing but the genuine driver
/// is ever installed. WinDivert is signed by its author; whether Windows accepts the signature is checked by <see cref="DriverSignature"/>.
/// </summary>
public sealed class WinDivertInstaller(HttpClient http, string destination, string url = WinDivertInstaller.Url,
    string archiveSha256 = WinDivertInstaller.ArchiveSha256, string dllSha256 = WinDivertInstaller.DllSha256,
    string sysSha256 = WinDivertInstaller.SysSha256)
{
    public const string Version = "2.2.2";
    public const string Url = "https://github.com/basil00/WinDivert/releases/download/v2.2.2/WinDivert-2.2.2-A.zip";
    public const string ArchiveSha256 = "63cb41763bb4b20f600b6de04e991a9c2be73279e317d4d82f237b150c5f3f15";
    public const string DllSha256 = "c1e060ee19444a259b2162f8af0f3fe8c4428a1c6f694dce20de194ac8d7d9a2";
    public const string SysSha256 = "8da085332782708d8767bcace5327a6ec7283c17cfb85e40b03cd2323a90ddc2";

    /// <param name="progress">0 to 1 while the archive downloads.</param>
    public async Task InstallAsync(IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(destination);
        var work = Path.Combine(destination, "download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var zip = Path.Combine(work, "windivert.zip");
            await Download(zip, progress, cancel).ConfigureAwait(false);

            var hash = await HashOf(zip, cancel).ConfigureAwait(false);
            if (!hash.Equals(archiveSha256, StringComparison.OrdinalIgnoreCase))
                throw new DriverSetupException("The downloaded WinDivert archive is not the expected file (its SHA-256 differs). Nothing was installed.");

            var dll = Unpack(zip, "x64/WinDivert.dll", Path.Combine(work, "WinDivert.dll"), dllSha256);
            var sys = Unpack(zip, "x64/WinDivert64.sys", Path.Combine(work, "WinDivert64.sys"), sysSha256);

            // the old files may be loaded by a running steering session: move them aside rather than fail
            Place(dll, Path.Combine(destination, "WinDivert.dll"));
            Place(sys, Path.Combine(destination, "WinDivert64.sys"));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private async Task Download(string path, IProgress<double>? progress, CancellationToken cancel)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            await using var target = File.Create(path);
            var buffer = new byte[64 * 1024];
            long done = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, n), cancel).ConfigureAwait(false);
                done += n;
                if (total is > 0) progress?.Report((double)done / total.Value);
            }
        }
        catch (HttpRequestException ex)
        {
            throw new DriverSetupException("Could not download WinDivert: " + ex.Message);
        }
    }

    private static async Task<string> HashOf(string path, CancellationToken cancel)
    {
        // an antivirus may block the read of a file it dislikes: say so instead of failing with a bare I/O error
        try
        {
            await using var stream = File.OpenRead(path);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancel).ConfigureAwait(false));
        }
        catch (IOException ex)
        {
            throw new DriverSetupException("The archive could not be read, an antivirus probably blocked it: " + ex.Message);
        }
    }

    private static string Unpack(string zipPath, string entrySuffix, string target, string expectedSha256)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/" + entrySuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new DriverSetupException("The archive does not contain " + entrySuffix + ".");
        entry.ExtractToFile(target, overwrite: true);
        using var stream = File.OpenRead(target);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new DriverSetupException(entrySuffix + " is not the expected file (its SHA-256 differs). Nothing was installed.");
        return target;
    }

    private static void Place(string from, string to)
    {
        if (File.Exists(to))
        {
            var aside = to + ".old";
            try { File.Delete(aside); } catch (IOException) { }
            try { File.Move(to, aside, overwrite: true); } catch (IOException) { /* in use: the copy below reports it */ }
        }
        File.Copy(from, to, overwrite: true);
    }
}
