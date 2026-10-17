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
    private AppServices? _services;
    private ShellViewModel? _shell;

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

        _services = AppServices.Create(demo);
        Loc.Apply(_services.Settings.Language);
        _services.Monitor.Start();

        _shell = new ShellViewModel(_services);
        var window = new MainWindow(_shell);
        MainWindow = window;
        window.Show();

        if (shotDir is not null) _ = TakeScreenshotsAsync(window, shotDir);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Monitor.Stop();
        _shell?.Dispose();
        base.OnExit(e);
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
        await Task.Delay(6500);
        foreach (var lang in new[] { "uz", "en" })
        {
            Loc.Apply(lang);
            foreach (var page in new[] { "panel", "apps", "downloads", "settings" })
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
