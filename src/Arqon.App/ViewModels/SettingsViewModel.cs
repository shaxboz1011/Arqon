using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Arqon.App.Infrastructure;
using Arqon.App.Services;
using Arqon.Core.Steering;
using Arqon.Core.Windows;
using Microsoft.Win32;

namespace Arqon.App.ViewModels;

public sealed record LanguageOption(string Label, string Code);

public sealed record RatioOption(string Label, double Value);

public sealed record SpeedUnitOption(string Label, bool InBytes);

public sealed record LimitOption(string Label, int Megabytes);

public sealed class SettingsViewModel : ViewModelBase
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly AppServices _services;
    private string _fixMessage = "";

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        FixCommand = new RelayCommand(ApplyFix);
        RestartAsAdminCommand = new RelayCommand(RestartAsAdmin);
        CheckNowCommand = new RelayCommand(() => _ = CheckNow());
        DownloadUpdateCommand = new RelayCommand(OpenUpdatePage);
        _services.Steering.StateChanged += OnSteeringChanged;
        _services.SafeGame.Changed += OnSteeringChanged;
        _services.UpdateChanged += OnSteeringChanged;
    }

    private void OnSteeringChanged() =>
        Application.Current?.Dispatcher.BeginInvoke(() => Raise(string.Empty));

    public override void Dispose()
    {
        _services.Steering.StateChanged -= OnSteeringChanged;
        _services.SafeGame.Changed -= OnSteeringChanged;
        _services.UpdateChanged -= OnSteeringChanged;
        base.Dispose();
    }

    public RelayCommand FixCommand { get; }
    public RelayCommand CheckNowCommand { get; }
    public RelayCommand DownloadUpdateCommand { get; }

    private bool _checking;

    public bool CheckUpdates
    {
        get => _services.Settings.CheckUpdates;
        set { _services.SaveSettings(_services.Settings with { CheckUpdates = value }); Raise(); }
    }

    public bool HasUpdate => _services.AvailableUpdate is not null;

    public string UpdateButton => _services.AvailableUpdate is { } u ? Loc.Format("S.Upd.Download", u.Tag) : "";

    public string UpdateStatus
    {
        get
        {
            if (_checking) return Loc.Get("S.Upd.Checking");
            if (_services.AvailableUpdate is { } update) return Loc.Format("S.Upd.Available", update.Tag, AppInfo.Version);
            if (_services.LastUpdateCheck is null) return Loc.Get("S.Upd.Never");
            return _services.LastUpdateCheckFailed ? Loc.Get("S.Upd.Failed") : Loc.Format("S.Upd.Latest", AppInfo.Version);
        }
    }

    private async Task CheckNow()
    {
        _checking = true;
        Raise(string.Empty);
        try { await _services.CheckForUpdatesAsync(); }
        finally
        {
            _checking = false;
            Application.Current?.Dispatcher.BeginInvoke(() => Raise(string.Empty));
        }
    }

    private void OpenUpdatePage()
    {
        if (_services.AvailableUpdate is { } update)
            Process.Start(new ProcessStartInfo(update.Url) { UseShellExecute = true });
    }

    public IReadOnlyList<LimitOption> DailyLimits { get; } =
    [
        new("–", 0), new("500 MB", 500), new("1 GB", 1024), new("2 GB", 2048), new("5 GB", 5120), new("10 GB", 10240), new("20 GB", 20480),
    ];

    public LimitOption SelectedDailyLimit
    {
        get => DailyLimits.FirstOrDefault(l => l.Megabytes == _services.Settings.DailyLimitMb) ?? DailyLimits[0];
        set
        {
            if (value is null || value.Megabytes == _services.Settings.DailyLimitMb) return;
            _services.SaveSettings(_services.Settings with { DailyLimitMb = value.Megabytes });
            Raise();
        }
    }
    public RelayCommand RestartAsAdminCommand { get; }

    public IReadOnlyList<LanguageOption> Languages { get; } = [new("O'zbekcha", "uz"), new("English", "en"), new("Русский", "ru")];

    public LanguageOption SelectedLanguage
    {
        get => Languages.First(l => l.Code == _services.Settings.Language);
        set
        {
            if (value is null || value.Code == _services.Settings.Language) return;
            _services.SaveSettings(_services.Settings with { Language = value.Code });
            Loc.Apply(value.Code);
        }
    }

    public bool GameProtection
    {
        get => _services.Settings.GameProtection;
        set
        {
            _services.SaveSettings(_services.Settings with { GameProtection = value });
            Raise();
        }
    }

    /// <summary>System-wide steering needs the packet driver and administrator rights.</summary>
    public bool SystemModeAvailable => _services.Steering.IsAvailable;

    /// <summary>On while system mode runs, and also while safe game mode holds it back for a running game (the wish is kept).</summary>
    public bool SystemMode
    {
        get => _services.Steering.State == SteeringState.Running || _services.SafeGame.Paused;
        set
        {
            if (value == SystemMode) return;
            if (!value)
            {
                _services.Steering.Stop();
                _services.SaveSettings(_services.Settings with { SystemMode = false });
            }
            else if (_services.SafeGame.WouldPause)
            {
                // A game is running: remember the wish, it starts when the game ends.
                _services.SaveSettings(_services.Settings with { SystemMode = true });
            }
            else
            {
                var on = _services.Steering.Start();
                _services.SaveSettings(_services.Settings with { SystemMode = on });
            }
            Raise(string.Empty);
        }
    }

    public IReadOnlyList<int> ParallelOptions { get; } = [1, 2, 3, 4, 5, 6, 8, 10];

    public int MaxConcurrentDownloads
    {
        get => _services.Settings.MaxConcurrentDownloads;
        set
        {
            if (value < 1 || value == _services.Settings.MaxConcurrentDownloads) return;
            _services.SaveSettings(_services.Settings with { MaxConcurrentDownloads = value });
            Raise();
        }
    }

    /// <summary>Keep sharing a finished torrent (off by default: it uses the upload speed and sends data to strangers).</summary>
    public bool SeedTorrents
    {
        get => _services.Settings.SeedTorrents;
        set
        {
            if (value == _services.Settings.SeedTorrents) return;
            _services.SaveSettings(_services.Settings with { SeedTorrents = value });
            Raise();
        }
    }

    public IReadOnlyList<RatioOption> SeedRatios { get; } = [new("1×", 1.0), new("2×", 2.0), new("3×", 3.0), new("∞", 0.0)];

    public RatioOption SelectedSeedRatio
    {
        get => SeedRatios.FirstOrDefault(r => r.Value == _services.Settings.SeedRatio) ?? SeedRatios[0];
        set
        {
            if (value is null || value.Value == _services.Settings.SeedRatio) return;
            _services.SaveSettings(_services.Settings with { SeedRatio = value.Value });
            Raise();
        }
    }

    private readonly ShellAssociations _shellAssociations = new(Environment.ProcessPath ?? "Arqon.exe");

    /// <summary>Windows opens magnet links and .torrent files with this copy of Arqon.</summary>
    public bool OpensTorrents
    {
        get => !_services.Demo && _shellAssociations.IsRegistered();
        set
        {
            if (_services.Demo) return;
            if (value) _shellAssociations.Register(); else _shellAssociations.Unregister();
            Raise();
        }
    }

    /// <summary>Another program (say a torrent client) still opens magnet links: Windows keeps the choice for the user to make.</summary>
    public bool MagnetOwnedByOther => !_services.Demo && _shellAssociations.IsRegistered() && _shellAssociations.OtherMagnetHandler() is not null;

    public string ShellOtherNote
    {
        get
        {
            var command = _shellAssociations.OtherMagnetHandler() ?? "";
            var exe = command.StartsWith('"') ? command.Split('"', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? command : command.Split(' ')[0];
            return Loc.Format("S.Set.ShellOther", System.IO.Path.GetFileNameWithoutExtension(exe));
        }
    }

    public RelayCommand OpenDefaultAppsCommand { get; } =
        new(() => Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }));

    public bool SafeGameMode
    {
        get => _services.Settings.SafeGameMode;
        set { _services.SaveSettings(_services.Settings with { SafeGameMode = value }); Raise(string.Empty); }
    }

    public string SystemModeNote
    {
        get
        {
            var steering = _services.Steering;
            if (_services.SafeGame.Paused) return Loc.Get("S.Set.SystemModePaused");
            if (steering.State == SteeringState.Running) return Loc.Get("S.Set.SystemModeOn");
            if (steering.State == SteeringState.Faulted) return Loc.Format("S.Set.SystemModeFault", steering.Reason ?? "");
            return steering.UnavailableReason switch
            {
                "driver-missing" => Loc.Get("S.Set.SystemModeNoDriver"),
                "not-admin" => Loc.Get("S.Set.SystemModeNoAdmin"),
                _ => Loc.Get("S.Set.SystemModeReady"),
            };
        }
    }

    public bool SystemModeProblem => !_services.Steering.IsAvailable || _services.Steering.State == SteeringState.Faulted;

    public bool CloseToTray
    {
        get => _services.Settings.CloseToTray;
        set { _services.SaveSettings(_services.Settings with { CloseToTray = value }); Raise(); }
    }

    public bool Notifications
    {
        get => _services.Settings.Notifications;
        set { _services.SaveSettings(_services.Settings with { Notifications = value }); Raise(); }
    }

    public bool ClipboardWatch
    {
        get => _services.Settings.ClipboardWatch;
        set { _services.SaveSettings(_services.Settings with { ClipboardWatch = value }); Raise(); }
    }

    public IReadOnlyList<SpeedUnitOption> SpeedUnits { get; } = [new("Mbit/s", false), new("MB/s", true)];

    public SpeedUnitOption SelectedSpeedUnit
    {
        get => SpeedUnits.First(o => o.InBytes == _services.Settings.SpeedInBytes);
        set
        {
            if (value is null || value.InBytes == _services.Settings.SpeedInBytes) return;
            _services.SaveSettings(_services.Settings with { SpeedInBytes = value.InBytes });
            Raise();
        }
    }

    public bool TaskbarMeter
    {
        get => _services.Settings.TaskbarMeter;
        set { _services.SaveSettings(_services.Settings with { TaskbarMeter = value }); Raise(); }
    }

    public bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Arqon") is string;
        }
        set
        {
            if (_services.Demo) return;
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (value && Environment.ProcessPath is { } exe) key.SetValue("Arqon", $"\"{exe}\" --tray");
            else key.DeleteValue("Arqon", throwOnMissingValue: false);
            Raise();
        }
    }

    public bool NeedsFix => _services.Fix.NeedsFix;
    public string FixStatus => _fixMessage.Length > 0 ? _fixMessage
        : _services.Fix.NeedsFix ? Loc.Get("S.Set.FixNeeded") : Loc.Get("S.Set.FixOk");

    public bool IsAdmin => WinDivertSteerer.IsAdministrator();

    public bool CanRestartAsAdmin => !IsAdmin;
    public string AdminText => IsAdmin ? Loc.Get("S.Set.AdminYes") : Loc.Get("S.Set.AdminNo");
    public string Version => "Arqon " + AppInfo.Version;

    private void ApplyFix()
    {
        var answer = MessageBox.Show(Loc.Get("S.Fix.Confirm") + "\n\n" + _services.Fix.Describe(), "Arqon",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        try
        {
            _services.Fix.Apply();
            _fixMessage = Loc.Get("S.Fix.Done");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            _fixMessage = Loc.Get("S.Fix.NeedAdmin");
        }
        Raise(string.Empty);
    }

    private void RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = "--restarted" });
            ((App)Application.Current).ExitApp();
        }
        catch (Win32Exception)
        {
            // the user declined the UAC prompt: nothing to do
        }
    }
}
