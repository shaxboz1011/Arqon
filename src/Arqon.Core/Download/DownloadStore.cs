using System.Text.Json;

namespace Arqon.Core.Download;

public enum DownloadKind { Http, Torrent }

/// <param name="Url">The link, a magnet link, or (for a torrent added from a file) the saved copy of the .torrent file.</param>
/// <param name="Path">A plain download: the file. A torrent: the folder it is saved into.</param>
/// <param name="Files">A torrent: the indexes of the files to download; null means all of them.</param>
public sealed record DownloadRecord(string Url, string Path, string Name, bool Completed, DownloadKind Kind = DownloadKind.Http,
    IReadOnlyList<int>? Files = null, string? Sha256 = null)
{
    /// <summary>Identifies the download in the queue. Torrents share a folder, so for them the source is the identity.</summary>
    public string Key => Kind == DownloadKind.Torrent ? Url : Path;
}

/// <summary>Remembers the download list so unfinished downloads can be resumed after a restart.</summary>
public sealed class DownloadStore(string path)
{
    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arqon", "downloads.json");

    public IReadOnlyList<DownloadRecord> Load()
    {
        try
        {
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<DownloadRecord>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<DownloadRecord> records)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(records.ToList()));
        File.Move(tmp, path, overwrite: true);
    }
}
