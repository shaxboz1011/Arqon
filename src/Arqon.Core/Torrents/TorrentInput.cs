namespace Arqon.Core.Torrents;

public enum InputKind { Invalid, Http, Magnet, TorrentUrl, TorrentFile }

/// <summary>Tells what the user pasted into the download box: a plain link, a magnet link, or a .torrent (online or on disk).</summary>
public static class TorrentInput
{
    private const string TorrentExtension = ".torrent";

    public static InputKind Classify(string? text)
    {
        var t = Clean(text);
        if (t.Length == 0) return InputKind.Invalid;
        if (t.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) return InputKind.Magnet;

        if (Uri.TryCreate(t, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https")
                return uri.AbsolutePath.EndsWith(TorrentExtension, StringComparison.OrdinalIgnoreCase) ? InputKind.TorrentUrl : InputKind.Http;
            if (uri.IsFile)
                return uri.LocalPath.EndsWith(TorrentExtension, StringComparison.OrdinalIgnoreCase) ? InputKind.TorrentFile : InputKind.Invalid;
            return InputKind.Invalid;
        }

        return Path.IsPathRooted(t) && t.EndsWith(TorrentExtension, StringComparison.OrdinalIgnoreCase) ? InputKind.TorrentFile : InputKind.Invalid;
    }

    /// <summary>The file path of a pasted path or file:// link (Explorer's "Copy as path" adds quotes).</summary>
    public static string LocalPath(string text)
    {
        var t = Clean(text);
        return Uri.TryCreate(t, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : t;
    }

    private static string Clean(string? text) => (text ?? "").Trim().Trim('"').Trim();
}
