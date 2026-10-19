using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using Arqon.App.Infrastructure;
using Arqon.Core;
using Arqon.Core.Download;
using Arqon.Core.Guard;
using Arqon.Core.Links;
using Arqon.Core.Meter;
using Arqon.Core.Monitoring;
using Arqon.Core.Policy;
using Arqon.Core.Settings;
using Arqon.Core.Steering;
using Arqon.Core.Torrents;
using Arqon.Core.Usage;
using Arqon.Core.Windows;

namespace Arqon.App.Services;

/// <summary>A message for the user that is worth a tray balloon (a link came or went, a game was found, a fault happened).</summary>
public sealed record Notice(string Title, string Message);

/// <summary>Composition root: the one place that decides between real Windows services and the demo data set.</summary>
public sealed class AppServices : IDisposable
{
    private static readonly TimeSpan MaintenanceEvery = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan GameCheckEvery = TimeSpan.FromSeconds(1);
    private const int SaveUsageEveryTicks = 10;

    private readonly TtlCache<IReadOnlyList<LinkInfo>> _linkCache;
    private readonly LinkChangeDetector _linkChanges = new();
    private readonly Dictionary<string, DateTimeOffset> _capNotified = new();
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private Timer? _maintenance;
    private Timer? _gameTimer;
    private readonly object _torrentGate = new();
    private TorrentEngine? _torrents;
    private LinkRoundRobin? _torrentLinks;
    private int _ticks;

    public bool Demo { get; }
    public ILinkProvider Links { get; }
    public LinkMonitor Monitor { get; }
    public ISettingsStore SettingsStore { get; }
    public WindowsFixService Fix { get; }
    public ArqonSettings Settings { get; private set; }
    public RuleEngine Rules => new(Settings);

    public LinkThrottle Throttle { get; } = new();
    public GameGuard Guard { get; }
    public GameWatcher Games { get; }
    public UsageTracker Usage { get; }
    public SteeringService Steering { get; }

    /// <summary>
    /// The torrent engine, created on first use: it opens a listening port, so a user who never adds a torrent never gets
    /// a firewall question. Its peer connections are spread over the links (Wi-Fi, phone) one by one.
    /// </summary>
    public TorrentEngine Torrents
    {
        get
        {
            lock (_torrentGate)
            {
                _torrentLinks ??= new LinkRoundRobin(() => _linkCache.Get() ?? [], IsLinkAlive);
                return _torrents ??= new TorrentEngine(new TorrentEngineOptions(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arqon", "torrent-cache"),
                    Links: _torrentLinks));
            }
        }
    }

    /// <summary>How many peer connections each link got so far (empty until a torrent was used).</summary>
    public IReadOnlyDictionary<string, int> TorrentConnectionsPerLink() => _torrentLinks?.Counts ?? new Dictionary<string, int>();

    private bool IsLinkAlive(LinkInfo link) =>
        !Monitor.Latest.TryGetValue(link.Id, out var sample) || sample.Loss < 1;

    /// <summary>Holds system mode (and unloads its driver) while a game runs, so anti-cheat never sees a network driver.</summary>
    public SafeGameMode SafeGame { get; }

    /// <summary>The link a game would use right now (steadiest, with hysteresis); null when no link is measured.</summary>
    public string? GameLinkId { get; private set; }

    public event Action? SettingsChanged;
    public event Action<Notice>? NoticeRaised;

