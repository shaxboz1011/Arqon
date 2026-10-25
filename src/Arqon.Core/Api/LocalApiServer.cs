using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arqon.Core.Torrents;

namespace Arqon.Core.Api;

public sealed record ApiLink(string Id, string Name, string Kind, double? PingMs, double DownBps, double UpBps);

public sealed record ApiDownload(string Name, string State, double Percent, double SpeedBps);

public sealed record ApiStatus(string Version, double DownBps, double UpBps, IReadOnlyList<ApiLink> Links);

/// <summary>What the local API can see and do; the app answers on its own thread.</summary>
public interface ILocalApiHandler
{
    ApiStatus Status();
    IReadOnlyList<ApiDownload> Downloads();
    void Add(string url);
}

/// <summary>
/// A small HTTP API on 127.0.0.1 for scripts and other programs: read the links and downloads, add a download.
/// Every request needs <c>Authorization: Bearer {token}</c> (the token is in <c>%APPDATA%\Arqon\api-token</c>), so a web page
/// cannot use it: a browser does not send that header to another origin without asking, and the server never answers such a question.
/// It is off unless switched on in Settings. One request per connection, bodies up to 16 KB.
/// </summary>
public sealed class LocalApiServer : IDisposable
{
    public const int DefaultPort = 47011;
    private const int MaxHeaderBytes = 8 * 1024;
    private const int MaxBodyBytes = 16 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ILocalApiHandler _handler;
    private readonly byte[] _token;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();

    public LocalApiServer(ILocalApiHandler handler, string token, int port = DefaultPort)
    {
        if (token.Length < 16) throw new ArgumentException("The token is too short", nameof(token));
        _handler = handler;
        _token = Encoding.UTF8.GetBytes(token);
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    /// <summary>The port it listens on (useful when it was started with port 0).</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start()
    {
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            timeout.CancelAfter(RequestTimeout);
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, timeout.Token).ConfigureAwait(false);
                var (status, body) = request is null ? (400, Error("bad request")) : Handle(request);
                await WriteResponseAsync(stream, status, body, timeout.Token).ConfigureAwait(false);
                await CloseGentlyAsync(client, stream).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // the caller went away or was too slow
            }
        }
    }

    internal sealed record Request(string Method, string Path, Dictionary<string, string> Headers, string Body);

    internal (int Status, string Body) Handle(Request request)
    {
        if (!Authorized(request)) return (401, Error("missing or wrong token"));
        try
        {
            return (request.Method, request.Path) switch
            {
                ("GET", "/api/status") => (200, JsonSerializer.Serialize(_handler.Status(), Json)),
                ("GET", "/api/downloads") => (200, JsonSerializer.Serialize(_handler.Downloads(), Json)),
                ("POST", "/api/downloads") => AddDownload(request.Body),
                (_, "/api/status" or "/api/downloads") => (405, Error("method not allowed")),
                _ => (404, Error("not found")),
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (500, Error("internal error"));
        }
    }

    private (int, string) AddDownload(string body)
    {
        string? url;
        try { url = JsonDocument.Parse(body).RootElement.GetProperty("url").GetString()?.Trim(); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return (400, Error("expected {\"url\": \"...\"}")); }

        if (TorrentInput.Classify(url) is not (InputKind.Http or InputKind.TorrentUrl or InputKind.Magnet))
            return (400, Error("only http, https and magnet links can be added"));
        _handler.Add(url!);
        return (202, JsonSerializer.Serialize(new { added = url }, Json));
    }

    private bool Authorized(Request request)
    {
        if (!request.Headers.TryGetValue("authorization", out var value) || !value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value[7..].Trim()), _token);
    }

    private static string Error(string message) => JsonSerializer.Serialize(new { error = message }, Json);

    private static async Task<Request?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxHeaderBytes + MaxBodyBytes];
        var read = 0;
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            if (read >= MaxHeaderBytes) return null;
            var n = await stream.ReadAsync(buffer.AsMemory(read, MaxHeaderBytes - read), ct).ConfigureAwait(false);
            if (n == 0) return null;
            read += n;
            headerEnd = buffer.AsSpan(0, read).IndexOf("\r\n\r\n"u8);
        }

        var lines = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length != 3) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        var length = headers.TryGetValue("content-length", out var cl) && int.TryParse(cl, out var l) ? l : 0;
        if (length < 0 || length > MaxBodyBytes) return null;
        var bodyStart = headerEnd + 4;
        while (read - bodyStart < length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, bodyStart + length - read), ct).ConfigureAwait(false);
            if (n == 0) return null;
            read += n;
        }

        var path = first[1].Split('?')[0];
        return new Request(first[0].ToUpperInvariant(), path, headers, Encoding.UTF8.GetString(buffer, bodyStart, length));
    }

    /// <summary>
    /// Says "no more from us" and reads what the caller is still sending (a refused oversized body), for a moment: closing
    /// with unread data would reset the connection, and the caller would see an error instead of the answer.
    /// </summary>
    private static async Task CloseGentlyAsync(TcpClient client, NetworkStream stream)
    {
        client.Client.Shutdown(SocketShutdown.Send);
        using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var scratch = new byte[8192];
        var total = 0;
        while (total < 1024 * 1024)
        {
            var n = await stream.ReadAsync(scratch, drain.Token).ConfigureAwait(false);
            if (n == 0) return;
            total += n;
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string body, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var reason = status switch { 200 => "OK", 202 => "Accepted", 400 => "Bad Request", 401 => "Unauthorized", 404 => "Not Found", 405 => "Method Not Allowed", _ => "Error" };
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
    }
}

/// <summary>The secret a caller of the local API must send. Made once, kept in a file only this user can normally read.</summary>
public static class ApiToken
{
    public static string LoadOrCreate(string directory)
    {
        var path = Path.Combine(directory, "api-token");
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (existing.Length >= 32) return existing;
            }
        }
        catch (IOException) { /* make a new one */ }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, token);
        return token;
    }
}
