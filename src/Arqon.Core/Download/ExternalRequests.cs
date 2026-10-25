using Arqon.Core.Torrents;

namespace Arqon.Core.Download;

/// <summary>
/// Arqon opened with something to download (a magnet link or .torrent file from the browser or Explorer, "Open with")
/// while another copy is already running: the second copy leaves the request here and exits, the first picks it up.
/// </summary>
public sealed class ExternalRequests(string? directory = null)
{
    private const int MaxLength = 8192;

    private readonly string _directory = directory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arqon", "inbox");

    /// <summary>
    /// The arguments of the command line that are something to download; switches and anything else are ignored.
    /// An <c>arqon://add?url=…</c> link (from the browser extension) gives the link inside it.
    /// </summary>
    public static IReadOnlyList<string> FromArguments(IEnumerable<string> args) =>
        args.Select(a => a.Trim())
            .Select(a => ArqonLink.IsArqonLink(a) ? ArqonLink.Unwrap(a) ?? "" : a)
            .Where(a => !a.StartsWith("--") && TorrentInput.Classify(a) != InputKind.Invalid)
            .ToList();

    public void Post(string text)
    {
        Directory.CreateDirectory(_directory);
        var name = Guid.NewGuid().ToString("N");
        var temp = Path.Combine(_directory, name + ".tmp");
        File.WriteAllText(temp, text.Length > MaxLength ? text[..MaxLength] : text);
        File.Move(temp, Path.Combine(_directory, name + ".req"));   // renamed when complete, so it is never read half-written
    }

    /// <summary>Every waiting request, oldest first; they are removed as they are read.</summary>
    public IReadOnlyList<string> TakeAll()
    {
        if (!Directory.Exists(_directory)) return [];

        var taken = new List<string>();
        foreach (var file in Directory.GetFiles(_directory, "*.req").OrderBy(File.GetCreationTimeUtc))
        {
            try
            {
                var text = File.ReadAllText(file).Trim();
                File.Delete(file);
                if (text.Length > 0) taken.Add(text);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // another copy may be reading it right now; the next signal tries again
            }
        }
        return taken;
    }
}
