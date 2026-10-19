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
    /// <param name="occupied">
    /// Other things on the taskbar (buttons, the weather widget of Windows 11). Whatever sits right next to the notification
    /// area, one thing touching the next, is stepped over, so the meter never covers it. Things further away are ignored.
    /// </param>
    public static PixelRect For(PixelRect taskbar, PixelRect tray, int width, int height, int gap, IReadOnlyList<PixelRect>? occupied = null)
    {
        var touching = gap + 16;   // two things closer than this count as touching

        if (taskbar.Width >= taskbar.Height)
        {
            // Bottom or top taskbar: left of the notification area, centred vertically.
            height = Math.Min(height, taskbar.Height);
            var top = taskbar.Top + (taskbar.Height - height) / 2;
            var edge = tray.Left;
            var row = (occupied ?? []).Where(r => r.Width > 0 && r.Top < taskbar.Bottom && r.Bottom > taskbar.Top && r.Left < tray.Left).ToList();
            for (var moved = true; moved;)
            {
                moved = false;
                foreach (var r in row)
                {
                    if (r.Right >= edge - touching && r.Left < edge) { edge = r.Left; moved = true; }
                }
            }
            var left = Math.Max(taskbar.Left, edge - gap - width);
            return new PixelRect(left, top, left + width, top + height);
        }

        // Left or right taskbar: above the notification area, centred horizontally.
        width = Math.Min(width, taskbar.Width);
        var x = taskbar.Left + (taskbar.Width - width) / 2;
        var topEdge = tray.Top;
        var column = (occupied ?? []).Where(r => r.Height > 0 && r.Left < taskbar.Right && r.Right > taskbar.Left && r.Top < tray.Top).ToList();
        for (var moved = true; moved;)
        {
            moved = false;
            foreach (var r in column)
            {
                if (r.Bottom >= topEdge - touching && r.Top < topEdge) { topEdge = r.Top; moved = true; }
            }
        }
        var y = Math.Max(taskbar.Top, topEdge - gap - height);
        return new PixelRect(x, y, x + width, y + height);
    }
}