    private AppServices(bool demo, ILinkProvider links, LinkMonitor monitor, ISettingsStore store, WindowsFixService fix,
        UsageTracker usage)
    {
        Demo = demo;
        Links = links;
        Monitor = monitor;
        SettingsStore = store;
        Fix = fix;
        Usage = usage;
        Settings = store.Load();
        Units.Unit = UnitOf(Settings);

        _linkCache = new TtlCache<IReadOnlyList<LinkInfo>>(links.GetLinks, TimeSpan.FromSeconds(1));
        var defaultAddress = new TtlCache<System.Net.IPAddress?>(demo ? () => DemoLinkProvider.Wifi.LocalAddress : DefaultRoute.LocalAddress,
            TimeSpan.FromSeconds(2));
        Func<IReadOnlyList<LinkInfo>> cachedLinks = () => _linkCache.Get() ?? [];
        Func<System.Net.IPAddress?> cachedDefault = () => defaultAddress.Get();

        Guard = new GameGuard(Throttle, () => Games!.GameRunning) { Enabled = Settings.GameProtection };
        Games = new GameWatcher(demo ? DemoProcesses : GameWatcher.CurrentProcessNames, () => Rules);
        Games.Changed += OnGamesChanged;

        var coordinator = new SteeringCoordinator(null, new FlowTable(() => DateTimeOffset.UtcNow), () => Rules, LatestSamples);
        IProcessResolver resolver = demo ? new DemoResolver() : new WindowsProcessResolver();
        var engine = new NatSteeringEngine(coordinator, resolver, cachedLinks, cachedDefault, Environment.ProcessId, () => DateTimeOffset.UtcNow);
        ISteerer steerer = demo ? new DemoSteerer() : new WinDivertSteerer(engine, cachedLinks, cachedDefault);
        Steering = new SteeringService(steerer, coordinator, engine);
        Steering.StateChanged += OnSteeringChanged;

        // Demo mode has fake games running all the time, which would keep the demo's system mode card paused.
        SafeGame = new SafeGameMode(Steering, () => Games.GameRunning, demo ? () => { } : WinDivertDriver.Unload)
        {
            Enabled = Settings.SafeGameMode && !demo,
            WantsSystemMode = Settings.SystemMode,
        };
        SafeGame.Changed += OnSafeGameChanged;
        if (!demo)
        {
            engine.Log += SteeringLog.Write;
            Steering.StateChanged += () =>
            {
                var links = string.Join("; ", cachedLinks().Select(l => $"{l.Name} {l.LocalAddress} gw {l.Gateway}"));
                SteeringLog.Write($"system mode {Steering.State}{(Steering.Reason is { } r ? $" ({r})" : "")}; default address {cachedDefault()}; links: {links}");
            };
        }
    }

    public static AppServices Create(bool demo)
    {
        if (demo)
        {
            var provider = new DemoLinkProvider();
            var monitor = new LinkMonitor(provider, new DemoProbe(), new DemoCounters(), TimeSpan.FromSeconds(1));
            return new AppServices(true, provider, monitor, new MemorySettingsStore(), new WindowsFixService(new DemoWcmPolicy()),
                DemoUsage.Seeded());
        }

        var links = new NetworkLinkProvider();
        var realMonitor = new LinkMonitor(links, new TcpConnectProbe(), new InterfaceCounterReader(), TimeSpan.FromSeconds(1));
        return new AppServices(false, links, realMonitor, new JsonSettingsStore(JsonSettingsStore.DefaultPath),
            new WindowsFixService(new RegistryWcmPolicy()), new UsageTracker(UsageTracker.DefaultPath));
    }

    /// <summary>Starts monitoring, the background upkeep loop and, if it was left on, system mode.</summary>
    public void Start()
    {
        Monitor.SampleReady += OnSample;
        Monitor.Start();
        Games.Refresh();
        _maintenance = new Timer(_ => Maintain(), null, MaintenanceEvery, MaintenanceEvery);

        // A game that is already running keeps system mode off (and unloads a driver left over from an earlier session).
        SafeGame.Evaluate();

        // The preference is kept when the driver is missing or Arqon is not elevated (it will start next time it can);
        // it is only cleared when system mode actually fails, see OnSteeringChanged.
        if (Settings.SystemMode && !SafeGame.Paused) Steering.Start();

        // Every second, not every 3: the sooner the driver is gone after a game starts, the less an anti-cheat's start-up scan can see.
        _gameTimer = new Timer(_ => RefreshGames(), null, GameCheckEvery, GameCheckEvery);
    }

