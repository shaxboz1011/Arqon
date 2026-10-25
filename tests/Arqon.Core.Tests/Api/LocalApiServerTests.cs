using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Arqon.Core.Api;

namespace Arqon.Core.Tests.Api;

public sealed class LocalApiServerTests : IDisposable
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    private sealed class Handler : ILocalApiHandler
    {
        public List<string> Added { get; } = [];

        public ApiStatus Status() => new("0.3.0", 2000, 100, [new ApiLink("w", "Wi-Fi", "Wifi", 21, 1500, 80), new ApiLink("p", "Phone", "Mobile", null, 500, 20)]);
        public IReadOnlyList<ApiDownload> Downloads() => [new ApiDownload("big.iso", "Downloading", 42.5, 1_000_000)];
        public void Add(string url) => Added.Add(url);
    }

    private readonly Handler _handler = new();
    private readonly LocalApiServer _server;
    private readonly HttpClient _http = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-api-" + Guid.NewGuid().ToString("N"));

    public LocalApiServerTests()
    {
        _server = new LocalApiServer(_handler, Token, port: 0);
        _server.Start();
        _http.BaseAddress = new Uri($"http://127.0.0.1:{_server.Port}");
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private HttpRequestMessage Req(HttpMethod method, string path, string? token = Token, string? body = null)
    {
        var r = new HttpRequestMessage(method, path);
        if (token is not null) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) r.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return r;
    }

    [Fact]
    public async Task Status_lists_the_links_with_their_numbers()
    {
        var response = await _http.SendAsync(Req(HttpMethod.Get, "/api/status"));
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("0.3.0", json.GetProperty("version").GetString());
        Assert.Equal(2, json.GetProperty("links").GetArrayLength());
        Assert.Equal("Wi-Fi", json.GetProperty("links")[0].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("links")[1].GetProperty("pingMs").ValueKind);
    }

    [Fact]
    public async Task Downloads_are_listed()
    {
        var text = await (await _http.SendAsync(Req(HttpMethod.Get, "/api/downloads"))).Content.ReadAsStringAsync();

        Assert.Contains("\"name\":\"big.iso\"", text);
        Assert.Contains("\"percent\":42.5", text);
    }

    [Fact]
    public async Task A_web_or_magnet_link_can_be_added()
    {
        var response = await _http.SendAsync(Req(HttpMethod.Post, "/api/downloads", body: """{"url":" https://example.com/a.zip "}"""));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(["https://example.com/a.zip"], _handler.Added);
    }

    [Theory]
    [InlineData("""{"url":"C:\\Windows\\win.ini"}""")]
    [InlineData("""{"url":"ftp://example.com/a"}""")]
    [InlineData("""{"link":"https://example.com/a.zip"}""")]
    [InlineData("not json")]
    public async Task Anything_else_is_refused(string body)
    {
        var response = await _http.SendAsync(Req(HttpMethod.Post, "/api/downloads", body: body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_handler.Added);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-token-wrong-token-wrong-token")]
    public async Task Without_the_right_token_nothing_is_answered_or_done(string? token)
    {
        var status = await _http.SendAsync(Req(HttpMethod.Get, "/api/status", token));
        var add = await _http.SendAsync(Req(HttpMethod.Post, "/api/downloads", token, """{"url":"https://example.com/a.zip"}"""));

        Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, add.StatusCode);
        Assert.Empty(_handler.Added);
        Assert.False(status.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Unknown_paths_and_methods_get_404_and_405()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.SendAsync(Req(HttpMethod.Get, "/nope"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _http.SendAsync(Req(HttpMethod.Delete, "/api/downloads"))).StatusCode);
    }

    [Fact]
    public async Task A_huge_body_is_refused()
    {
        var response = await _http.SendAsync(Req(HttpMethod.Post, "/api/downloads", body: "{\"url\":\"" + new string('a', 40_000) + "\"}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void The_token_is_made_once_and_then_kept()
    {
        var first = ApiToken.LoadOrCreate(_dir);
        var second = ApiToken.LoadOrCreate(_dir);

        Assert.Equal(48, first.Length);
        Assert.Equal(first, second);
    }
}
