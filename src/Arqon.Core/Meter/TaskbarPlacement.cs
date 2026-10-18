namespace Arqon.Core.Meter;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>Where the taskbar meter goes: in the taskbar, right next to the notification area.</summary>
public static class TaskbarPlacement
{
    /// <param name="taskbar">The taskbar's rectangle in screen pixels.</param>
    /// <param name="tray">The notification area (icons and clock) inside it.</param>
    public static PixelRect For(PixelRect taskbar, PixelRect tray, int width, int height, int gap)
    {
        if (taskbar.Width >= taskbar.Height)
        {
            // Bottom or top taskbar: left of the notification area, centred vertically.
            height = Math.Min(height, taskbar.Height);
            var top = taskbar.Top + (taskbar.Height - height) / 2;
            var left = Math.Max(taskbar.Left, tray.Left - gap - width);
            return new PixelRect(left, top, left + width, top + height);
        }

        // Left or right taskbar: above the notification area, centred horizontally.
        width = Math.Min(width, taskbar.Width);
        var x = taskbar.Left + (taskbar.Width - width) / 2;
        var y = Math.Max(taskbar.Top, tray.Top - gap - height);
        return new PixelRect(x, y, x + width, y + height);
    }
}
