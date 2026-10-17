using System.Net.Http.Headers;

namespace Arqon.Core.Download;

/// <summary>Validator is the ETag, or Last-Modified when there is none; used to detect that the file changed between runs.</summary>
public sealed record RemoteFileInfo(long? Length, bool AcceptsRanges, string? FileName, string? Validator = null);

public static class RemoteFileProbe
{
    public static async Task<RemoteFileInfo> ProbeAsync(Uri url, HttpClient client, CancellationToken ct)
    {
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                return new RemoteFileInfo(
                    resp.Content.Headers.ContentLength,
                    resp.Headers.AcceptRanges.Contains("bytes"),
                    FileNameFrom(resp.Content.Headers.ContentDisposition, url),
                    ValidatorFrom(resp));
            }
        }
        catch (HttpRequestException)
        {
            // some servers reject HEAD; fall through to a ranged GET
        }

        using var get = new HttpRequestMessage(HttpMethod.Get, url);
        get.Headers.Range = new RangeHeaderValue(0, 0);
        using var r = await client.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        r.EnsureSuccessStatusCode();

        var name = FileNameFrom(r.Content.Headers.ContentDisposition, url);
        if (r.StatusCode == System.Net.HttpStatusCode.PartialContent)
            return new RemoteFileInfo(r.Content.Headers.ContentRange?.Length, true, name, ValidatorFrom(r));
        return new RemoteFileInfo(r.Content.Headers.ContentLength, false, name, ValidatorFrom(r));
    }

    private static string? ValidatorFrom(HttpResponseMessage resp) =>
        resp.Headers.ETag?.Tag ?? resp.Content.Headers.LastModified?.ToString("R");

    private static string? FileNameFrom(ContentDispositionHeaderValue? disposition, Uri url)
    {
        var fromHeader = disposition?.FileNameStar ?? disposition?.FileName;
        if (!string.IsNullOrWhiteSpace(fromHeader)) return fromHeader.Trim('"');
        var last = Path.GetFileName(url.LocalPath);
        return string.IsNullOrWhiteSpace(last) ? null : Uri.UnescapeDataString(last);
    }
}
