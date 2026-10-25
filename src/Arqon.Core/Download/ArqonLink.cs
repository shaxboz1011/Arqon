using Arqon.Core.Torrents;

namespace Arqon.Core.Download;

/// <summary>
/// <c>arqon://add?url=…</c>: how the browser extension (or a bookmarklet) hands a link to Arqon. Windows starts Arqon with it,
/// the way it does for a magnet link. Only web links and magnet links are accepted, never a path on this computer.
/// </summary>
public static class ArqonLink
{
    public const string Scheme = "arqon";

    public static string Build(string url) => $"{Scheme}://add?url={Uri.EscapeDataString(url)}";

    public static bool IsArqonLink(string? text) =>
        text is not null && text.Trim().Trim('"').StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>The link inside, or null when this is not an arqon link or what it carries is not a web or magnet link.</summary>
    public static string? Unwrap(string? text)
    {
        if (!IsArqonLink(text)) return null;
        var t = text!.Trim().Trim('"');
        var query = t.IndexOf('?');
        if (query < 0) return null;

        var action = t[(Scheme.Length + 1)..query].Trim('/');
        if (!string.Equals(action, "add", StringComparison.OrdinalIgnoreCase)) return null;

        foreach (var pair in t[(query + 1)..].Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0 || !string.Equals(pair[..eq], "url", StringComparison.OrdinalIgnoreCase)) continue;
            string url;
            try { url = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' ')).Trim(); }
            catch (UriFormatException) { return null; }
            return TorrentInput.Classify(url) is InputKind.Http or InputKind.TorrentUrl or InputKind.Magnet ? url : null;
        }
        return null;
    }
}
