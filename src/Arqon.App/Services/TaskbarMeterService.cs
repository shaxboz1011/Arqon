using System.Windows;
using Arqon.App.ViewModels;
using Arqon.App.Views;

namespace Arqon.App.Services;

/// <summary>Shows or hides the taskbar meter as the setting changes.</summary>
public sealed class TaskbarMeterService : IDisposable
{
    private readonly AppServices _services;
    private readonly Action _open;
    private TaskbarMeterWindow? _window;

    public TaskbarMeterService(AppServices services, Action open)
    {
        _services = services;
        _open = open;
        services.SettingsChanged += Apply;
        Apply();
    }

    private void Apply()
    {
        var wanted = _services.Settings.TaskbarMeter;
        if (wanted && _window is null)
        {
            _window = new TaskbarMeterWindow(new TaskbarMeterViewModel(_services), _open,
                hide: () => _services.SaveSettings(_services.Settings with { TaskbarMeter = false }));
            _window.Show();
        }
        else if (!wanted && _window is not null)
        {
            _window.Close();
            _window = null;
        }
    }

    public void Dispose()
    {
        _services.SettingsChanged -= Apply;
        _window?.Close();
        _window = null;
    }
}
