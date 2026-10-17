using System.Text.Json;

namespace Arqon.Core.Download;

public sealed record DownloadRecord(string Url, string Path, string Name, bool Completed);

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
