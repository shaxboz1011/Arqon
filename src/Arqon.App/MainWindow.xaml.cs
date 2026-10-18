using System.Windows;
using System.Windows.Threading;
using Arqon.App.ViewModels;
using Arqon.Core.Download;

namespace Arqon.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly DispatcherTimer _summaryTimer;

    public MainWindow(ShellViewModel shell)
    {
        _shell = shell;
        DataContext = shell;
        InitializeComponent();

        AllowDrop = true;   // drop .torrent files, magnet links or web links on the window
        DragOver += OnDragOver;
        Drop += OnDrop;

        StateChanged += (_, _) => AdjustForMaximize();
        Activated += (_, _) => _shell.OnWindowActivated();
        _summaryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _summaryTimer.Tick += (_, _) => _shell.Refresh();
        _summaryTimer.Start();
    }

    private static bool Wanted(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Wanted(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var dropped = new List<string>();
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) dropped.AddRange(files);
        else if (e.Data.GetData(DataFormats.UnicodeText) is string text) dropped.AddRange(text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

        // Only what Arqon can download is taken: .torrent files, magnet links, web links. Other files are ignored.
        foreach (var item in ExternalRequests.FromArguments(dropped)) _shell.AddDownload(item);
        e.Handled = true;
    }

    // A borderless maximized window extends past the screen edge by the resize border width.
    private void AdjustForMaximize()
    {
        Root.Padding = WindowState == WindowState.Maximized
            ? new Thickness(SystemParameters.WindowResizeBorderThickness.Left + 2)
            : new Thickness(0);
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (Application.Current is App { ShouldHideOnClose: true } app)
        {
            e.Cancel = true;
            Hide();
            app.Tray?.ShowHiddenHint();
        }
        base.OnClosing(e);
    }
}
