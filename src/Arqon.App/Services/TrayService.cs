using System.IO;
using System.Windows;
using Arqon.App.Infrastructure;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Arqon.App.Services;

/// <summary>Tray icon with an Open/Exit menu, and the place where <see cref="Notice"/>s become balloon notifications.</summary>
public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly AppServices _services;
    private bool _hintShown;
    private readonly System.Windows.Threading.DispatcherTimer _speedTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public TrayService(AppServices services, Action open, Action exit)
    {
        _services = services;

        var menu = new WinForms.ContextMenuStrip();
        var openItem = menu.Items.Add(Loc.Get("S.Tray.Open"), null, (_, _) => open());
        openItem.Font = new Drawing.Font(openItem.Font, Drawing.FontStyle.Bold);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Loc.Get("S.Tray.Exit"), null, (_, _) => exit());
        Loc.Changed += () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            openItem.Text = Loc.Get("S.Tray.Open");
            menu.Items[^1].Text = Loc.Get("S.Tray.Exit");
        });

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Arqon",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) open(); };

        services.NoticeRaised += OnNotice;

        // hovering the tray icon shows the speed, so it can be read without opening the window
        _speedTimer.Tick += (_, _) => ShowSpeed();
        _speedTimer.Start();
        ShowSpeed();
    }

    private void ShowSpeed()
    {
        var (down, up) = _services.CurrentSpeed();
        // the tray tip is limited to 63 characters
        var text = $"Arqon  ↓ {Units.Speed(down)}  ↑ {Units.Speed(up)} {Units.SpeedLabel}";
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    /// <summary>The first time the window is hidden, say where Arqon went.</summary>
    public void ShowHiddenHint()
    {
        if (_hintShown) return;
        _hintShown = true;
        if (_services.Settings.Notifications) _icon.ShowBalloonTip(4000, "Arqon", Loc.Get("S.Tray.Tip"), WinForms.ToolTipIcon.Info);
    }

    private void OnNotice(Notice notice) =>
        Application.Current?.Dispatcher.BeginInvoke(() => _icon.ShowBalloonTip(5000, notice.Title, notice.Message, WinForms.ToolTipIcon.Info));

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && Drawing.Icon.ExtractAssociatedIcon(exe) is { } icon) return icon;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException) { /* fall through to the stock icon */ }
        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _speedTimer.Stop();
        _services.NoticeRaised -= OnNotice;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
