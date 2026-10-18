using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using Arqon.App.Controls;
using Arqon.App.Infrastructure;
using Arqon.App.Services;
using Arqon.Core.Usage;

namespace Arqon.App.ViewModels;

public sealed record UsageRow(string Name, Color Color, string Down, string Up, string Total, IReadOnlyList<BarPart> Share)
{
    public Brush Brush => new SolidColorBrush(Color);
}

public sealed record LegendItem(string Name, Brush Brush);

public sealed class UsageViewModel : ViewModelBase
{
    private const int Days = 14;
    private static readonly Color[] Fallback =
    [
        Color.FromRgb(0x4A, 0xDE, 0x80), Color.FromRgb(0xF5, 0x9E, 0x0B), Color.FromRgb(0x6C, 0x8C, 0xFF), Color.FromRgb(0xC0, 0x84, 0xFC),
    ];

    private readonly AppServices _services;
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<ChartSeries> _series = [];
    private IReadOnlyList<string> _labels = [];
    private IReadOnlyList<string> _tips = [];
    private IReadOnlyList<UsageRow> _rows = [];
    private UsageTotals _today = new(0, 0), _month = new(0, 0);
    private bool _hasData;

    public UsageViewModel(AppServices services)
    {
        _services = services;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    public IReadOnlyList<ChartSeries> Series => _series;
    public IReadOnlyList<string> Labels => _labels;
    public IReadOnlyList<string> Tips => _tips;
    public IReadOnlyList<UsageRow> Rows => _rows;
    public bool HasData => _hasData;
    public bool NoData => !_hasData;

    public string TodayDown => Units.Size(_today.Down);
    public string TodayUp => Units.Size(_today.Up);
    public string MonthDown => Units.Size(_month.Down);
    public string MonthUp => Units.Size(_month.Up);

    public IReadOnlyList<LegendItem> Legend => _series.Select(x => new LegendItem(x.Name, new SolidColorBrush(x.Color))).ToList();

    private void Refresh()
    {
        var usage = _services.Usage.Last(Days);
        var links = _services.Links.GetLinks();
        _today = _services.Usage.Today();
        _month = _services.Usage.ThisMonth();
        _hasData = usage.Any(l => l.Days.Any(d => d.Down + d.Up > 0));

        var colors = usage
            .Select((l, i) => (l.LinkId, Color: links.FirstOrDefault(x => x.Id == l.LinkId) is { } live
                ? LinkPalette.ColorFor(live.Kind) : Fallback[i % Fallback.Length]))
            .ToDictionary(x => x.LinkId, x => x.Color);

        _series = usage.Select(l => new ChartSeries(l.Name, colors[l.LinkId], l.Days.Select(d => (double)(d.Down + d.Up)).ToList())).ToList();

        var days = usage.FirstOrDefault()?.Days.Select(d => d.Date).ToList() ?? [];
        _labels = days.Select(d => d.Day.ToString(CultureInfo.InvariantCulture)).ToList();
        _tips = days.Select((d, i) =>
        {
            var parts = usage.Select(l => $"{l.Name}: {BarChart.FormatBytes(l.Days[i].Down + l.Days[i].Up)}");
            return d.ToString("d MMM yyyy", CultureInfo.CurrentUICulture) + "\n" + string.Join("\n", parts);
        }).ToList();

        var monthStart = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        var monthTotals = _services.Usage.Last(31)
            .Select(l => (Link: l, Down: Sum(l, monthStart, d => d.Down), Up: Sum(l, monthStart, d => d.Up)))
            .Where(x => x.Down + x.Up > 0)
            .OrderByDescending(x => x.Down + x.Up)
            .ToList();
        var grand = Math.Max(1, monthTotals.Sum(x => x.Down + x.Up));
        _rows = monthTotals.Select(x => new UsageRow(x.Link.Name, colors[x.Link.LinkId], Units.Size(x.Down), Units.Size(x.Up),
            Units.Size(x.Down + x.Up), [new BarPart(colors[x.Link.LinkId], x.Down + x.Up), new BarPart(Color.FromRgb(0x23, 0x2A, 0x3A), grand - (x.Down + x.Up))])).ToList();

        Raise(string.Empty);
    }

    /// <summary>The 14-day window can start before the month does, so the monthly figure is summed from the days inside both.</summary>
    private static long Sum(LinkUsage link, DateOnly from, Func<DayUsage, long> pick) =>
        link.Days.Where(d => d.Date >= from).Sum(pick);

    public override void Dispose()
    {
        _timer.Stop();
        base.Dispose();
    }
}
