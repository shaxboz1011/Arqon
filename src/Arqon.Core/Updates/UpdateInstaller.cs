using System.Security.Cryptography;

namespace Arqon.Core.Updates;

public sealed class UpdateException(string message) : Exception(message);

/// <summary>
/// Updates the program in place: downloads the new <c>Arqon.exe</c> of a release, checks it against the SHA-256 published with the
/// release, and swaps it for the running copy. Windows lets a running program be renamed but not overwritten, so the old copy is
/// renamed to <c>Arqon.exe.old</c>, the new one takes its name, and the caller starts it; the next start deletes the old file.
/// If anything fails the old program is left exactly as it was.
/// </summary>
public static class UpdateInstaller
{
    /// <summary>The first 64 hex digits of a ".sha256" file ("hash  Arqon.exe"), or null when there are none.</summary>
    public static string? ParseSha256(string text)
    {
        var token = text.Split([' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return token is { Length: 64 } && token.All(Uri.IsHexDigit) ? token.ToUpperInvariant() : null;
    }

    /// <summary>
    /// Downloads the release's program next to <paramref name="currentExe"/> (same drive and folder rights, so the swap is a rename) and
    /// returns the path once it matches the published SHA-256.
    /// </summary>
    public static async Task<string> DownloadAsync(HttpClient http, ReleaseInfo release, string currentExe,
        IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (release.ExeUrl is null || release.Sha256Url is null)
            throw new UpdateException("This release has no Arqon.exe with a checksum to update from.");

        var expected = ParseSha256(await ReadText(http, release.Sha256Url, cancel).ConfigureAwait(false))
            ?? throw new UpdateException("The release's checksum file is not readable.");

        var target = currentExe + ".new";
        try
        {
            using var response = await http.GetAsync(release.ExeUrl, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false))
            await using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[128 * 1024];
                long done = 0;
                int n;
                while ((n = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, n), cancel).ConfigureAwait(false);
                    done += n;
                    if (total is > 0) progress?.Report((double)done / total.Value);
                }
            }

            string actual;
            await using (var check = File.OpenRead(target))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(check, cancel).ConfigureAwait(false));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("The downloaded file does not match the release's checksum. Nothing was changed.");
            return target;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            TryDelete(target);
            throw new UpdateException(ex is UnauthorizedAccessException
                ? "Arqon's folder is not writable here. Download the new version from the release page instead."
                : "Could not download the update: " + ex.Message);
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    /// <summary>
    /// Puts <paramref name="newExe"/> in the place of <paramref name="currentExe"/> (which may be running) and keeps the old file as
    /// <c>.old</c>. Undoes itself if the second step fails.
    /// </summary>
    public static void Swap(string currentExe, string newExe)
    {
        var old = currentExe + ".old";
        try
        {
            if (File.Exists(old)) File.Delete(old);
            File.Move(currentExe, old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(newExe);
            throw new UpdateException("Could not replace Arqon.exe: " + ex.Message);
        }

        try
        {
            File.Move(newExe, currentExe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            File.Move(old, currentExe);   // put the old program back
            TryDelete(newExe);
            throw new UpdateException("Could not put the new Arqon.exe in place: " + ex.Message);
        }
    }

    /// <summary>Deletes what an earlier update left behind. Call at start-up.</summary>
    public static void CleanUp(string currentExe)
    {
        TryDelete(currentExe + ".old");
        TryDelete(currentExe + ".new");
    }

    private static async Task<string> ReadText(HttpClient http, string url, CancellationToken cancel)
    {
        try
        {
            return await http.GetStringAsync(url, cancel).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("Could not download the update's checksum: " + ex.Message);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
