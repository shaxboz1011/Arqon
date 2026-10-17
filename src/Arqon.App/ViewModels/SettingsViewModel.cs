using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using Arqon.App.Infrastructure;
using Arqon.App.Services;
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

    /// <summary>System-wide steering needs the packet driver and administrator rights; it stays unavailable until both are present.</summary>
    public bool SystemModeAvailable => false;

    public bool SystemMode
    {
        get => _services.Settings.SystemMode && SystemModeAvailable;
        set
        {
            if (!SystemModeAvailable) return;
            _services.SaveSettings(_services.Settings with { SystemMode = value });
            Raise();
        }
    }

    public string SystemModeNote => SystemModeAvailable ? Loc.Get("S.Set.SystemModeReady") : Loc.Get("S.Set.SystemModeWip");

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
            if (value && Environment.ProcessPath is { } exe) key.SetValue("Arqon", $"\"{exe}\"");
            else key.DeleteValue("Arqon", throwOnMissingValue: false);
            Raise();
        }
    }

    public bool NeedsFix => _services.Fix.NeedsFix;
    public string FixStatus => _fixMessage.Length > 0 ? _fixMessage
        : _services.Fix.NeedsFix ? Loc.Get("S.Set.FixNeeded") : Loc.Get("S.Set.FixOk");

    public bool IsAdmin
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

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
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Win32Exception)
        {
            // the user declined the UAC prompt: nothing to do
        }
    }
}
