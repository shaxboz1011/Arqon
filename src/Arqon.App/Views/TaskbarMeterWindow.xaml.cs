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
    private const double WidthDip = 104, HeightDip = 32, GapDip = 6, MinWidthDip = 74;
    private const int ThemeCheckEveryTicks = 5;
    private const int ScanEveryTicks = 3;

    private readonly TaskbarMeterViewModel _vm;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private IntPtr _handle;
    private int _ticks;
    private volatile IReadOnlyList<PixelRect> _taskbarItems = [];
    private int _scanning;
    private string _layout = "";

    private readonly Func<int> _getOffset;
    private readonly Action<int> _setOffset;
    private double _offsetDip;
    private bool _pressed, _dragging;
    private double _pressAt, _offsetAtPress;

    public TaskbarMeterWindow(TaskbarMeterViewModel vm, Action open, Action hide, Func<int> getOffset, Action<int> setOffset)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        _getOffset = getOffset;
        _setOffset = setOffset;
        _offsetDip = getOffset();

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
        // A click opens Arqon; pressing and dragging along the bar moves the meter, and the place is remembered.
        Root.MouseLeftButtonDown += (_, e) =>
        {
            _pressed = true;
            _dragging = false;
            _pressAt = Along(e);
            _offsetAtPress = _offsetDip;
            Root.CaptureMouse();
        };
        Root.MouseMove += (_, e) =>
        {
            if (!_pressed) return;
            var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var moved = Along(e) - _pressAt;
            if (!_dragging && Math.Abs(moved) < 4 * scale) return;
            _dragging = true;
            Details.IsOpen = false;
            _offsetDip = _offsetAtPress + moved / scale;
            Reposition();
        };
        Root.MouseLeftButtonUp += (_, _) =>
        {
            if (!_pressed) return;
            _pressed = false;
            Root.ReleaseMouseCapture();
            if (_dragging) _setOffset((int)Math.Round(_offsetDip));
            else open();
            _dragging = false;
        };
        ResetItem.Click += (_, _) => { _offsetDip = 0; _setOffset(0); Reposition(); };
        OpenItem.Click += (_, _) => open();
        HideItem.Click += (_, _) => hide();
        LayoutItem.Click += (_, _) => CopyLayout();
        Root.ContextMenuOpening += (_, _) => Details.IsOpen = false;
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Where the pointer is along the taskbar, in screen pixels (left to right, or top to bottom on a side taskbar).</summary>
    private double Along(MouseEventArgs e)
    {
        var screen = PointToScreen(e.GetPosition(this));
        return TaskbarNative.TryGetTaskbar(out var bar, out _) && bar.Width < bar.Height ? screen.Y : screen.X;
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

    /// <summary>
    /// Looks at what else is on the taskbar every few seconds, in the background (asking Explorer is slow). The meter steps aside
    /// for whatever sits next to the notification area, such as the weather widget of Windows 11.
    /// </summary>
    private void ScanTaskbarItems(PixelRect bar)
    {
        if (_ticks % ScanEveryTicks != 0 || Interlocked.Exchange(ref _scanning, 1) == 1) return;
        Task.Run(() =>
        {
            try { _taskbarItems = TaskbarNative.TaskbarItems(bar); }
            finally { Interlocked.Exchange(ref _scanning, 0); }
        });
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

        ScanTaskbarItems(bar);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var items = _taskbarItems;
        var spot = TaskbarPlacement.For(bar, tray, (int)Math.Round(WidthDip * scale), (int)Math.Round(HeightDip * scale),
            (int)Math.Round(GapDip * scale), items, (int)Math.Round(MinWidthDip * scale));
        _layout = DescribeLayout(bar, tray, scale, items, spot);
        if (spot is not { } rect)
        {
            Details.IsOpen = false;   // nowhere free on this taskbar: stay out of the way, the tray icon still opens Arqon
            if (IsVisible) Hide();
            return;
        }
        if (!IsVisible) Show();
        rect = TaskbarPlacement.Nudge(rect, bar, tray, (int)Math.Round(_offsetDip * scale), (int)Math.Round(GapDip * scale));
        TaskbarNative.Place(_handle, rect);   // every tick: it also lifts us back above the taskbar if that took the top spot
    }

    private static string DescribeLayout(PixelRect bar, PixelRect tray, double scale, IReadOnlyList<PixelRect> items, PixelRect? spot) =>
        string.Join(Environment.NewLine,
        [
            $"Arqon taskbar layout (Windows {Environment.OSVersion.Version}, scale {scale:0.##})",
            $"taskbar {bar}",
            $"tray {tray}",
            $"meter {(spot is { } r ? r.ToString() : "none: no free space")}",
            $"items ({items.Count}):",
            .. items.Take(80).Select(i => "  " + i),
        ]);

    /// <summary>Puts what the meter sees of the taskbar on the clipboard, so a wrong placement can be reported with facts.</summary>
    private void CopyLayout()
    {
        try { Clipboard.SetText(_layout); }
        catch (System.Runtime.InteropServices.COMException) { /* the clipboard is busy: try again from the menu */ }
    }
}
