namespace Arqon.Core.Download;

/// <summary>Decides whether copied text is a direct link to a downloadable file, so Arqon can offer to fetch it.</summary>
public static class DownloadLinkDetector
{
    private const int MaxLength = 2048;

    private static readonly HashSet<string> FileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".gz", ".tgz", ".bz2", ".xz", ".tar", ".cab",
        ".iso", ".img", ".dmg", ".bin",
        ".exe", ".msi", ".msix", ".apk", ".deb", ".rpm", ".pkg", ".appimage", ".jar",
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".mp3", ".flac", ".wav", ".m4a",
        ".pdf", ".epub", ".djvu", ".docx", ".xlsx", ".pptx",
    };

    public static Uri? Detect(string? text)
    {
        if (text is null) return null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxLength) return null;
        if (trimmed.Any(char.IsWhiteSpace)) return null;   // several words or lines are not a bare link

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        return FileExtensions.Contains(Path.GetExtension(uri.AbsolutePath)) ? uri : null;
    }
}
