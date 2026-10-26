using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Arqon.Core.Download;
using Arqon.Core.Download.Hls;
using Arqon.Core.Links;

namespace Arqon.Core.Tests.Download.Hls;

public sealed class HlsJobTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-hls-" + Guid.NewGuid().ToString("N"));
    private string Dest => Path.Combine(_dir, "video.ts");

    public HlsJobTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static LinkInfo Link(string id) => new(id, id, "", IPAddress.Loopback, null, LinkKind.Wifi, 0);

    /// <summary>A pretend CDN: serves fixed files, counts requests per link and can fail segments on a link.</summary>
    private sealed class Cdn
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public ConcurrentDictionary<string, int> RequestsPerLink { get; } = new();
        public ConcurrentBag<string> Requested { get; } = [];
        public Func<string, string, bool> Fails { get; set; } = (_, _) => false;
        public int DelayMs { get; set; }
        public Action<int>? OnRequest { get; set; }

        public HttpClient ClientFor(LinkInfo link) => new(new Handler(this, link.Id));

        private sealed class Handler(Cdn cdn, string linkId) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var path = request.RequestUri!.AbsolutePath;
                cdn.RequestsPerLink.AddOrUpdate(linkId, 1, (_, n) => n + 1);
                cdn.Requested.Add(path);
                cdn.OnRequest?.Invoke(cdn.Requested.Count);
                if (cdn.DelayMs > 0) await Task.Delay(cdn.DelayMs, ct);
                if (cdn.Fails(linkId, path)) return new HttpResponseMessage(HttpStatusCode.BadGateway);
                if (!cdn.Files.TryGetValue(path, out var body)) return new HttpResponseMessage(HttpStatusCode.NotFound);
                var content = new ByteArrayContent(body);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }
        }
    }

    private static byte[] Segment(int i) => Enumerable.Range(0, 1000 + i * 37).Select(b => (byte)(b * 7 + i)).ToArray();

    /// <summary>A playlist of <paramref name="count"/> segments at /v/ and the bytes the finished file must have.</summary>
    private static (Cdn Cdn, byte[] Expected) Video(int count, Func<int, byte[]>? encrypt = null, string keyLine = "")
    {
        var cdn = new Cdn();
        var playlist = new StringBuilder("#EXTM3U\n#EXT-X-TARGETDURATION:4\n").Append(keyLine);
        var expected = new List<byte>();
        for (var i = 0; i < count; i++)
        {
            playlist.Append("#EXTINF:4.0,\n").Append($"s{i}.ts\n");
            cdn.Files[$"/v/s{i}.ts"] = encrypt?.Invoke(i) ?? Segment(i);
            expected.AddRange(Segment(i));
        }
        playlist.Append("#EXT-X-ENDLIST\n");
        cdn.Files["/v/index.m3u8"] = Encoding.UTF8.GetBytes(playlist.ToString());
        return (cdn, expected.ToArray());
    }

    private static readonly Uri Playlist = new("https://cdn.test/v/index.m3u8");

    [Fact]
    public async Task Segments_are_spread_over_the_links_and_written_in_order()
    {
        var (cdn, expected) = Video(40);
        cdn.DelayMs = 5;

        var result = await new HlsJob(cdn.ClientFor).RunAsync(Playlist, Dest, [Link("wifi"), Link("phone")], null, default);

        Assert.Equal(expected, await File.ReadAllBytesAsync(Dest));
        Assert.Equal(".ts", result.Extension);
        Assert.Equal(40, result.Segments);
        Assert.True(cdn.RequestsPerLink.GetValueOrDefault("wifi") > 5, "wifi carried too little");
        Assert.True(cdn.RequestsPerLink.GetValueOrDefault("phone") > 5, "phone carried too little");
        Assert.False(File.Exists(Dest + ".arqpart"));
        Assert.False(File.Exists(Dest + ".arqhls"));
    }

    [Fact]
    public async Task A_master_playlist_downloads_its_best_stream()
    {
        var (cdn, expected) = Video(5);
        cdn.Files["/master.m3u8"] = Encoding.UTF8.GetBytes(
            "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=100\nlow/index.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=900\nv/index.m3u8\n");

        await new HlsJob(cdn.ClientFor).RunAsync(new Uri("https://cdn.test/master.m3u8"), Dest, [Link("a")], null, default);

        Assert.Equal(expected, await File.ReadAllBytesAsync(Dest));
        Assert.DoesNotContain("/low/index.m3u8", cdn.Requested);
    }

    [Fact]
    public async Task Aes_128_segments_are_decrypted_with_the_playlist_iv_or_the_sequence_number()
    {
        var key = Enumerable.Range(100, 16).Select(i => (byte)i).ToArray();
        var explicitIv = Enumerable.Range(0, 16).Select(i => (byte)(255 - i)).ToArray();
        using var aes = Aes.Create();
        aes.Key = key;

        // the first video names the IV in the playlist, the second leaves it to the sequence number
        var (withIv, expected1) = Video(4, i => aes.EncryptCbc(Segment(i), explicitIv),
            $"#EXT-X-KEY:METHOD=AES-128,URI=\"/k.bin\",IV=0x{Convert.ToHexString(explicitIv)}\n");
        withIv.Files["/k.bin"] = key;
        var (bySequence, expected2) = Video(4, i => aes.EncryptCbc(Segment(i), HlsJob.IvFor(new HlsSegment(i, Playlist, 4, new HlsKey(Playlist, null)))),
            "#EXT-X-KEY:METHOD=AES-128,URI=\"/k.bin\"\n");
        bySequence.Files["/k.bin"] = key;

        await new HlsJob(withIv.ClientFor).RunAsync(Playlist, Dest, [Link("a"), Link("b")], null, default);
        Assert.Equal(expected1, await File.ReadAllBytesAsync(Dest));

        await new HlsJob(bySequence.ClientFor).RunAsync(Playlist, Dest, [Link("a")], null, default);
        Assert.Equal(expected2, await File.ReadAllBytesAsync(Dest));
        Assert.Single(bySequence.Requested, p => p == "/k.bin");   // the key is fetched once
    }

    [Fact]
    public async Task An_fmp4_stream_starts_with_its_init_segment()
    {
        var cdn = new Cdn();
        cdn.Files["/v/index.m3u8"] = Encoding.UTF8.GetBytes("#EXTM3U\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:4,\n0.m4s\n#EXTINF:4,\n1.m4s\n#EXT-X-ENDLIST\n");
        cdn.Files["/v/init.mp4"] = [1, 1, 1];
        cdn.Files["/v/0.m4s"] = [2, 2];
        cdn.Files["/v/1.m4s"] = [3];

        var result = await new HlsJob(cdn.ClientFor).RunAsync(Playlist, Dest, [Link("a"), Link("b")], null, default);

        Assert.Equal(new byte[] { 1, 1, 1, 2, 2, 3 }, await File.ReadAllBytesAsync(Dest));
        Assert.Equal(".mp4", result.Extension);
    }

    [Fact]
    public async Task A_link_that_cannot_fetch_segments_leaves_them_to_the_other_link()
    {
        var (cdn, expected) = Video(20);
        cdn.Fails = (link, path) => link == "broken" && path.EndsWith(".ts");

        await new HlsJob(cdn.ClientFor).RunAsync(Playlist, Dest, [Link("broken"), Link("good")], null, default);

        Assert.Equal(expected, await File.ReadAllBytesAsync(Dest));
    }

    [Fact]
    public async Task A_dead_link_that_fails_at_once_does_not_use_up_the_retries_of_a_working_link()
    {
        var (cdn, expected) = Video(6);
        cdn.DelayMs = 1200;   // slow but working; the dead link refuses everything at once, like a phone whose data was switched off

        HttpClient ClientFor(LinkInfo l) => l.Id == "dead" ? new HttpClient(new Refusing()) : cdn.ClientFor(l);

        await new HlsJob(ClientFor).RunAsync(Playlist, Dest, [Link("dead"), Link("good")], null, default);

        Assert.Equal(expected, await File.ReadAllBytesAsync(Dest));
    }

    private sealed class Refusing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("No route to host");
    }

    [Fact]
    public async Task A_segment_that_no_link_can_fetch_fails_the_download()
    {
        var (cdn, _) = Video(6);
        cdn.Fails = (_, path) => path == "/v/s3.ts";

        await Assert.ThrowsAsync<IOException>(() => new HlsJob(cdn.ClientFor).RunAsync(Playlist, Dest, [Link("a"), Link("b")], null, default));
        Assert.False(File.Exists(Dest));
    }

    [Fact]
    public async Task A_paused_download_goes_on_from_where_it_stopped()
    {
        var (cdn, expected) = Video(60);
        cdn.DelayMs = 5;
        using var cts = new CancellationTokenSource();
        cdn.OnRequest = n => { if (n > 35) cts.Cancel(); };   // paused a little after half way

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new HlsJob(cdn.ClientFor).RunAsync(Playlist, Dest, [Link("a")], null, cts.Token));
        Assert.True(File.Exists(Dest + ".arqhls"));

        cdn.OnRequest = null;
        cdn.Requested.Clear();
        await new HlsJob(cdn.ClientFor).RunAsync(Playlist, Dest, [Link("a")], null, default);

        Assert.Equal(expected, await File.ReadAllBytesAsync(Dest));
        Assert.True(cdn.Requested.Count(p => p.EndsWith(".ts")) < 45, $"started over: {cdn.Requested.Count} requests");
    }

    [Fact]
    public async Task Progress_ends_at_the_real_size_and_counts_every_link()
    {
        var (cdn, expected) = Video(10);
        DownloadProgress? last = null;

        await new HlsJob(cdn.ClientFor).RunAsync(Playlist, Dest, [Link("a"), Link("b")], new SyncProgress<DownloadProgress>(p => last = p), default);

        Assert.Equal(expected.Length, last!.Done);
        Assert.Equal(expected.Length, last.Total);
        Assert.Equal(expected.Length, last.PerLinkBytes.Values.Sum());
    }

    [Fact]
    public async Task A_live_stream_is_refused_before_anything_is_written()
    {
        var cdn = new Cdn();
        cdn.Files["/v/index.m3u8"] = Encoding.UTF8.GetBytes("#EXTM3U\n#EXTINF:4,\ns0.ts\n");

        await Assert.ThrowsAsync<NotSupportedException>(() => new HlsJob(cdn.ClientFor).RunAsync(Playlist, Dest, [Link("a")], null, default));
        Assert.False(File.Exists(Dest + ".arqpart"));
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
