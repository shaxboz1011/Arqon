using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Arqon.App.Controls;
using Arqon.App.Infrastructure;
using Arqon.App.Services;
using Arqon.Core.Download;
using Arqon.Core.Links;
using Arqon.Core.Speed;
using Arqon.Core.Monitoring;
using Arqon.Core.Steering;

namespace Arqon.App.ViewModels;

public sealed class LinkCardViewModel(LinkInfo link) : ViewModelBase
{
    public string Id { get; } = link.Id;
    public string Name { get; } = link.Name;
    public string Glyph { get; } = LinkPalette.GlyphFor(link.Kind);
    public Color Color { get; } = LinkPalette.ColorFor(link.Kind);
    public Brush Brush => new SolidColorBrush(Color);

    private LinkSample? _sample;
    private IReadOnlyList<ChartSeries> _series = [];

    public void Update(LinkSample sample, IReadOnlyList<LinkSample> history)
    {
        _sample = sample;
        Series = [new ChartSeries(Name, Color, history.Select(h => Units.ToSpeed(h.DownBps)).ToList())];
        Raise(string.Empty);
    }

    public IReadOnlyList<ChartSeries> Series { get => _series; private set => Set(ref _series, value); }
    public double DownBps => _sample?.DownBps ?? 0;
    public string Down => _sample is null ? "–" : Units.Speed(_sample.DownBps);
    public string Up => _sample is null ? "–" : Units.Speed(_sample.UpBps);
    public string SpeedUnitLabel => Units.SpeedLabel;
    public string Ping => _sample is null || _sample.Loss >= 1 ? "–" : FormattableString.Invariant($"{_sample.LatencyMs:0}");
    public string Jitter => _sample is null ? "–" : FormattableString.Invariant($"{_sample.JitterMs:0.#}");
    public string Loss => _sample is null ? "–" : FormattableString.Invariant($"{_sample.Loss * 100:0.#}%");

    public string Status
    {
        get
        {
            if (_sample is null) return Loc.Get("S.Status.Checking");
            if (_sample.Loss >= 1) return Loc.Get("S.Status.Offline");
            if (_sample.Loss > 0.05 || _sample.JitterMs > 25) return Loc.Get("S.Status.Unstable");
            return LinkScorer.Score(_sample) < 60 ? Loc.Get("S.Status.Excellent") : Loc.Get("S.Status.Good");
        }
    }

    public bool IsHealthy => _sample is { Loss: < 0.05, JitterMs: < 25 };
}

public sealed record SteerLinkViewModel(string Name, Color Color, long Flows, bool Paused)
{
    public Brush Brush => new SolidColorBrush(Color);
    public string FlowsText => Loc.Format("S.Steer.Flows", Flows);
    public string Note => Paused ? Loc.Get("S.Steer.Paused") : "";
    public bool HasNote => Paused;
}

