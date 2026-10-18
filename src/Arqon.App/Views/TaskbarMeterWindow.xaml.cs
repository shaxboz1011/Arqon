using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Arqon.App.Infrastructure;
using Arqon.App.ViewModels;
using Arqon.Core.Meter;

namespace Arqon.App.Views;

/// <summary>
/// A small window that sits in the taskbar next to the notification area and shows the live speed. It is an ordinary
/// topmost window placed by hand (a DeskBand would need a COM shell extension and does not exist on Windows 11).
/// </summary>
public partial class TaskbarMeterWindow : Window
{
    private const double WidthDip = 104, HeightDip = 32, GapDip = 6;
    private const int ThemeCheckEveryTicks = 5;

    private readonly TaskbarMeterViewModel _vm;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private IntPtr _handle;
    private int _ticks;

    public TaskbarMeterWindow(TaskbarMeterViewModel vm, Action open, Action hide)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            TaskbarNative.MakeToolWindow(_handle);
        };
        Loaded += (_, _) => Tick();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        Root.MouseEnter += (_, _) => { _vm.RefreshDetails(); Details.IsOpen = true; };
        Root.MouseLeave += (_, _) => Details.IsOpen = false;
        Root.MouseLeftButtonUp += (_, _) => open();
        OpenItem.Click += (_, _) => open();
        HideItem.Click += (_, _) => hide();
        Root.ContextMenuOpening += (_, _) => Details.IsOpen = false;
        Closed += (_, _) => _timer.Stop();
    }

    private void Tick()
    {
        _vm.RefreshSpeed();
        if (Details.IsOpen) _vm.RefreshDetails();
        if (_ticks++ % ThemeCheckEveryTicks == 0) ApplyTheme();
        Reposition();
    }

    private void ApplyTheme()
    {
        var light = TaskbarNative.TaskbarIsLight();
        Root.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,
            new SolidColorBrush(light ? Color.FromRgb(0x1B, 0x1F, 0x2A) : Color.FromRgb(0xF2, 0xF4, 0xFA)));
    }

    /// <summary>Follows the taskbar (Explorer restarts, resolution or DPI changes) and steps aside for full-screen apps.</summary>
    private void Reposition()
    {
        if (_handle == IntPtr.Zero) return;

        PixelRect bar = default, tray = default;
        var found = !TaskbarNative.FullscreenAppActive() && TaskbarNative.TryGetTaskbar(out bar, out tray);
        if (!found || Math.Min(bar.Width, bar.Height) < 16)   // full screen, Explorer not running, or an auto-hidden taskbar
        {
            Details.IsOpen = false;
            if (IsVisible) Hide();
            return;
        }

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var rect = TaskbarPlacement.For(bar, tray, (int)Math.Round(WidthDip * scale), (int)Math.Round(HeightDip * scale),
            (int)Math.Round(GapDip * scale));
        if (!IsVisible) Show();
        TaskbarNative.Place(_handle, rect);   // every tick: it also lifts us back above the taskbar if that took the top spot
    }
}