    private void RefreshGames()
    {
        try
        {
            Games.Refresh();
            SafeGame.Evaluate();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The next tick tries again.
        }
    }

    public void SaveSettings(ArqonSettings settings)
    {
        Settings = settings;
        Units.Unit = UnitOf(settings);
        Guard.Enabled = settings.GameProtection;
        SettingsStore.Save(settings);
        SafeGame.Enabled = settings.SafeGameMode && !Demo;
        SafeGame.WantsSystemMode = settings.SystemMode;
        SafeGame.Evaluate();
        SettingsChanged?.Invoke();
    }

    private static SpeedUnit UnitOf(ArqonSettings s) => s.SpeedInBytes ? SpeedUnit.Bytes : SpeedUnit.Bits;

    public IReadOnlyList<LinkSteerStats> SteerStats() => Demo ? DemoSteerStats() : Steering.Engine.Stats();

    /// <summary>Combined speed of all links right now, in bytes per second.</summary>
    public (double Down, double Up) CurrentSpeed()
    {
        var samples = LatestSamples();
        return (samples.Sum(s => s.DownBps), samples.Sum(s => s.UpBps));
    }

    public void Dispose()
    {
        _maintenance?.Dispose();
        _gameTimer?.Dispose();
        lock (_torrentGate) _torrents?.Dispose();
        Monitor.SampleReady -= OnSample;
        Monitor.Stop();
        Steering.Stop();
        if (!Demo) WinDivertDriver.Unload();   // do not leave a loaded driver behind for an anti-cheat to find
        Usage.Save();
    }

    // ---- wiring ---------------------------------------------------------------------------------------------

    private IReadOnlyList<LinkSample> LatestSamples()
    {
        var links = _linkCache.Get() ?? [];
        var latest = Monitor.Latest;
        return links.Where(l => latest.ContainsKey(l.Id)).Select(l => latest[l.Id]).ToList();
    }

    private void OnSample(LinkSample sample)
    {
        var links = _linkCache.Get() ?? [];
        var name = links.FirstOrDefault(l => l.Id == sample.LinkId)?.Name ?? sample.LinkId;
        Usage.OnSample(sample, name);

        GameLinkId = LinkSelector.PickBestForGame(LatestSamples(), GameLinkId);
        var wasLimiting = Guard.IsLimiting(sample.LinkId);
        Guard.OnSample(sample, sample.LinkId == GameLinkId);
        if (!wasLimiting && Guard.IsLimiting(sample.LinkId)) NotifyCap(sample.LinkId, name);
    }

    private void NotifyCap(string linkId, string name)
    {
        var now = DateTimeOffset.UtcNow;
        if (_capNotified.TryGetValue(linkId, out var last) && now - last < TimeSpan.FromMinutes(2)) return;
        _capNotified[linkId] = now;
        Raise(Loc.Get("S.Notice.CapTitle"), Loc.Format("S.Notice.CapBody", name));
    }

    private void Maintain()
    {
        try
        {
            foreach (var change in _linkChanges.Update(Links.GetLinks()))
            {
                var key = change.Kind == LinkChangeKind.Appeared ? "S.Notice.LinkUp" : "S.Notice.LinkDown";
                Raise(Loc.Get(key + "Title"), Loc.Format(key + "Body", change.Link.Name));
            }

            if (Steering.State == SteeringState.Running && !Demo) Steering.Engine.CheckHealth();
            LimitTorrentsForGames();
            if (++_ticks % SaveUsageEveryTicks == 0) Usage.Save();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Upkeep must never take the app down; the next tick tries again.
        }
    }

