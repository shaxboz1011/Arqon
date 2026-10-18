using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Arqon.App.Infrastructure;
using Arqon.App.Services;
using Arqon.Core.Steering;
using Microsoft.Win32;

namespace Arqon.App.ViewModels;

public sealed record LanguageOption(string Label, string Code);

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
        _services.Steering.StateChanged += OnSteeringChanged;
    }

    private void OnSteeringChanged() =>
        Application.Current?.Dispatcher.BeginInvoke(() => Raise(string.Empty));

    public override void Dispose()
    {
        _services.Steering.StateChanged -= OnSteeringChanged;
        base.Dispose();
    }

    public RelayCommand FixCommand { get; }
    public RelayCommand RestartAsAdminCommand { get; }

    public IReadOnlyList<LanguageOption> Languages { get; } = [new("O'zbekcha", "uz"), new("English", "en")];

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

    public bool SystemMode
    {
        get => _services.Steering.State == SteeringState.Running;
        set
        {
            if (value == (_services.Steering.State == SteeringState.Running)) return;
            var on = value && _services.Steering.Start();
            if (!value) _services.Steering.Stop();
            _services.SaveSettings(_services.Settings with { SystemMode = on });
            Raise(string.Empty);
        }
    }

    public string SystemModeNote
    {
        get
        {
            var steering = _services.Steering;
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
    public string Version => "Arqon 0.1.0";

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
