using System.Windows;
using System.Windows.Threading;
using Arqon.App.ViewModels;

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

        StateChanged += (_, _) => AdjustForMaximize();
        Activated += (_, _) => _shell.OnWindowActivated();
        _summaryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _summaryTimer.Tick += (_, _) => _shell.Refresh();
        _summaryTimer.Start();
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