    /// <summary>
    /// A torrent opens many connections at once and can fill a link on its own. While the game protection holds a link back
    /// for a game, the torrents are held to that same speed in total (downloading and seeding), so they cannot undo the protection.
    /// </summary>
    private void LimitTorrentsForGames()
    {
        TorrentEngine? engine;
        lock (_torrentGate) engine = _torrents;
        if (engine is null) return;

        var caps = (_linkCache.Get() ?? []).Select(l => Throttle.Get(l.Id)).Where(c => c is not null).Select(c => c!.Value).ToList();
        var cap = caps.Count == 0 ? (long?)null : caps.Min();
        _ = engine.SetDownloadLimitAsync(cap);
        _ = engine.SetUploadLimitAsync(cap);   // seeding must not undo the protection either
    }

    private void OnGamesChanged()
    {
        if (!Games.GameRunning) return;
        Raise(Loc.Get("S.Notice.GameTitle"), Loc.Format("S.Notice.GameBody", string.Join(", ", Games.RunningGames)));
    }

    /// <summary>A download or torrent finished.</summary>
    public void NotifyDownloadDone(string name) => Raise(Loc.Get("S.Notice.DownloadDoneTitle"), name);

    private void OnSafeGameChanged()
    {
        var key = SafeGame.Paused ? "S.Notice.SafePaused" : "S.Notice.SafeResumed";
        Raise(Loc.Get(key + "Title"), Loc.Get(key + "Body"));
    }

    private void OnSteeringChanged()
    {
        if (Steering.State != SteeringState.Faulted) return;
        Raise(Loc.Get("S.Notice.SteerFaultTitle"), Loc.Format("S.Notice.SteerFaultBody", Steering.Reason ?? ""));

        // This runs on the thread that detected the fault; settings listeners belong to the UI thread.
        if (Settings.SystemMode)
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => SaveSettings(Settings with { SystemMode = false }));
    }

    private void Raise(string title, string message)
    {
        if (!Settings.Notifications) return;
        NoticeRaised?.Invoke(new Notice(title, message));
    }

    // ---- demo -----------------------------------------------------------------------------------------------

    private static IEnumerable<string> DemoProcesses() => ["explorer.exe", "chrome.exe", "telegram.exe", "valorant-win64-shipping.exe"];

    private IReadOnlyList<LinkSteerStats> DemoSteerStats()
    {
        var t = _uptime.Elapsed.TotalSeconds;
        return
        [
            new LinkSteerStats("wifi", 14 + (long)(t / 3), (long)(t * 410_000), (long)(t * 5_300_000), false),
            new LinkSteerStats("phone", 11 + (long)(t / 4), (long)(t * 300_000), (long)(t * 3_900_000), false),
        ];
    }

    private sealed class DemoSteerer : ISteerer
    {
        public bool IsAvailable => true;
        public void Start() { }
        public void Stop() { }
        public event Action<string>? Faulted { add { } remove { } }
    }

    private sealed class DemoResolver : IProcessResolver
    {
        public ProcessInfo? Resolve(byte protocol, ushort localPort) => null;
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        private ArqonSettings _s = ArqonSettings.Default() with
        {
            SystemMode = true,
            Rules =
            [
                new AppRule("steam.exe", AppKind.Downloader, LinkChoice.Only, "phone", 1_250_000),
                new AppRule("telegram.exe", AppKind.Normal, LinkChoice.Both, null, null),
                new AppRule("windowsupdate", AppKind.Background, LinkChoice.Only, "phone", null),
            ],
        };

        public ArqonSettings Load() => _s;
        public void Save(ArqonSettings settings) => _s = settings;
    }
}

public static class LinkPalette
{
    public static Color ColorFor(LinkKind kind) => kind switch
    {
        LinkKind.Wifi => Color.FromRgb(0x4A, 0xDE, 0x80),
        LinkKind.Mobile => Color.FromRgb(0xF5, 0x9E, 0x0B),
        LinkKind.Ethernet => Color.FromRgb(0x6C, 0x8C, 0xFF),
        _ => Color.FromRgb(0x8B, 0x93, 0xA7),
    };

    public static string GlyphFor(LinkKind kind) => kind switch
    {
        LinkKind.Wifi => "",
        LinkKind.Mobile => "",
        LinkKind.Ethernet => "",
        _ => "",
    };
}
