using System.Net;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections.Peer;
using MonoTorrent.Dht;

namespace Arqon.Core.Torrents;

/// <param name="CacheDirectory">Where MonoTorrent keeps resume data and the metadata of magnet links.</param>
/// <param name="ListenPort">Port for incoming peers; 0 lets Windows choose one.</param>
/// <param name="Links">When given, every outgoing peer connection is bound to the next link (see <see cref="LinkBoundSocketConnector"/>).</param>
/// <param name="AllowPortForwarding">UPnP / NAT-PMP port mapping. It changes the router's settings, so it is off unless asked for.</param>
public sealed record TorrentEngineOptions(
    string CacheDirectory,
    int ListenPort = 0,
    bool EnableDht = true,
    LinkRoundRobin? Links = null,
    bool AllowPortForwarding = false,
    bool AllowLocalPeerDiscovery = true,
    IPAddress? ListenAddress = null);

/// <summary>One file inside a torrent; <see cref="Index"/> is its position in the torrent's file list.</summary>
public sealed record TorrentFileInfo(int Index, string Path, long Size);

public enum TorrentPhase { Metadata, Checking, Downloading, Complete, Stopped, Error }

public sealed record TorrentSnapshot(
    string Name,
    long Size,
    long Done,
    double DownBps,
    double UpBps,
    int Peers,
    int Seeds,
    TorrentPhase Phase,
    string? Error,
    long Uploaded = 0);

/// <summary>A thin layer over MonoTorrent: add torrents by magnet link or file, and read plain numbers back.</summary>
public sealed class TorrentEngine : IDisposable
{
    private readonly ClientEngine _engine;
    private readonly DhtEngine? _dht;
    private volatile bool _disposed;

    public TorrentEngine(TorrentEngineOptions options)
    {
        Directory.CreateDirectory(options.CacheDirectory);
        var settings = new EngineSettingsBuilder
        {
            CacheDirectory = options.CacheDirectory,
            AllowPortForwarding = options.AllowPortForwarding,
            AllowLocalPeerDiscovery = options.AllowLocalPeerDiscovery,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadMagnetLinkMetadata = true,
            ListenEndPoints = new Dictionary<string, IPEndPoint> { ["ipv4"] = new(options.ListenAddress ?? IPAddress.Any, options.ListenPort) },
            DhtEndPoint = options.EnableDht ? new IPEndPoint(options.ListenAddress ?? IPAddress.Any, options.ListenPort) : null!,
        }.ToSettings();

        var factories = Factories.Default;
        if (options.EnableDht)
        {
            // MonoTorrent's built-in bootstrap hosts (router.bittorrent.com, router.utorrent.com) do not answer on every
            // network, and then the DHT never starts. Arqon makes its own DHT engine and gives it hosts that do answer.
            var dht = new DhtEngine();
            factories = factories.WithDhtCreator(() => dht);
            _dht = dht;
        }
        if (options.Links is { } links)
        {
            // The default peer connection opens its own sockets, so the IPv4 peer connection creator is replaced as well.
            var connector = new LinkBoundSocketConnector(links);
            factories = factories
                .WithSocketConnectorCreator(() => connector)
                .WithPeerConnectionCreator("ipv4", uri => new SocketPeerConnection(uri, connector));
        }
        _engine = new ClientEngine(settings, factories);
        if (_dht is not null) _ = SeedDhtAsync(_dht);
    }

    private static readonly (string Host, int Port)[] DhtBootstrap =
    [
        ("dht.transmissionbt.com", 6881),
        ("dht.libtorrent.org", 25401),
        ("router.bittorrent.com", 6881),
        ("router.utorrent.com", 6881),
        ("dht.aelitis.com", 6881),
    ];

    /// <summary>The "compact node info" of the DHT protocol: a 20 byte node id, the IPv4 address and the port. A bootstrap host does not check the id.</summary>
    internal static ReadOnlyMemory<byte> CompactNode(IPAddress address, int port)
    {
        var bytes = new byte[26];
        Random.Shared.NextBytes(bytes.AsSpan(0, 20));
        address.TryWriteBytes(bytes.AsSpan(20, 4), out _);
        bytes[24] = (byte)(port >> 8);
        bytes[25] = (byte)port;
        return bytes;
    }

