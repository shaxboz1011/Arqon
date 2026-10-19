using System.Globalization;
using System.Text.Json;

namespace Arqon.Core.Updates;

/// <summary>A published release: its tag (such as "v0.1.4"), the page where it can be downloaded, and its title.</summary>
public sealed record ReleaseInfo(string Tag, string Url, string Name);

/// <summary>
/// Asks GitHub which release is the newest. It is one small request to the public API, made at most every few hours and only when
/// the user has not switched it off; nothing about the user or the computer is sent (only the standard request headers).
/// </summary>
public static class UpdateChecker
{
    public const string Repository = "shaxboz1011/Arqon";

    /// <summary>Reads GitHub's "latest release" answer. Null for anything that is not a normal published release.</summary>
    public static ReleaseInfo? ParseLatest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
            if (!root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String) return null;
            var url = root.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString()! : "";
            var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : tag.GetString()!;
            // only ever link to the project's own releases page
            if (!url.StartsWith("https://github.com/" + Repository + "/", StringComparison.OrdinalIgnoreCase)) return null;
            return new ReleaseInfo(tag.GetString()!, url, name);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>True when <paramref name="tag"/> names a higher version than <paramref name="current"/> ("0.1.3" or "v0.1.3-beta" forms).</summary>
    public static bool IsNewer(string current, string tag)
    {
        var a = Parse(current);
        var b = Parse(tag);
        if (a is null || b is null) return false;
        for (var i = 0; i < 3; i++)
        {
            if (b[i] != a[i]) return b[i] > a[i];
        }
        return false;
    }

    private static int[]? Parse(string version)
    {
        var text = version.Trim().TrimStart('v', 'V');
        var cut = text.IndexOfAny(['-', '+']);
        if (cut >= 0) text = text[..cut];
        var parts = text.Split('.');
        if (parts.Length is < 1 or > 4) return null;
        var numbers = new int[3];
        for (var i = 0; i < parts.Length && i < 3; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return null;
        }
        return numbers;
    }

    /// <summary>The newest release, or null when it cannot be read (no connection, rate limit, not found).</summary>
    public static async Task<ReleaseInfo?> LatestAsync(HttpClient http, CancellationToken cancel = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
            request.Headers.UserAgent.ParseAdd("Arqon-update-check");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return ParseLatest(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }
}
