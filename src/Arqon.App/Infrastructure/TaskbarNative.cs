using System.IO;
using System.Runtime.InteropServices;
using Arqon.Core.Meter;
using Microsoft.Win32;

namespace Arqon.App.Infrastructure;

/// <summary>The few Win32 calls the taskbar meter needs: find the taskbar, place a window on it, detect full-screen apps.</summary>
internal static class TaskbarNative
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x10, SwpShowWindow = 0x40;
    private static readonly IntPtr HwndTopmost = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? title);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    /// <summary>The primary taskbar and its notification area, in screen pixels. False while Explorer is not running.</summary>
    public static bool TryGetTaskbar(out PixelRect taskbar, out PixelRect tray)
    {
        taskbar = tray = default;
        var bar = FindWindow("Shell_TrayWnd", null);
        if (bar == IntPtr.Zero || !GetWindowRect(bar, out var b)) return false;
        taskbar = new PixelRect(b.Left, b.Top, b.Right, b.Bottom);

        var notify = FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);
        tray = notify != IntPtr.Zero && GetWindowRect(notify, out var t)
            ? new PixelRect(t.Left, t.Top, t.Right, t.Bottom)
            : new PixelRect(b.Right - (b.Right - b.Left) / 6, b.Top, b.Right, b.Bottom);   // unusual shell: assume the right sixth
        return true;
    }

    public static bool FullscreenAppActive() =>
        SHQueryUserNotificationState(out var state) == 0 && FullscreenState.ShouldHideWidget(state);

    /// <summary>Keeps the window out of Alt+Tab and stops it from ever taking the keyboard focus.</summary>
    public static void MakeToolWindow(IntPtr window) =>
        SetWindowLong(window, GwlExStyle, GetWindowLong(window, GwlExStyle) | WsExToolWindow | WsExNoActivate);

    /// <summary>Moves the window and lifts it above the taskbar, which is itself topmost.</summary>
    public static void Place(IntPtr window, PixelRect r) =>
        SetWindowPos(window, HwndTopmost, r.Left, r.Top, r.Width, r.Height, SwpNoActivate | SwpShowWindow);

    /// <summary>True when the taskbar uses the light theme (dark text is then needed on top of it).</summary>
    public static bool TaskbarIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
