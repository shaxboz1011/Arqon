using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Arqon.Core.Download;

/// <summary>
/// SHA-256 checks of finished downloads. The expected value comes from the link (<c>…/file.iso#sha256=…</c>, the fragment is
/// never sent to the server) or from text the user copied: a bare hash, or a SHA256SUMS file where the line for the file counts.
/// </summary>
public static partial class Checksum
{
    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Hex64();

    [GeneratedRegex(@"^(?<hash>[0-9a-fA-F]{64})\s+\*?(?<name>.+)$")]
    private static partial Regex SumsLine();

    [GeneratedRegex(@"^SHA256\s*\((?<name>.+)\)\s*=\s*(?<hash>[0-9a-fA-F]{64})$", RegexOptions.IgnoreCase)]
    private static partial Regex BsdLine();

    /// <summary>The hash in a link's <c>#sha256=</c> (or <c>#sha-256=</c>) fragment, lower case, or null.</summary>
    public static string? FromUrl(string url)
    {
        var hash = url.IndexOf('#');
        if (hash < 0) return null;
        foreach (var part in url[(hash + 1)..].Split('&'))
        {
            var eq = part.IndexOf('=');
            if (eq < 0) continue;
            var name = part[..eq];
            if (name.Equals("sha256", StringComparison.OrdinalIgnoreCase) || name.Equals("sha-256", StringComparison.OrdinalIgnoreCase))
            {
                var value = part[(eq + 1)..].Trim();
                return Hex64().IsMatch(value) ? value.ToLowerInvariant() : null;
            }
        }
        return null;
    }

    /// <summary>
    /// The expected hash for <paramref name="fileName"/> in copied text: a bare hash, a "hash  name" line of a SHA256SUMS file,
    /// or a BSD style "SHA256 (name) = hash" line. With several lines, only the one naming the file counts.
    /// </summary>
    public static string? FromText(string? text, string fileName)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 1 && Hex64().IsMatch(lines[0])) return lines[0].ToLowerInvariant();

        foreach (var line in lines)
        {
            var m = SumsLine().Match(line);
            if (!m.Success) m = BsdLine().Match(line);
            if (!m.Success) continue;
            var name = Path.GetFileName(m.Groups["name"].Value.Trim().Replace('/', Path.DirectorySeparatorChar));
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) return m.Groups["hash"].Value.ToLowerInvariant();
        }
        // one line for a file of another name is still the value the user meant
        if (lines.Count == 1)
        {
            var m = SumsLine().Match(lines[0]);
            if (m.Success) return m.Groups["hash"].Value.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>The SHA-256 of a file, lower case hex. Reads with a large buffer; a 4 GB file takes a few seconds.</summary>
    public static async Task<string> ComputeAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
