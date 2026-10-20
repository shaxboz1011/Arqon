using Arqon.App.Infrastructure;
using Arqon.App.Services;

namespace Arqon.App.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Dictionary<string, ViewModelBase> _pages;
    private string _key = "panel";

    public ShellViewModel(AppServices services)
    {
        _services = services;
        _pages = new()
        {
            ["panel"] = new PanelViewModel(services),
            ["apps"] = new AppsViewModel(services),
            ["downloads"] = new DownloadsViewModel(services),
            ["usage"] = new UsageViewModel(services),
            ["settings"] = new SettingsViewModel(services),
        };
        NavigateCommand = new RelayCommand(p => Navigate((string)p!));
        UpdateCommand = new RelayCommand(() => _ = UpdateFlow.RunAsync(_services));
        _services.UpdateChanged += OnUpdateChanged;
    }

    private void OnUpdateChanged() => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Raise(string.Empty));

    public RelayCommand UpdateCommand { get; }
    public bool HasUpdate => _services.AvailableUpdate is not null;
    public bool Updating => _services.Updating;
    public bool UpdateIdle => !_services.Updating;
    public double UpdatePercent => _services.UpdateProgress * 100;
    public string UpdateLabel => _services.Updating
        ? Loc.Format("S.Upd.Working", (int)(_services.UpdateProgress * 100))
        : Loc.Format("S.Upd.Banner", _services.AvailableUpdate?.Tag ?? "");

    public RelayCommand NavigateCommand { get; }

    public ViewModelBase Current => _pages[_key];
    public bool IsPanel => _key == "panel";
    public bool IsApps => _key == "apps";
    public bool IsDownloads => _key == "downloads";
    public bool IsUsage => _key == "usage";
    public bool IsSettings => _key == "settings";

    public bool Demo => _services.Demo;

    public string VersionLine => "Arqon " + AppInfo.Version + " · MIT";

    public int ActiveLinks => _services.Links.GetLinks().Count;
    public string LinkSummary => ActiveLinks switch
    {
        0 => Loc.Get("S.Side.NoLinks"),
        1 => Loc.Get("S.Side.OneLink"),
        _ => Loc.Format("S.Side.ManyLinks", ActiveLinks),
    };

    public void Navigate(string key)
    {
        if (!_pages.ContainsKey(key) || key == _key) return;
        _key = key;
        if (key == "downloads") OnWindowActivated();
        Raise(string.Empty);
    }

    public void Refresh() => Raise(string.Empty);

    /// <summary>Something to download came from outside the page: a magnet link, a .torrent file, a dropped link.</summary>
    public void AddDownload(string text)
    {
        Navigate("downloads");
        (_pages["downloads"] as DownloadsViewModel)?.AddFromText(text);
    }

    /// <summary>The window came to the front: let pages react (the downloads page offers a copied link).</summary>
    public void OnWindowActivated() => (_pages["downloads"] as DownloadsViewModel)?.CheckClipboard();

    public override void Dispose()
    {
        _services.UpdateChanged -= OnUpdateChanged;
        foreach (var p in _pages.Values) p.Dispose();
        base.Dispose();
    }
}
