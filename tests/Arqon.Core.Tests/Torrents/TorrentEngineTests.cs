using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Arqon.Core.Links;
using Arqon.Core.Torrents;
using MonoTorrent;

namespace Arqon.Core.Tests.Torrents;

public sealed class TorrentEngineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-torrent-" + Guid.NewGuid().ToString("N"));

    public TorrentEngineTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort: the engine may still hold a file for a moment */ }
    }

    private static LinkInfo Loopback(string id, string address) =>
        new(id, id, id, IPAddress.Parse(address), null, LinkKind.Ethernet, 0);

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static async Task<T> Until<T>(Func<T> read, Func<T, bool> done, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (true)
        {
            var value = read();
            if (done(value) || DateTime.UtcNow > end) return value;
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task The_connector_binds_every_outgoing_connection_to_the_next_link()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var rotation = new LinkRoundRobin(() => [Loopback("a", "127.0.0.2"), Loopback("b", "127.0.0.3")], _ => true);
        var connector = new LinkBoundSocketConnector(rotation);

        var seen = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var accept = listener.AcceptTcpClientAsync();
            using var socket = await connector.ConnectAsync(new Uri($"ipv4://127.0.0.1:{port}"), CancellationToken.None);
            using var accepted = await accept;
            seen.Add(((IPEndPoint)accepted.Client.RemoteEndPoint!).Address.ToString());
        }

        Assert.Equal(["127.0.0.2", "127.0.0.3", "127.0.0.2", "127.0.0.3"], seen);
        Assert.Equal(2, rotation.Counts["a"]);
    }

    [Fact]
    public async Task A_file_is_downloaded_from_a_local_seeder_over_links_chosen_by_arqon()
    {
        // The file and its .torrent
        var seedDir = Path.Combine(_dir, "seed");
        Directory.CreateDirectory(seedDir);
        var data = new byte[3 * 1024 * 1024 + 123];
        new Random(7).NextBytes(data);
        var dataPath = Path.Combine(seedDir, "payload.bin");
        await File.WriteAllBytesAsync(dataPath, data);
        var torrentPath = Path.Combine(_dir, "payload.torrent");
        var dictionary = await new TorrentCreator().CreateAsync(new TorrentFileSource(dataPath));
        await File.WriteAllBytesAsync(torrentPath, dictionary.Encode());

        // A seeder on loopback
        var seederPort = FreePort();
        // Local peer discovery is off so the leecher must connect to the seeder itself (otherwise the seeder may find and call it)
        using var seeder = new TorrentEngine(new TorrentEngineOptions(Path.Combine(_dir, "cache-seed"), ListenPort: seederPort, EnableDht: false,
            AllowLocalPeerDiscovery: false));
        var seeding = await seeder.AddFileAsync(torrentPath, seedDir);
        await seeding.StartAsync();
        var seedState = await Until(seeding.Snapshot, s => s.Phase == TorrentPhase.Complete, TimeSpan.FromSeconds(30));
        Assert.Equal(TorrentPhase.Complete, seedState.Phase);

        // A downloader whose connections Arqon spreads over two (loopback) links
        var rotation = new LinkRoundRobin(() => [Loopback("a", "127.0.0.2"), Loopback("b", "127.0.0.3")], _ => true);
        var saveDir = Path.Combine(_dir, "downloaded");
        using var leecher = new TorrentEngine(new TorrentEngineOptions(Path.Combine(_dir, "cache-leech"), EnableDht: false, Links: rotation,
            AllowLocalPeerDiscovery: false));
        var handle = await leecher.AddFileAsync(torrentPath, saveDir);
        await handle.StartAsync();
        await handle.AddPeerAsync(new IPEndPoint(IPAddress.Loopback, seederPort));

        var state = await Until(handle.Snapshot, s => s.Phase == TorrentPhase.Complete, TimeSpan.FromSeconds(60));

        Assert.Equal(TorrentPhase.Complete, state.Phase);
        Assert.Equal("payload.bin", state.Name);
        Assert.Equal(data.Length, state.Size);
        await handle.StopAsync();
        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)),
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(saveDir, "payload.bin")))));
        Assert.True(rotation.Counts.Values.Sum() >= 1, "the connection to the seeder should have been made through a chosen link");
    }

    [Fact]
    public async Task Only_the_chosen_files_of_a_multi_file_torrent_are_downloaded_and_counted()
    {
        var seedRoot = Path.Combine(_dir, "seed-multi");
        var pack = Path.Combine(seedRoot, "pack");
        Directory.CreateDirectory(pack);
        var sizes = new Dictionary<string, int> { ["a.bin"] = 700_000, ["b.bin"] = 900_000, ["c.bin"] = 500_000 };
        var contents = new Dictionary<string, byte[]>();
        var seed = 11;
        foreach (var (name, size) in sizes)
        {
            contents[name] = new byte[size];
            new Random(seed++).NextBytes(contents[name]);
            await File.WriteAllBytesAsync(Path.Combine(pack, name), contents[name]);
        }
        var torrentPath = Path.Combine(_dir, "pack.torrent");
        await File.WriteAllBytesAsync(torrentPath, (await new TorrentCreator().CreateAsync(new TorrentFileSource(pack))).Encode());

        var seederPort = FreePort();
        using var seeder = new TorrentEngine(new TorrentEngineOptions(Path.Combine(_dir, "cache-seed-m"), ListenPort: seederPort, EnableDht: false,
            AllowLocalPeerDiscovery: false));
        var seeding = await seeder.AddFileAsync(torrentPath, seedRoot);
        await seeding.StartAsync();
        await Until(seeding.Snapshot, s => s.Phase == TorrentPhase.Complete, TimeSpan.FromSeconds(30));

        var saveDir = Path.Combine(_dir, "downloaded-m");
        using var leecher = new TorrentEngine(new TorrentEngineOptions(Path.Combine(_dir, "cache-leech-m"), EnableDht: false,
            AllowLocalPeerDiscovery: false));
        var handle = await leecher.AddFileAsync(torrentPath, saveDir);
        Assert.Equal(["a.bin", "b.bin", "c.bin"], handle.Files.Select(f => Path.GetFileName(f.Path)).Order());
        var wanted = handle.Files.Where(f => Path.GetFileName(f.Path) != "b.bin").Select(f => f.Index).ToHashSet();
        await handle.SelectFilesAsync(wanted);
        await handle.StartAsync();
        await handle.AddPeerAsync(new IPEndPoint(IPAddress.Loopback, seederPort));

        var state = await Until(handle.Snapshot, s => s.Phase == TorrentPhase.Complete, TimeSpan.FromSeconds(60));

        Assert.Equal(TorrentPhase.Complete, state.Phase);
        Assert.Equal(sizes["a.bin"] + sizes["c.bin"], state.Size);
        Assert.Equal(state.Size, state.Done);
        await handle.StopAsync();
        Assert.Equal(contents["a.bin"], await File.ReadAllBytesAsync(Path.Combine(saveDir, "pack", "a.bin")));
        Assert.Equal(contents["c.bin"], await File.ReadAllBytesAsync(Path.Combine(saveDir, "pack", "c.bin")));
        var skipped = Path.Combine(saveDir, "pack", "b.bin");
        Assert.True(!File.Exists(skipped) || !(await File.ReadAllBytesAsync(skipped)).AsSpan().SequenceEqual(contents["b.bin"]),
            "the file that was not chosen must not have been downloaded");
    }

    [Fact]
    public async Task A_finished_torrent_that_is_left_running_seeds_the_next_downloader_and_counts_what_it_uploaded()
    {
        var seedDir = Path.Combine(_dir, "seed-first");
        Directory.CreateDirectory(seedDir);
        var data = new byte[2 * 1024 * 1024 + 77];
        new Random(21).NextBytes(data);
        var dataPath = Path.Combine(seedDir, "share.bin");
        await File.WriteAllBytesAsync(dataPath, data);
        var torrentPath = Path.Combine(_dir, "share.torrent");
        await File.WriteAllBytesAsync(torrentPath, (await new TorrentCreator().CreateAsync(new TorrentFileSource(dataPath))).Encode());

        // The origin seeder
        var originPort = FreePort();
        using var origin = new TorrentEngine(new TorrentEngineOptions(Path.Combine(_dir, "cache-origin"), ListenPort: originPort, EnableDht: false,
            AllowLocalPeerDiscovery: false));
        var originHandle = await origin.AddFileAsync(torrentPath, seedDir);
        await originHandle.StartAsync();
        await Until(originHandle.Snapshot, s => s.Phase == TorrentPhase.Complete, TimeSpan.FromSeconds(30));

        // The first downloader finishes and is NOT stopped: it keeps seeding
        var firstPort = FreePort();
        using var first = new TorrentEngine(new TorrentEngineOptions(Path.Combine(_dir, "cache-first"), ListenPort: firstPort, EnableDht: false,
            AllowLocalPeerDiscovery: false));
        var firstHandle = await first.AddFileAsync(torrentPath, Path.Combine(_dir, "first"));
        await firstHandle.StartAsync();
        await firstHandle.AddPeerAsync(new IPEndPoint(IPAddress.Loopback, originPort));
        var firstDone = await Until(firstHandle.Snapshot, s => s.Phase == TorrentPhase.Complete, TimeSpan.FromSeconds(60));
        Assert.Equal(TorrentPhase.Complete, firstDone.Phase);

        // The origin goes away, so the second downloader can only get the data from the first one
        await originHandle.StopAsync();
        origin.Dispose();
        using var second = new TorrentEngine(new TorrentEngineOptions(Path.Combine(_dir, "cache-second"), EnableDht: false, AllowLocalPeerDiscovery: false));
        var secondHandle = await second.AddFileAsync(torrentPath, Path.Combine(_dir, "second"));
        await secondHandle.StartAsync();
        await secondHandle.AddPeerAsync(new IPEndPoint(IPAddress.Loopback, firstPort));
        var secondDone = await Until(secondHandle.Snapshot, s => s.Phase == TorrentPhase.Complete, TimeSpan.FromSeconds(60));

        Assert.Equal(TorrentPhase.Complete, secondDone.Phase);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)),
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(_dir, "second", "share.bin")))));
        Assert.True(firstHandle.Snapshot().Uploaded >= data.Length, "the first downloader should have counted what it uploaded");
    }

    [Fact]
    public async Task The_upload_speed_can_be_capped_and_released()
    {
        using var engine = new TorrentEngine(new TorrentEngineOptions(Path.Combine(_dir, "cache"), EnableDht: false));

        await engine.SetUploadLimitAsync(750_000);
        Assert.Equal(750_000, engine.UploadLimit);

        await engine.SetUploadLimitAsync(null);
        Assert.Null(engine.UploadLimit);
    }

    [Fact]
    public async Task A_magnet_link_that_is_not_valid_is_refused_with_a_clear_error()
    {
        using var engine = new TorrentEngine(new TorrentEngineOptions(Path.Combine(_dir, "cache"), EnableDht: false));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => engine.AddMagnetAsync("magnet:?xt=urn:btih:nothex", _dir));

        Assert.Contains("magnet", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Discarding_a_torrent_from_an_earlier_session_deletes_its_files_and_nothing_else()
    {
        var saveDir = Path.Combine(_dir, "downloads");
        Directory.CreateDirectory(saveDir);
        var partial = Path.Combine(saveDir, "movie.bin");
        var bystander = Path.Combine(saveDir, "keep-me.txt");
        await File.WriteAllBytesAsync(partial, new byte[300_000]);
        await File.WriteAllTextAsync(bystander, "not part of the torrent");
        var torrentPath = Path.Combine(_dir, "movie.torrent");
        await File.WriteAllBytesAsync(torrentPath, (await new TorrentCreator().CreateAsync(new TorrentFileSource(partial))).Encode());
        using var restarted = new TorrentEngine(new TorrentEngineOptions(Path.Combine(_dir, "cache"), EnableDht: false));   // nothing is known to it yet

        await restarted.DiscardAsync(torrentPath, saveDir);

        Assert.False(File.Exists(partial));
        Assert.True(File.Exists(bystander));
    }

    [Fact]
    public async Task Adding_the_same_torrent_twice_gives_the_same_handle_state_instead_of_an_error()
    {
        var dataPath = Path.Combine(_dir, "a.bin");
        await File.WriteAllBytesAsync(dataPath, new byte[200_000]);
        var torrentPath = Path.Combine(_dir, "a.torrent");
        await File.WriteAllBytesAsync(torrentPath, (await new TorrentCreator().CreateAsync(new TorrentFileSource(dataPath))).Encode());
        using var engine = new TorrentEngine(new TorrentEngineOptions(Path.Combine(_dir, "cache"), EnableDht: false));

        var first = await engine.AddFileAsync(torrentPath, _dir);
        var second = await engine.AddFileAsync(torrentPath, _dir);

        Assert.Equal(first.Snapshot().Name, second.Snapshot().Name);
    }
}
