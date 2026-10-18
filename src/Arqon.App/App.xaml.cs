using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Arqon.App.Infrastructure;
using Arqon.App.Services;
using Arqon.App.ViewModels;

namespace Arqon.App;

public partial class App : Application
{
    private const string InstanceName = @"Local\Arqon.SingleInstance";
    private const string ShowSignalName = @"Local\Arqon.Show";

    private AppServices? _services;
    private ShellViewModel? _shell;
    private TrayService? _tray;
    private Mutex? _instance;
    private EventWaitHandle? _showSignal;
    private bool _exiting;

    /// <summary>True when closing the window should only hide it to the tray.</summary>
    public bool ShouldHideOnClose => !_exiting && _tray is not null && _services is { Settings.CloseToTray: true };

    public TrayService? Tray => _tray;

    /// <summary>Really quits (the window's X only hides it when the tray is on).</summary>
    public void ExitApp()
    {
        _exiting = true;
        Shutdown();
    }

    public void ShowMainWindow()
    {
        if (MainWindow is not { } window) return;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            MessageBox.Show(args.Exception.Message, "Arqon", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var args = e.Args;
        var shotIndex = Array.IndexOf(args, "--screenshots");
        var shotDir = shotIndex >= 0 && shotIndex + 1 < args.Length ? args[shotIndex + 1] : null;
        var demo = args.Contains("--demo") || shotDir is not null;
        var startHidden = args.Contains("--tray");

        if (!demo && !AcquireSingleInstance(args.Contains("--restarted")))
        {
            Shutdown();
            return;
        }

        _services = AppServices.Create(demo);
        Loc.Apply(_services.Settings.Language);
        _services.Start();

        _shell = new ShellViewModel(_services);
        var window = new MainWindow(_shell);
        MainWindow = window;
        SessionEnding += (_, _) => _exiting = true;

        if (shotDir is null)
        {
            _tray = new TrayService(_services, ShowMainWindow, ExitApp);
            ListenForSecondInstance();
        }
        if (!(startHidden && _tray is not null && _services.Settings.CloseToTray)) window.Show();

        if (shotDir is not null) _ = TakeScreenshotsAsync(window, shotDir);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _shell?.Dispose();
        _services?.Dispose();
        _showSignal?.Dispose();
        if (_instance is not null)
        {
            try { _instance.ReleaseMutex(); } catch (ApplicationException) { /* not owned */ }
            _instance.Dispose();
        }
        base.OnExit(e);
    }

    /// <summary>One Arqon per user session. A second launch asks the first to show itself and exits.</summary>
    private bool AcquireSingleInstance(bool waitForPrevious)
    {
        try
        {
            _instance = new Mutex(true, InstanceName, out var created);
            if (created) return true;

            // Restarting as administrator: the old process is still shutting down, give it a moment.
            if (waitForPrevious && _instance.WaitOne(TimeSpan.FromSeconds(8))) return true;

            try { EventWaitHandle.OpenExisting(ShowSignalName).Set(); }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException) { }
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true; // the other instance runs elevated and we cannot see its objects; do not block the user
        }
    }

    private void ListenForSecondInstance()
    {
        try { _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName); }
        catch (UnauthorizedAccessException) { return; }

        var signal = _showSignal;
        var thread = new Thread(() =>
        {
            try
            {
                while (signal.WaitOne()) Dispatcher.BeginInvoke(ShowMainWindow);
            }
            catch (ObjectDisposedException) { /* app is exiting */ }
        }) { IsBackground = true, Name = "arqon-show-signal" };
        thread.Start();
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arqon");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"{DateTime.Now:O}\n{ex}\n\n");
        }
        catch (IOException) { }
    }

    /// <summary>Developer aid: renders every page (both languages) to PNG files, then exits.</summary>
    private async Task TakeScreenshotsAsync(MainWindow window, string dir)
    {
        Directory.CreateDirectory(dir);
        window.Height = 1000;   // tall enough that the whole dashboard fits in one picture
        window.Top = 10;
        await Task.Delay(6500);
        foreach (var lang in new[] { "uz", "en" })
        {
            Loc.Apply(lang);
            foreach (var page in new[] { "panel", "apps", "downloads", "usage", "settings" })
            {
                _shell!.Navigate(page);
                await Task.Delay(900);
                window.UpdateLayout();
                var dpi = VisualTreeHelper.GetDpi(window);
                var bmp = new RenderTargetBitmap((int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY),
                    96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                bmp.Render(window);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                await using var fs = File.Create(Path.Combine(dir, $"{lang}-{page}.png"));
                enc.Save(fs);
            }
        }
        Shutdown();
    }
}
