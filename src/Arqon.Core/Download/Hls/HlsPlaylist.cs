using System.Globalization;

namespace Arqon.Core.Download.Hls;

/// <summary>How a segment is encrypted: AES-128 (CBC) with the key at <see cref="Uri"/>; no IV means the sequence number.</summary>
public sealed record HlsKey(Uri Uri, byte[]? Iv);

public sealed record HlsSegment(long Sequence, Uri Uri, double Duration, HlsKey? Key);

public sealed record HlsVariant(Uri Uri, long Bandwidth, string? Resolution);

/// <summary>A media playlist: the segments of one stream, plus the init segment of an fMP4 stream.</summary>
public sealed record HlsMedia(IReadOnlyList<HlsSegment> Segments, Uri? InitSegment, bool Ended)
{
    public double Duration => Segments.Sum(s => s.Duration);

    /// <summary>".mp4" for fragmented MP4 streams, ".ts" for MPEG transport streams.</summary>
    public string Extension => InitSegment is null ? ".ts" : ".mp4";
}

/// <summary>A master playlist: the same video in several qualities.</summary>
public sealed record HlsMaster(IReadOnlyList<HlsVariant> Variants)
{
    /// <summary>The best quality (highest bandwidth).</summary>
    public HlsVariant Best => Variants.MaxBy(v => v.Bandwidth)!;
}

/// <summary>Reads HLS playlists (RFC 8216) far enough to download a whole video: no live streams, no DRM.</summary>
public static class HlsPlaylist
{
    private static readonly string[] GenericNames = ["index", "playlist", "master", "prog_index", "chunklist", "manifest", "video", "stream", "main"];

    /// <summary>True when the link points to an HLS playlist (.m3u8).</summary>
    public static bool IsPlaylistUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A file name for the video: the playlist's own name, or the folder above it when the name says nothing ("index.m3u8").
    /// </summary>
    public static string FileNameFor(Uri playlist)
    {
        var parts = playlist.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
        var stem = parts.Count > 0 ? Path.GetFileNameWithoutExtension(parts[^1]) : "";
        var useFolder = stem.Length == 0 || GenericNames.Contains(stem, StringComparer.OrdinalIgnoreCase) || stem.All(c => char.IsDigit(c) || c is '_' or '-');
        if (useFolder && parts.Count >= 2) stem = parts[^2];
        var clean = string.Concat(stem.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return (clean.Length == 0 ? "video" : clean) + ".ts";
    }

    /// <summary>Parses either kind of playlist. Throws <see cref="FormatException"/> or <see cref="NotSupportedException"/>.</summary>
    public static object Parse(string text, Uri baseUri)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0 || lines[0] != "#EXTM3U") throw new FormatException("Not an HLS playlist");
        return lines.Any(l => l.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal)) ? ParseMaster(lines, baseUri) : ParseMedia(lines, baseUri);
    }

    private static HlsMaster ParseMaster(List<string> lines, Uri baseUri)
    {
        var variants = new List<HlsVariant>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal)) continue;
            var attributes = Attributes(lines[i]["#EXT-X-STREAM-INF:".Length..]);
            var next = lines.Skip(i + 1).FirstOrDefault(l => !l.StartsWith('#'));
            if (next is null) continue;
            long.TryParse(attributes.GetValueOrDefault("BANDWIDTH"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bandwidth);
            variants.Add(new HlsVariant(new Uri(baseUri, next), bandwidth, attributes.GetValueOrDefault("RESOLUTION")));
        }
        if (variants.Count == 0) throw new FormatException("The master playlist lists no streams");
        return new HlsMaster(variants);
    }

    private static HlsMedia ParseMedia(List<string> lines, Uri baseUri)
    {
        var segments = new List<HlsSegment>();
        long sequence = 0;
        double duration = 0;
        HlsKey? key = null;
        Uri? init = null;
        var ended = false;

        foreach (var line in lines.Skip(1))
        {
            if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.Ordinal))
                sequence = long.Parse(line["#EXT-X-MEDIA-SEQUENCE:".Length..], CultureInfo.InvariantCulture);
            else if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
                duration = double.Parse(line["#EXTINF:".Length..].Split(',')[0], CultureInfo.InvariantCulture);
            else if (line.StartsWith("#EXT-X-KEY:", StringComparison.Ordinal))
                key = ParseKey(Attributes(line["#EXT-X-KEY:".Length..]), baseUri);
            else if (line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal))
            {
                var map = Attributes(line["#EXT-X-MAP:".Length..]);
                if (map.ContainsKey("BYTERANGE")) throw new NotSupportedException("Byte ranges in HLS playlists are not supported yet");
                init = new Uri(baseUri, map.GetValueOrDefault("URI") ?? throw new FormatException("EXT-X-MAP without a URI"));
            }
            else if (line.StartsWith("#EXT-X-BYTERANGE", StringComparison.Ordinal))
                throw new NotSupportedException("Byte ranges in HLS playlists are not supported yet");
            else if (line == "#EXT-X-ENDLIST")
                ended = true;
            else if (!line.StartsWith('#'))
            {
                segments.Add(new HlsSegment(sequence++, new Uri(baseUri, line), duration, key));
                duration = 0;
            }
        }
        if (segments.Count == 0) throw new FormatException("The playlist has no segments");
        if (!ended) throw new NotSupportedException("This is a live stream; only finished videos can be downloaded");
        return new HlsMedia(segments, init, ended);
    }

    private static HlsKey? ParseKey(Dictionary<string, string> attributes, Uri baseUri)
    {
        var method = attributes.GetValueOrDefault("METHOD") ?? "NONE";
        if (method == "NONE") return null;
        if (method != "AES-128") throw new NotSupportedException($"This video is protected ({method}) and cannot be downloaded");
        var uri = attributes.GetValueOrDefault("URI") ?? throw new FormatException("EXT-X-KEY without a URI");
        byte[]? iv = null;
        if (attributes.TryGetValue("IV", out var hex))
        {
            hex = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
            iv = Convert.FromHexString(hex.PadLeft(32, '0'));
        }
        return new HlsKey(new Uri(baseUri, uri), iv);
    }

    /// <summary>KEY=value,KEY="quoted, value" as a dictionary.</summary>
    public static Dictionary<string, string> Attributes(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < text.Length)
        {
            var eq = text.IndexOf('=', i);
            if (eq < 0) break;
            var name = text[i..eq].Trim().TrimStart(',').Trim();
            string value;
            if (eq + 1 < text.Length && text[eq + 1] == '"')
            {
                var close = text.IndexOf('"', eq + 2);
                if (close < 0) close = text.Length;
                value = text[(eq + 2)..close];
                i = close + 1;
            }
            else
            {
                var comma = text.IndexOf(',', eq + 1);
                if (comma < 0) comma = text.Length;
                value = text[(eq + 1)..comma];
                i = comma;
            }
            if (name.Length > 0) result[name] = value.Trim();
            while (i < text.Length && text[i] == ',') i++;
        }
        return result;
    }
}
