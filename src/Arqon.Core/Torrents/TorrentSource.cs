namespace Arqon.Core.Torrents;

/// <summary>
/// A torrent added from a .torrent file or link is kept as a copy in Arqon's data folder, named after its info hash, so
/// it still resumes when the original file has been moved or deleted, and the same torrent is never added twice.
/// </summary>
public static class TorrentSource
{
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arqon", "torrents");

    public static string SaveCopy(byte[] torrentBytes, string hash, string? directory = null)
    {
        var dir = directory ?? DefaultDirectory;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, hash + ".torrent");
        File.WriteAllBytes(path, torrentBytes);
        return path;
    }

    /// <summary>The info hash behind a stored torrent's source: a magnet link, or the saved copy of a .torrent file.</summary>
    public static string? HashOf(string source) =>
        source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)
            ? TorrentIdentity.OfMagnet(source)
            : Path.GetFileNameWithoutExtension(source).ToLowerInvariant();
}
