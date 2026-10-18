using System.Net;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections.Peer;

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
    string? Error);

/// <summary>A thin layer over MonoTorrent: add torrents by magnet link or file, and read plain numbers back.</summary>
public sealed class TorrentEngine : IDisposable
{
    private readonly ClientEngine _engine;

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
        if (options.Links is { } links)
        {
            // The default peer connection opens its own sockets, so the IPv4 peer connection creator is replaced as well.
            var connector = new LinkBoundSocketConnector(links);
            factories = factories
                .WithSocketConnectorCreator(() => connector)
                .WithPeerConnectionCreator("ipv4", uri => new SocketPeerConnection(uri, connector));
        }
        _engine = new ClientEngine(settings, factories);
    }

    public async Task<TorrentHandle> AddMagnetAsync(string magnet, string saveFolder)
    {
        if (!MagnetLink.TryParse(magnet, out var link) || link is null)
            throw new ArgumentException("That is not a valid magnet link.", nameof(magnet));

        var existing = _engine.Torrents.FirstOrDefault(m => m.InfoHashes.Equals(link.InfoHashes));
        if (existing is not null) return new TorrentHandle(_engine, existing);

        Directory.CreateDirectory(saveFolder);
        return new TorrentHandle(_engine, await _engine.AddAsync(link, saveFolder).ConfigureAwait(false));
    }

    public async Task<TorrentHandle> AddFileAsync(string torrentPath, string saveFolder)
    {
        var torrent = await Torrent.LoadAsync(torrentPath).ConfigureAwait(false);

        var existing = _engine.Torrents.FirstOrDefault(m => m.InfoHashes.Equals(torrent.InfoHashes));
        if (existing is not null) return new TorrentHandle(_engine, existing);

        Directory.CreateDirectory(saveFolder);
        return new TorrentHandle(_engine, await _engine.AddAsync(torrent, saveFolder).ConfigureAwait(false));
    }

    /// <summary>Caps the download speed of all torrents together (used to keep a game's ping steady); null removes the cap.</summary>
    public async Task SetDownloadLimitAsync(long? bytesPerSecond)
    {
        var rate = bytesPerSecond is { } bps ? (int)Math.Clamp(bps, 1, int.MaxValue) : 0;   // 0 means unlimited
        if (_engine.Settings.MaximumDownloadRate == rate) return;
        await _engine.UpdateSettingsAsync(new EngineSettingsBuilder(_engine.Settings) { MaximumDownloadRate = rate }.ToSettings())
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
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

    /// <summary>Stops transferring and writes the resume data, so the torrent continues where it stopped.</summary>
    public Task StopAsync() => _manager.StopAsync();

    public Task<bool> AddPeerAsync(IPEndPoint peer) => _manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://{peer}")));

    /// <summary>Takes the torrent out of the engine; with <paramref name="deleteData"/> the downloaded files go too.</summary>
    public Task<bool> RemoveAsync(bool deleteData) =>
        _engine.RemoveAsync(_manager, deleteData ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.CacheDataOnly);

    public TorrentSnapshot Snapshot()
    {
        var m = _manager;
        var size = m.Torrent?.Size ?? m.MagnetLink?.Size ?? 0;
        var name = m.Torrent?.Name ?? m.MagnetLink?.Name ?? "";
        var done = (long)(size * Math.Clamp(m.Progress, 0, 100) / 100.0);

        var phase = m.State switch
        {
            TorrentState.Error => TorrentPhase.Error,
            TorrentState.Metadata or TorrentState.FetchingHashes => TorrentPhase.Metadata,
            TorrentState.Hashing or TorrentState.HashingPaused => TorrentPhase.Checking,
            _ when m.HasMetadata && m.Complete && m.HashChecked => TorrentPhase.Complete,
            TorrentState.Seeding => TorrentPhase.Complete,
            TorrentState.Starting or TorrentState.Downloading => TorrentPhase.Downloading,
            _ => TorrentPhase.Stopped,
        };
        if (phase == TorrentPhase.Complete) done = size;

        return new TorrentSnapshot(name, size, done, m.Monitor.DownloadRate, m.Monitor.UploadRate, m.OpenConnections, m.Peers.Seeds,
            phase, m.Error?.Exception?.Message ?? (phase == TorrentPhase.Error ? m.Error?.Reason.ToString() : null));
    }
}