public sealed class PanelViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<ChartSeries> _totalSeries = [];
    private double _totalDown, _totalUp;
    private IReadOnlyList<LinkSteerStats> _steerStats = [];
    private bool _needsFix;
    private string _fixMessage = "";

    public ObservableCollection<LinkCardViewModel> Links { get; } = [];

    public PanelViewModel(AppServices services)
    {
        _services = services;
        ApplyFixCommand = new RelayCommand(ApplyFix);
        TestAloneCommand = new RelayCommand(() => _ = RunTest(alone: true, together: false));
        TestTogetherCommand = new RelayCommand(() => _ = RunTest(alone: false, together: true));
        TestCompareCommand = new RelayCommand(() => _ = RunTest(alone: true, together: true));
        TestStopCommand = new RelayCommand(() => _testCancel?.Cancel());
        DoctorCommand = new RelayCommand(() => _ = RunDoctorAsync());
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        _services.SettingsChanged += Refresh;
        Refresh();
    }

    public RelayCommand ApplyFixCommand { get; }

    // ---- speed test: each connection alone, all together, and the gain from combining them

    private const double TestSeconds = 6;
    private CancellationTokenSource? _testCancel;
    private bool _testRunning;
    private string _testStatus = "";
    private IReadOnlyList<string> _testLines = [];

    public RelayCommand TestAloneCommand { get; }
    public RelayCommand TestTogetherCommand { get; }
    public RelayCommand TestCompareCommand { get; }
    public RelayCommand TestStopCommand { get; }

    public bool TestRunning { get => _testRunning; private set { Set(ref _testRunning, value); Raise(nameof(TestIdle)); } }
    public bool TestIdle => !_testRunning && !_services.Demo;
    public bool TestCanCombine => Links.Count >= 2 && TestIdle;
    public string TestStatus { get => _testStatus; private set => Set(ref _testStatus, value); }
    public IReadOnlyList<string> TestLines { get => _testLines; private set => Set(ref _testLines, value); }
    public bool HasTestLines => _testLines.Count > 0;

    private async Task RunTest(bool alone, bool together)
    {
        if (_testRunning || _services.Demo) return;
        var links = _services.Links.GetLinks().ToList();
        if (links.Count == 0 || (together && !alone && links.Count < 2)) return;

        _testCancel = new CancellationTokenSource();
        var clients = new List<HttpClient>();
        HttpClient Make(LinkInfo link) { var c = BoundHttp.Create(link); lock (clients) clients.Add(c); return c; }
        var tester = new LinkSpeedTest(Make, LinkSpeedTest.DefaultUrl);
        var lines = new List<string>();
        TestRunning = true;
        TestLines = [];
        try
        {
            var single = new List<double>();
            if (alone)
            {
                foreach (var link in links)
                {
                    TestStatus = Loc.Format("S.Test.Running", link.Name);
                    var result = await tester.RunAsync([link], TimeSpan.FromSeconds(TestSeconds), _testCancel.Token);
                    if (_testCancel.IsCancellationRequested) break;
                    single.Add(result.TotalMbit);
                    lines.Add(result.Errors.TryGetValue(link.Id, out var error)
                        ? Loc.Format("S.Test.Error", link.Name, error)
                        : Loc.Format("S.Test.Line", link.Name, Mbit(result.TotalMbit)));
                    TestLines = lines.ToList();
                }
            }
            if (together && links.Count >= 2 && !_testCancel.IsCancellationRequested)
            {
                TestStatus = Loc.Get("S.Test.RunningAll");
                var result = await tester.RunAsync(links, TimeSpan.FromSeconds(TestSeconds), _testCancel.Token);
                if (!_testCancel.IsCancellationRequested)
                {
                    foreach (var link in links) lines.Add("   " + Loc.Format("S.Test.Line", link.Name, Mbit(result.Mbit(link.Id))));
                    lines.Add(Loc.Format("S.Test.Total", Mbit(result.TotalMbit)));
                    if (single.Count > 0 && single.Max() > 0)
                        lines.Add(Loc.Format("S.Test.Gain", (result.TotalMbit / single.Max()).ToString("0.00", System.Globalization.CultureInfo.CurrentCulture)));
                    TestLines = lines.ToList();
                }
            }
        }
        finally
        {
            lock (clients) foreach (var c in clients) c.Dispose();
            _testCancel.Dispose();
            _testCancel = null;
            TestStatus = "";
            TestRunning = false;
            Raise(string.Empty);
        }
    }

    // ---- network doctor: why combining does not help, in one click

    private bool _doctorRunning;
    private IReadOnlyList<string> _doctorLines = [];

    public RelayCommand DoctorCommand { get; }
    public bool DoctorIdle => !_doctorRunning && !_services.Demo;
    public bool DoctorRunning => _doctorRunning;
    public IReadOnlyList<string> DoctorLines => _doctorLines;
    public bool HasDoctorLines => _doctorLines.Count > 0;

    private async Task RunDoctorAsync()
    {
        if (_doctorRunning || _services.Demo) return;
        _doctorRunning = true;
        _doctorLines = [];
        Raise(string.Empty);
        try
        {
            var doctor = new Core.Doctor.NetworkDoctor(BoundHttp.Create, windowsPolicyNeedsFix: () => _services.Fix.NeedsFix);
            var report = await doctor.RunAsync(_services.Links.GetLinks());
            _doctorLines = report.Findings.Select(f => (f.Level switch
            {
                Core.Doctor.FindingLevel.Ok => "✓  ",
                Core.Doctor.FindingLevel.Warning => "⚠  ",
                _ => "✗  ",
            }) + Loc.Format("S.Doc.F." + f.Key, [.. f.Args])).ToList();
        }
        finally
        {
            _doctorRunning = false;
            Raise(string.Empty);
        }
    }

    private static string Mbit(double value) => value.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);

    public IReadOnlyList<ChartSeries> TotalSeries { get => _totalSeries; private set => Set(ref _totalSeries, value); }
    public string TotalDown => Units.Speed(_totalDown);
    public string TotalUp => Units.Speed(_totalUp);
    public string SpeedUnitLabel => Units.SpeedLabel;
    public int LinkCount => Links.Count;
    public bool NoLinks => Links.Count == 0;
    public bool SingleLink => Links.Count == 1;
    public bool HasLinks => Links.Count > 0;

    public bool NeedsFix { get => _needsFix; private set => Set(ref _needsFix, value); }
    public string FixMessage { get => _fixMessage; private set => Set(ref _fixMessage, value); }
    public bool HasFixMessage => _fixMessage.Length > 0;

    public IReadOnlyList<BarPart> SpeedSplit => Links.Select(l => new BarPart(l.Color, l.DownBps)).ToList();

    public string Subtitle => Links.Count switch
    {
        0 => Loc.Get("S.Panel.SubNone"),
        1 => Loc.Get("S.Panel.SubOne"),
        _ => Loc.Format("S.Panel.SubMany", Links.Count),
    };

    private string? GameLinkId => _services.GameLinkId;
    public bool GameProtectionOn => _services.Settings.GameProtection;
    public string GameLinkName => Links.FirstOrDefault(l => l.Id == GameLinkId)?.Name ?? "–";
    public Brush GameLinkBrush => Links.FirstOrDefault(l => l.Id == GameLinkId)?.Brush ?? Brushes.Gray;
    public string GamePing => Links.FirstOrDefault(l => l.Id == GameLinkId)?.Ping ?? "–";
    public string GameHeadline => !GameProtectionOn ? Loc.Get("S.Game.Off")
        : GameLinkId is null ? Loc.Get("S.Game.NoLink")
        : Loc.Format("S.Game.Using", GameLinkName);

    /// <summary>Second line of the game card: which game was found, and whether downloads are being held back for it.</summary>
    public string GameDetail
    {
        get
        {
            if (!GameProtectionOn || !_services.Games.GameRunning) return "";
            var capped = Links
                .Select(l => (Link: l, Limit: _services.Throttle.Get(l.Id)))
                .FirstOrDefault(x => x.Limit is not null);
            var found = Loc.Format("S.Game.Detected", string.Join(", ", _services.Games.RunningGames));
            return capped.Limit is { } limit
                ? found + " " + Loc.Format("S.Game.Capped", capped.Link.Name, Units.Speed(limit), Units.SpeedLabel)
                : found;
        }
    }

    public bool HasGameDetail => GameDetail.Length > 0;
    public bool GameCapped => Links.Any(l => _services.Throttle.Get(l.Id) is not null);

    public bool SteeringRunning => _services.Steering.State == SteeringState.Running;
    public bool SteeringFaulted => _services.Steering.State == SteeringState.Faulted;
    public bool SteeringPaused => _services.SafeGame.Paused;
    public bool ShowSteering => SteeringRunning || SteeringFaulted || SteeringPaused;
    public string SteeringFault => Loc.Format("S.Steer.Fault", _services.Steering.Reason ?? "");

    public IReadOnlyList<SteerLinkViewModel> SteerLinks => Links
        .Select(l =>
        {
            var stat = _steerStats.FirstOrDefault(x => x.LinkId == l.Id);
            return new SteerLinkViewModel(l.Name, l.Color, stat?.Flows ?? 0, stat?.Quarantined ?? false);
        })
        .ToList();

    public IReadOnlyList<BarPart> SteerSplit => SteerLinks.Select(l => new BarPart(l.Color, Math.Max(0, l.Flows))).ToList();

    private void Refresh()
    {
        var links = _services.Links.GetLinks();
        var latest = _services.Monitor.Latest;

        for (var i = Links.Count - 1; i >= 0; i--)
            if (links.All(l => l.Id != Links[i].Id)) Links.RemoveAt(i);
        foreach (var link in links)
        {
            var card = Links.FirstOrDefault(c => c.Id == link.Id);
            if (card is null) Links.Add(card = new LinkCardViewModel(link));
            if (latest.TryGetValue(link.Id, out var sample))
                card.Update(sample, _services.Monitor.History(link.Id));
        }

        var samples = links.Where(l => latest.ContainsKey(l.Id)).Select(l => latest[l.Id]).ToList();
        _totalDown = samples.Sum(s => s.DownBps);
        _totalUp = samples.Sum(s => s.UpBps);
        _steerStats = _services.SteerStats();

        TotalSeries = links
            .Select(l => new ChartSeries(l.Name, LinkPalette.ColorFor(l.Kind),
                _services.Monitor.History(l.Id).Select(h => Units.ToSpeed(h.DownBps)).ToList()))
            .ToList();

        NeedsFix = _services.Fix.NeedsFix;
        Raise(string.Empty);
    }

    private void ApplyFix()
    {
        var answer = MessageBox.Show(
            Loc.Get("S.Fix.Confirm") + "\n\n" + _services.Fix.Describe(),
            "Arqon", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        try
        {
            _services.Fix.Apply();
            FixMessage = Loc.Get("S.Fix.Done");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            FixMessage = Loc.Get("S.Fix.NeedAdmin");
        }
        Refresh();
    }

    public override void Dispose()
    {
        _timer.Stop();
        _services.SettingsChanged -= Refresh;
        base.Dispose();
    }
}
