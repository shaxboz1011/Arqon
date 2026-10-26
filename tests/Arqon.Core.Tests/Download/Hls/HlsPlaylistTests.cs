using Arqon.Core.Download.Hls;

namespace Arqon.Core.Tests.Download.Hls;

public class HlsPlaylistTests
{
    private static readonly Uri Base = new("https://cdn.example.com/videos/talk/index.m3u8");

    [Fact]
    public void A_media_playlist_gives_its_segments_with_absolute_links_and_numbers()
    {
        var media = Assert.IsType<HlsMedia>(HlsPlaylist.Parse("""
            #EXTM3U
            #EXT-X-VERSION:3
            #EXT-X-MEDIA-SEQUENCE:7
            #EXTINF:6.0,
            seg7.ts
            #EXTINF:4.5,first part
            /other/seg8.ts
            #EXTINF:2,
            https://mirror.example.com/seg9.ts
            #EXT-X-ENDLIST
            """, Base));

        Assert.Equal([7L, 8L, 9L], media.Segments.Select(s => s.Sequence));
        Assert.Equal("https://cdn.example.com/videos/talk/seg7.ts", media.Segments[0].Uri.ToString());
        Assert.Equal("https://cdn.example.com/other/seg8.ts", media.Segments[1].Uri.ToString());
        Assert.Equal("https://mirror.example.com/seg9.ts", media.Segments[2].Uri.ToString());
        Assert.Equal(12.5, media.Duration);
        Assert.Equal(".ts", media.Extension);
        Assert.Null(media.Segments[0].Key);
    }

    [Fact]
    public void A_master_playlist_offers_the_best_stream()
    {
        var master = Assert.IsType<HlsMaster>(HlsPlaylist.Parse("""
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360,CODECS="avc1.4d401e,mp4a.40.2"
            low/index.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080,CODECS="avc1.640028,mp4a.40.2"
            hd/index.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=2500000,RESOLUTION=1280x720
            mid/index.m3u8
            """, Base));

        Assert.Equal(3, master.Variants.Count);
        Assert.Equal("https://cdn.example.com/videos/talk/hd/index.m3u8", master.Best.Uri.ToString());
        Assert.Equal("1920x1080", master.Best.Resolution);
    }

    [Fact]
    public void Keys_apply_to_the_segments_after_them_until_switched_off()
    {
        var media = Assert.IsType<HlsMedia>(HlsPlaylist.Parse("""
            #EXTM3U
            #EXTINF:4,
            a.ts
            #EXT-X-KEY:METHOD=AES-128,URI="keys/k1.bin",IV=0x000102030405060708090a0b0c0d0e0f
            #EXTINF:4,
            b.ts
            #EXT-X-KEY:METHOD=NONE
            #EXTINF:4,
            c.ts
            #EXT-X-ENDLIST
            """, Base));

        Assert.Null(media.Segments[0].Key);
        Assert.Equal("https://cdn.example.com/videos/talk/keys/k1.bin", media.Segments[1].Key!.Uri.ToString());
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (byte)i).ToArray(), media.Segments[1].Key!.Iv);
        Assert.Null(media.Segments[2].Key);
    }

    [Fact]
    public void An_fmp4_stream_has_an_init_segment_and_is_saved_as_mp4()
    {
        var media = Assert.IsType<HlsMedia>(HlsPlaylist.Parse("#EXTM3U\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:4,\n0.m4s\n#EXT-X-ENDLIST\n", Base));

        Assert.Equal("https://cdn.example.com/videos/talk/init.mp4", media.InitSegment!.ToString());
        Assert.Equal(".mp4", media.Extension);
    }

    [Theory]
    [InlineData("#EXTM3U\n#EXTINF:4,\na.ts\n")]                                              // live: no end
    [InlineData("#EXTM3U\n#EXT-X-KEY:METHOD=SAMPLE-AES,URI=\"skd://x\"\n#EXTINF:4,\na.ts\n#EXT-X-ENDLIST\n")]
    [InlineData("#EXTM3U\n#EXTINF:4,\n#EXT-X-BYTERANGE:1000@0\na.ts\n#EXT-X-ENDLIST\n")]
    public void Live_protected_or_byte_range_playlists_are_refused_with_a_reason(string text)
    {
        Assert.Throws<NotSupportedException>(() => HlsPlaylist.Parse(text, Base));
    }

    [Theory]
    [InlineData("<html>not a playlist</html>")]
    [InlineData("#EXTM3U\n#EXT-X-ENDLIST\n")]
    [InlineData("")]
    public void Something_that_is_not_a_playlist_is_a_format_error(string text)
    {
        Assert.Throws<FormatException>(() => HlsPlaylist.Parse(text, Base));
    }

    [Theory]
    [InlineData("https://x.com/videos/my-talk/index.m3u8", "my-talk.ts")]
    [InlineData("https://x.com/hls/Big%20Buck%20Bunny.m3u8?token=1", "Big Buck Bunny.ts")]
    [InlineData("https://x.com/v/123456/720/playlist.m3u8", "720.ts")]
    [InlineData("https://x.com/master.m3u8", "master.ts")]
    public void The_file_name_comes_from_the_playlist_or_the_folder_above_a_generic_name(string url, string name)
    {
        Assert.Equal(name, HlsPlaylist.FileNameFor(new Uri(url)));
    }

    [Theory]
    [InlineData("https://x.com/a/index.m3u8", true)]
    [InlineData("https://x.com/a/INDEX.M3U8?x=1", true)]
    [InlineData("https://x.com/a/file.zip", false)]
    [InlineData("magnet:?xt=urn:btih:abc", false)]
    public void Playlist_links_are_recognised_by_their_extension(string url, bool expected)
    {
        Assert.Equal(expected, HlsPlaylist.IsPlaylistUrl(url));
    }

    [Fact]
    public void Quoted_attribute_values_may_hold_commas()
    {
        var attributes = HlsPlaylist.Attributes("BANDWIDTH=1,CODECS=\"a,b\",RESOLUTION=2x2");

        Assert.Equal("a,b", attributes["CODECS"]);
        Assert.Equal("2x2", attributes["RESOLUTION"]);
    }
}
