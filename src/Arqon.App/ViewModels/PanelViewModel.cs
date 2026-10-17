using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Arqon.App.Controls;
using Arqon.App.Infrastructure;
using Arqon.App.Services;
using Arqon.Core.Links;
using Arqon.Core.Monitoring;

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
        Series = [new ChartSeries(Name, Color, history.Select(h => Units.ToMbit(h.DownBps)).ToList())];
        Raise(string.Empty);
    }

    public IReadOnlyList<ChartSeries> Series { get => _series; private set => Set(ref _series, value); }
    public double DownBps => _sample?.DownBps ?? 0;
    public string Down => _sample is null ? "–" : Units.Mbit(_sample.DownBps);
    public string Up => _sample is null ? "–" : Units.Mbit(_sample.UpBps);
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

public sealed class PanelViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _timer;
    private string? _gameLinkId;
    private IReadOnlyList<ChartSeries> _totalSeries = [];
    private double _totalDown, _totalUp;
    private bool _needsFix;
    private string _fixMessage = "";

    public ObservableCollection<LinkCardViewModel> Links { get; } = [];

    public PanelViewModel(AppServices services)
    {
        _services = services;
        ApplyFixCommand = new RelayCommand(ApplyFix);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        _services.SettingsChanged += Refresh;
        Refresh();
    }

    public RelayCommand ApplyFixCommand { get; }

    public IReadOnlyList<ChartSeries> TotalSeries { get => _totalSeries; private set => Set(ref _totalSeries, value); }
    public string TotalDown => Units.Mbit(_totalDown);
    public string TotalUp => Units.Mbit(_totalUp);
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

    public bool GameProtectionOn => _services.Settings.GameProtection;
    public string GameLinkName => Links.FirstOrDefault(l => l.Id == _gameLinkId)?.Name ?? "–";
    public Brush GameLinkBrush => Links.FirstOrDefault(l => l.Id == _gameLinkId)?.Brush ?? Brushes.Gray;
    public string GamePing => Links.FirstOrDefault(l => l.Id == _gameLinkId)?.Ping ?? "–";
    public string GameHeadline => !GameProtectionOn ? Loc.Get("S.Game.Off")
        : _gameLinkId is null ? Loc.Get("S.Game.NoLink")
        : Loc.Format("S.Game.Using", GameLinkName);

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
        _gameLinkId = LinkSelector.PickBestForGame(samples, _gameLinkId);

        TotalSeries = links
            .Select(l => new ChartSeries(l.Name, LinkPalette.ColorFor(l.Kind),
                _services.Monitor.History(l.Id).Select(h => Units.ToMbit(h.DownBps)).ToList()))
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
