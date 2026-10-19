namespace Arqon.Core.Meter;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>Where the taskbar meter goes: in the taskbar, as near to the notification area as it fits, and never on top of anything.</summary>
public static class TaskbarPlacement
{
    /// <param name="taskbar">The taskbar's rectangle in screen pixels.</param>
    /// <param name="tray">The notification area (icons and clock) inside it.</param>
    /// <param name="occupied">
    /// Other things on the taskbar: buttons, and on Windows 11 the weather widget. The meter goes into the free space nearest to the
    /// notification area that is big enough; it never covers any of them, however far from the notification area they are.
    /// </param>
    /// <param name="minWidth">
    /// The narrowest the meter may be made when a free space is smaller than <paramref name="width"/> (it then shrinks to fit; on a
    /// vertical taskbar this applies to the height). Defaults to the full size: no shrinking.
    /// </param>
    /// <returns>The rectangle for the meter, or null when nowhere is free (it is then hidden rather than forced on top of something).</returns>
    public static PixelRect? For(PixelRect taskbar, PixelRect tray, int width, int height, int gap,
        IReadOnlyList<PixelRect>? occupied = null, int? minWidth = null)
    {
        var items = occupied ?? [];

        if (taskbar.Width >= taskbar.Height)
        {
            // Bottom or top taskbar: left of the notification area, centred vertically.
            height = Math.Min(height, taskbar.Height);
            var top = taskbar.Top + (taskbar.Height - height) / 2;
            var blocked = items
                .Where(r => r.Width > 0 && r.Top < taskbar.Bottom && r.Bottom > taskbar.Top && r.Left < tray.Left)
                .Select(r => (r.Left, r.Right));
            var spot = Fit(taskbar.Left, tray.Left, blocked, width, gap, Math.Min(minWidth ?? width, width));
            return spot is { } s ? new PixelRect(s.Start, top, s.Start + s.Length, top + height) : null;
        }

        // Left or right taskbar: above the notification area, centred horizontally.
        width = Math.Min(width, taskbar.Width);
        var x = taskbar.Left + (taskbar.Width - width) / 2;
        var column = items
            .Where(r => r.Height > 0 && r.Left < taskbar.Right && r.Right > taskbar.Left && r.Top < tray.Top)
            .Select(r => (r.Top, r.Bottom));
        var place = Fit(taskbar.Top, tray.Top, column, height, gap, Math.Min(Math.Max(16, height * 3 / 4), height));
        return place is { } p ? new PixelRect(x, p.Start, x + width, p.Start + p.Length) : null;
    }

    /// <summary>
    /// One-dimensional core: the free space runs from <paramref name="low"/> to <paramref name="high"/> (the notification area's edge),
    /// with <paramref name="blocked"/> spans inside it. Walks the free stretches from the high end down and takes the first one that
    /// has room for at least <paramref name="smallest"/>, keeping <paramref name="gap"/> away from whatever bounds it.
    /// </summary>
    private static (int Start, int Length)? Fit(int low, int high, IEnumerable<(int Start, int End)> blocked, int length, int gap, int smallest)
    {
        var spans = new List<(int Start, int End)>();
        foreach (var (start, end) in blocked.Select(b => (Math.Max(b.Start, low), Math.Min(b.End, high))).Where(b => b.Item2 > b.Item1).OrderBy(b => b.Item1))
        {
            if (spans.Count > 0 && start <= spans[^1].End) spans[^1] = (spans[^1].Start, Math.Max(spans[^1].End, end));
            else spans.Add((start, end));
        }

        var edge = high;
        for (var i = spans.Count - 1; i >= -1; i--)
        {
            var upper = edge - gap;
            var lower = i >= 0 ? spans[i].End + gap : low;   // the bar's own end needs no gap
            var room = upper - lower;
            if (room > 0 && room >= smallest)
            {
                var fit = Math.Min(length, room);
                return (upper - fit, fit);
            }
            if (i >= 0) edge = spans[i].Start;
        }
        return null;
    }
}
