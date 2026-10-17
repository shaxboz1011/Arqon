using System.Text.Json;

namespace Arqon.Core.Download;

/// <summary>Which chunks of a partially downloaded file are already on disk.</summary>
public sealed record ResumeState(string Url, long Length, long ChunkSize, List<long[]> Completed)
{
    public static ResumeState? TryLoad(string path, Uri url, long length, long chunkSize)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var s = JsonSerializer.Deserialize<ResumeState>(File.ReadAllText(path));
            if (s is null || s.Url != url.ToString() || s.Length != length || s.ChunkSize != chunkSize) return null;
            return s;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this));
        File.Move(tmp, path, overwrite: true);
    }

    public HashSet<ByteRange> ToRanges() => Completed.Select(c => new ByteRange(c[0], c[1])).ToHashSet();
}
