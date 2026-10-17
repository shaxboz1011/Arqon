using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Arqon.App.Infrastructure;
using Arqon.App.Services;
using Arqon.Core.Policy;
using Microsoft.Win32;

namespace Arqon.App.ViewModels;

/// <summary>One entry of the link drop-down. A record, so SelectedItem matches by value.</summary>
public sealed record ChoiceOption(string Label, LinkChoice Choice, string? LinkId);

public sealed record KindOption(string Label, AppKind Kind);

public sealed class AppRuleViewModel : ViewModelBase
{
    private readonly AppsViewModel _owner;
    private AppRule _rule;

    public AppRuleViewModel(AppsViewModel owner, AppRule rule)
    {
        _owner = owner;
        _rule = rule;
        RemoveCommand = new RelayCommand(() => owner.Remove(this));
    }

    public AppRule Rule => _rule;
    public RelayCommand RemoveCommand { get; }
    public string ExeName => _rule.ExeName;
    public string Initial => string.IsNullOrEmpty(_rule.ExeName) ? "?" : _rule.ExeName[..1].ToUpperInvariant();
    public bool IsGame => _rule.Kind == AppKind.Game;

    public IReadOnlyList<ChoiceOption> ChoiceOptions => _owner.ChoiceOptions;
    public IReadOnlyList<KindOption> KindOptions => _owner.KindOptions;

    public ChoiceOption SelectedChoice
    {
        get => _owner.ChoiceOptions.FirstOrDefault(o => o.Choice == _rule.Choice && (o.Choice != LinkChoice.Only || o.LinkId == _rule.LinkId))
               ?? _owner.ChoiceOptions[0];
        set
        {
            if (value is null) return;
            _rule = _rule with { Choice = value.Choice, LinkId = value.Choice == LinkChoice.Only ? value.LinkId : null };
            _owner.Persist();
            Raise(string.Empty);
        }
    }

    public KindOption SelectedKind
    {
        get => _owner.KindOptions.First(o => o.Kind == _rule.Kind);
        set
        {
            if (value is null) return;
            _rule = _rule with { Kind = value.Kind };
            _owner.Persist();
            Raise(string.Empty);
        }
    }

    /// <summary>Speed cap in Mbit/s; empty means unlimited.</summary>
    public string LimitText
    {
        get => _rule.LimitBps is { } b ? Units.ToMbit(b).ToString("0.##", CultureInfo.InvariantCulture) : "";
        set
        {
            long? bps = null;
            if (double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var mbit) && mbit > 0)
                bps = (long)(mbit * 1_000_000 / 8);
            _rule = _rule with { LimitBps = bps };
            _owner.Persist();
            Raise(nameof(LimitText));
        }
    }

    public void RefreshOptions() => Raise(nameof(ChoiceOptions));
}

public sealed class AppsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private bool _loading;
    private bool _pickerOpen;

    public ObservableCollection<AppRuleViewModel> Rules { get; } = [];
    public ObservableCollection<string> RunningApps { get; } = [];
    public IReadOnlyList<ChoiceOption> ChoiceOptions { get; private set; } = [];
    public IReadOnlyList<KindOption> KindOptions { get; private set; } = [];

    public RelayCommand AddFromFileCommand { get; }
    public RelayCommand TogglePickerCommand { get; }
    public RelayCommand AddRunningCommand { get; }

    public AppsViewModel(AppServices services)
    {
        _services = services;
        AddFromFileCommand = new RelayCommand(AddFromFile);
        TogglePickerCommand = new RelayCommand(TogglePicker);
        AddRunningCommand = new RelayCommand(p => { if (p is string s) { Add(s); PickerOpen = false; } });
        Loc.Changed += Rebuild;
        services.SettingsChanged += OnSettingsChanged;
        Rebuild();
    }

    public bool PickerOpen { get => _pickerOpen; set => Set(ref _pickerOpen, value); }
    public bool IsEmpty => Rules.Count == 0;
    public bool SystemModeOff => !_services.Settings.SystemMode;
    public int KnownGames => GameCatalog.Count;

    private void OnSettingsChanged()
    {
        Raise(nameof(SystemModeOff));
    }

    private void Rebuild()
    {
        _loading = true;
        var options = new List<ChoiceOption>
        {
            new(Loc.Get("S.Apps.Auto"), LinkChoice.Auto, null),
            new(Loc.Get("S.Apps.Both"), LinkChoice.Both, null),
        };
        foreach (var link in _services.Links.GetLinks())
            options.Add(new ChoiceOption(Loc.Format("S.Apps.Only", link.Name), LinkChoice.Only, link.Id));
        foreach (var rule in _services.Settings.Rules.Where(r => r.Choice == LinkChoice.Only))
            if (options.All(o => o.LinkId != rule.LinkId))
                options.Add(new ChoiceOption(Loc.Format("S.Apps.OnlyOffline", rule.LinkId ?? "?"), LinkChoice.Only, rule.LinkId));
        ChoiceOptions = options;

        KindOptions =
        [
            new(Loc.Get("S.Kind.Normal"), AppKind.Normal),
            new(Loc.Get("S.Kind.Game"), AppKind.Game),
            new(Loc.Get("S.Kind.Downloader"), AppKind.Downloader),
            new(Loc.Get("S.Kind.Background"), AppKind.Background),
        ];

        Rules.Clear();
        foreach (var r in _services.Settings.Rules) Rules.Add(new AppRuleViewModel(this, r));
        _loading = false;
        Raise(string.Empty);
    }

    public void Persist()
    {
        if (_loading) return;
        _services.SaveSettings(_services.Settings with { Rules = Rules.Select(r => r.Rule).ToList() });
        Raise(nameof(IsEmpty));
    }

    public void Remove(AppRuleViewModel rule)
    {
        Rules.Remove(rule);
        Persist();
    }

    private void Add(string exe)
    {
        var name = GameCatalog.Normalize(exe);
        if (Rules.Any(r => string.Equals(GameCatalog.Normalize(r.ExeName), name, StringComparison.OrdinalIgnoreCase))) return;
        var resolved = _services.Rules.Resolve(name);
        Rules.Add(new AppRuleViewModel(this, resolved with { ExeName = name }));
        Persist();
    }

    private void AddFromFile()
    {
        var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = Loc.Get("S.Apps.Add") };
        if (dialog.ShowDialog() == true) Add(dialog.FileName);
    }

    private void TogglePicker()
    {
        if (!PickerOpen)
        {
            RunningApps.Clear();
            foreach (var name in Process.GetProcesses()
                         .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch (InvalidOperationException) { return false; } })
                         .Select(p => p.ProcessName + ".exe")
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                RunningApps.Add(name);
        }
        PickerOpen = !PickerOpen;
    }

    public override void Dispose()
    {
        Loc.Changed -= Rebuild;
        _services.SettingsChanged -= OnSettingsChanged;
        base.Dispose();
    }
}
