using System.Windows.Media;
using Arqon.App.Infrastructure;
using Arqon.App.Services;
using Arqon.Core.Links;
using Arqon.Core.Meter;

namespace Arqon.App.ViewModels;

public sealed record MeterLinkRow(string Name, Brush Brush, string Today, string Month);

/// <summary>What the taskbar meter shows: live speed all the time, traffic totals while the pointer is over it.</summary>
public sealed class TaskbarMeterViewModel : ObservableObject
{
    private static readonly Color Fallback = Color.FromRgb(0x8B, 0x93, 0xA7);

    private readonly AppServices _services;
    private SpeedText _down = SpeedFormat.Of(0), _up = SpeedFormat.Of(0);
    private string _todayDown = "–", _todayUp = "–", _monthDown = "–", _monthUp = "–";
    private IReadOnlyList<MeterLinkRow> _links = [];

    public TaskbarMeterViewModel(AppServices services) => _services = services;

    public string DownValue => _down.Value;
    public string DownUnit => _down.Unit;
    public string UpValue => _up.Value;
    public string UpUnit => _up.Unit;

    public string TodayDown => _todayDown;
    public string TodayUp => _todayUp;
    public string MonthDown => _monthDown;
    public string MonthUp => _monthUp;
    public IReadOnlyList<MeterLinkRow> Links => _links;

    public void RefreshSpeed()
    {
        var (down, up) = _services.CurrentSpeed();
        _down = SpeedFormat.Of(down);
        _up = SpeedFormat.Of(up);
        Raise(nameof(DownValue));
        Raise(nameof(DownUnit));
        Raise(nameof(UpValue));
        Raise(nameof(UpUnit));
    }

    public void RefreshDetails()
    {
        var today = _services.Usage.Today();
        var month = _services.Usage.ThisMonth();
        _todayDown = Units.Size(today.Down);
        _todayUp = Units.Size(today.Up);
        _monthDown = Units.Size(month.Down);
        _monthUp = Units.Size(month.Up);

        var live = _services.Links.GetLinks();
        var todayByLink = _services.Usage.TodayByLink().ToDictionary(t => t.LinkId);
        _links = _services.Usage.MonthByLink().Select(m =>
        {
            var kind = live.FirstOrDefault(l => l.Id == m.LinkId)?.Kind;
            var color = kind is { } k ? LinkPalette.ColorFor(k) : Fallback;
            var t = todayByLink.GetValueOrDefault(m.LinkId);
            return new MeterLinkRow(m.Name, new SolidColorBrush(color), Units.Size((t?.Down ?? 0) + (t?.Up ?? 0)), Units.Size(m.Down + m.Up));
        }).ToList();

        Raise(nameof(TodayDown));
        Raise(nameof(TodayUp));
        Raise(nameof(MonthDown));
        Raise(nameof(MonthUp));
        Raise(nameof(Links));
    }
}