    /// <summary>
    /// The DHT only starts once a torrent is started, and nodes given before that are lost, so the hosts are offered
    /// again every few seconds until the DHT reports that it is ready (or for a while at most).
    /// </summary>
    private async Task SeedDhtAsync(DhtEngine dht)
    {
        for (var round = 0; round < 12 && !_disposed; round++)
        {
            if (dht.State == DhtState.Ready) return;
            foreach (var (host, port) in DhtBootstrap)
            {
                try
                {
                    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var addresses = await Dns.GetHostAddressesAsync(host, limit.Token).ConfigureAwait(false);
                    var nodes = addresses.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(a => CompactNode(a, port)).ToList();
                    if (nodes.Count > 0) dht.Add(nodes);
                }
                catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
                {
                    // one unreachable host must not stop the others
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
    }

    public async Task<TorrentHandle> AddMagnetAsync(string magnet, string saveFolder)
    {
        if (!MagnetLink.TryParse(magnet, out var link) || link is null)
            throw new ArgumentException("That is not a valid magnet link.", nameof(magnet));

        var existing = _engine.Torrents.FirstOrDefault(m => m.InfoHashes.Equals(link.InfoHashes));
        if (existing is not null) return new TorrentHandle(_engine, existing);

        Directory.CreateDirectory(saveFolder);
        return new TorrentHandle(_engine, await _engine.AddAsync(MagnetTrackers.Complete(link), saveFolder).ConfigureAwait(false));
    }

    public async Task<TorrentHandle> AddFileAsync(string torrentPath, string saveFolder)
    {
        var torrent = await Torrent.LoadAsync(torrentPath).ConfigureAwait(false);

        var existing = _engine.Torrents.FirstOrDefault(m => m.InfoHashes.Equals(torrent.InfoHashes));
        if (existing is not null) return new TorrentHandle(_engine, existing);

        Directory.CreateDirectory(saveFolder);
        return new TorrentHandle(_engine, await _engine.AddAsync(torrent, saveFolder).ConfigureAwait(false));
    }

    /// <summary>
    /// Removes a torrent that this engine has not seen yet (one from an earlier session) together with its downloaded files.
    /// The torrent is added first so MonoTorrent knows exactly which files are its own and leaves everything else in the
    /// folder alone. A magnet link whose metadata was never cached has no known files, so only its cache entry goes.
    /// </summary>
    public async Task DiscardAsync(string source, string saveFolder)
    {
        var handle = source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)
            ? await AddMagnetAsync(source, saveFolder).ConfigureAwait(false)
            : await AddFileAsync(source, saveFolder).ConfigureAwait(false);
        await handle.RemoveAsync(deleteData: true).ConfigureAwait(false);
    }

    /// <summary>How the DHT (the trackerless peer search that magnet links without trackers depend on) is doing.</summary>
    public string DhtStatus => _engine.Dht.State.ToString();

    public int DhtNodes => _engine.Dht.NodeCount;

    /// <summary>Caps the download speed of all torrents together (used to keep a game's ping steady); null removes the cap.</summary>
    public async Task SetDownloadLimitAsync(long? bytesPerSecond)
    {
        var rate = bytesPerSecond is { } bps ? (int)Math.Clamp(bps, 1, int.MaxValue) : 0;   // 0 means unlimited
        if (_engine.Settings.MaximumDownloadRate == rate) return;
        await _engine.UpdateSettingsAsync(new EngineSettingsBuilder(_engine.Settings) { MaximumDownloadRate = rate }.ToSettings())
            .ConfigureAwait(false);
    }

    /// <summary>The cap on the upload speed of all torrents together, or null when there is none.</summary>
    public long? UploadLimit => _engine.Settings.MaximumUploadRate is > 0 and var rate ? rate : null;

    /// <summary>Caps the upload speed of all torrents together (seeding must not hold a game's link back either); null removes the cap.</summary>
    public async Task SetUploadLimitAsync(long? bytesPerSecond)
    {
        var rate = bytesPerSecond is { } bps ? (int)Math.Clamp(bps, 1, int.MaxValue) : 0;   // 0 means unlimited
        if (_engine.Settings.MaximumUploadRate == rate) return;
        await _engine.UpdateSettingsAsync(new EngineSettingsBuilder(_engine.Settings) { MaximumUploadRate = rate }.ToSettings())
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        _disposed = true;
        try { _engine.StopAllAsync(TimeSpan.FromSeconds(4)).Wait(TimeSpan.FromSeconds(6)); }
        catch (Exception ex) when (ex is AggregateException or TimeoutException or ObjectDisposedException)
        {
            // closing must never take the app down
        }
        _engine.Dispose();
    }
}

public sealed class TorrentHandle
{
    private readonly ClientEngine _engine;
    private readonly TorrentManager _manager;

    internal TorrentHandle(ClientEngine engine, TorrentManager manager)
    {
        _engine = engine;
        _manager = manager;
    }

    public Task StartAsync() => _manager.StartAsync();

    /// <summary>The files of the torrent; empty while the metadata of a magnet link has not arrived.</summary>
    public IReadOnlyList<TorrentFileInfo> Files =>
        _manager.HasMetadata ? _manager.Files.Select((f, i) => new TorrentFileInfo(i, f.Path, f.Length)).ToList() : [];

    /// <summary>Downloads only the files with these indexes (null: all of them). The other files are left alone.</summary>
    public async Task SelectFilesAsync(IReadOnlySet<int>? selected)
    {
        if (!_manager.HasMetadata) return;
        for (var i = 0; i < _manager.Files.Count; i++)
        {
            var wanted = selected is null || selected.Contains(i);
            await _manager.SetFilePriorityAsync(_manager.Files[i], wanted ? Priority.Normal : Priority.DoNotDownload).ConfigureAwait(false);
        }
    }

    /// <summary>Stops transferring and writes the resume data, so the torrent continues where it stopped.</summary>
    public Task StopAsync() => _manager.StopAsync();

    public Task<bool> AddPeerAsync(IPEndPoint peer) => _manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://{peer}")));

    /// <summary>Takes the torrent out of the engine; with <paramref name="deleteData"/> the downloaded files go too.</summary>
    public Task<bool> RemoveAsync(bool deleteData) =>
        _engine.RemoveAsync(_manager, deleteData ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.CacheDataOnly);

    public TorrentSnapshot Snapshot()
    {
        var m = _manager;
        var name = m.Torrent?.Name ?? m.MagnetLink?.Name ?? "";
        // Only the chosen files count: their size is the total, and the torrent is finished when they are all there.
        var size = m.HasMetadata
            ? m.Files.Where(f => f.Priority != Priority.DoNotDownload).Sum(f => f.Length)
            : m.MagnetLink?.Size ?? 0;
        var wantedDone = m.HasMetadata && m.HashChecked && size > 0 && m.PartialProgress >= 100;
        var done = (long)(size * Math.Clamp(m.PartialProgress, 0, 100) / 100.0);

        var phase = m.State switch
        {
            TorrentState.Error => TorrentPhase.Error,
            TorrentState.Metadata or TorrentState.FetchingHashes => TorrentPhase.Metadata,
            TorrentState.Hashing or TorrentState.HashingPaused => TorrentPhase.Checking,
            _ when wantedDone => TorrentPhase.Complete,
            TorrentState.Seeding => TorrentPhase.Complete,
            TorrentState.Starting or TorrentState.Downloading => TorrentPhase.Downloading,
            _ => TorrentPhase.Stopped,
        };
        if (phase == TorrentPhase.Complete) done = size;

        return new TorrentSnapshot(name, size, done, m.Monitor.DownloadRate, m.Monitor.UploadRate, m.OpenConnections, m.Peers.Seeds,
            phase, m.Error?.Exception?.Message ?? (phase == TorrentPhase.Error ? m.Error?.Reason.ToString() : null),
            m.Monitor.DataBytesSent);
    }
}
