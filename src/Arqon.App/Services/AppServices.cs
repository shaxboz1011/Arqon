using System.Diagnostics;
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
using Arqon.Core.Usage;
using Arqon.Core.Windows;

namespace Arqon.App.Services;

/// <summary>A message for the user that is worth a tray balloon (a link came or went, a game was found, a fault happened).</summary>
public sealed record Notice(string Title, string Message);

/// <summary>Composition root: the one place that decides between real Windows services and the demo data set.</summary>
public sealed class AppServices : IDisposable
{
    private static readonly TimeSpan MaintenanceEvery = TimeSpan.FromSeconds(3);
    private const int SaveUsageEveryTicks = 10;

    private readonly TtlCache<IReadOnlyList<LinkInfo>> _linkCache;
    private readonly LinkChangeDetector _linkChanges = new();
    private readonly Dictionary<string, DateTimeOffset> _capNotified = new();
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private Timer? _maintenance;
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

        // The preference is kept when the driver is missing or Arqon is not elevated (it will start next time it can);
        // it is only cleared when system mode actually fails, see OnSteeringChanged.
        if (Settings.SystemMode) Steering.Start();
    }

    public void SaveSettings(ArqonSettings settings)
    {
        Settings = settings;
        Units.Unit = UnitOf(settings);
        Guard.Enabled = settings.GameProtection;
        SettingsStore.Save(settings);
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
        Monitor.SampleReady -= OnSample;
        Monitor.Stop();
        Steering.Stop();
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
            Games.Refresh();
            foreach (var change in _linkChanges.Update(Links.GetLinks()))
            {
                var key = change.Kind == LinkChangeKind.Appeared ? "S.Notice.LinkUp" : "S.Notice.LinkDown";
                Raise(Loc.Get(key + "Title"), Loc.Format(key + "Body", change.Link.Name));
            }

            if (Steering.State == SteeringState.Running && !Demo) Steering.Engine.CheckHealth();
            if (++_ticks % SaveUsageEveryTicks == 0) Usage.Save();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Upkeep must never take the app down; the next tick tries again.
        }
    }

    private void OnGamesChanged()
    {
        if (!Games.GameRunning) return;
        Raise(Loc.Get("S.Notice.GameTitle"), Loc.Format("S.Notice.GameBody", string.Join(", ", Games.RunningGames)));
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
