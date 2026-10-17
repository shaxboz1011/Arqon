using System.Windows;
using System.Windows.Media;

namespace Arqon.App.Controls;

public sealed record BarPart(Color Color, double Value);

/// <summary>Progress bar split into coloured parts, one per link. Total 0 means "sum of the parts".</summary>
public sealed class SplitBar : FrameworkElement
{
    public static readonly DependencyProperty PartsProperty = DependencyProperty.Register(
        nameof(Parts), typeof(IReadOnlyList<BarPart>), typeof(SplitBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TotalProperty = DependencyProperty.Register(
        nameof(Total), typeof(double), typeof(SplitBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<BarPart>? Parts { get => (IReadOnlyList<BarPart>?)GetValue(PartsProperty); set => SetValue(PartsProperty, value); }
    public double Total { get => (double)GetValue(TotalProperty); set => SetValue(TotalProperty, value); }

    private static readonly Brush TrackBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x23, 0x2A, 0x3A)));

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width, double.IsNaN(Height) ? 8 : Height);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 2 || h < 2) return;

        var radius = h / 2;
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, w, h), radius, radius);

        var parts = Parts;
        if (parts is null || parts.Count == 0) return;
        var total = Total > 0 ? Total : parts.Sum(p => p.Value);
        if (total <= 0) return;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), radius, radius));
        double x = 0;
        foreach (var part in parts)
        {
            var width = Math.Min(w - x, w * part.Value / total);
            if (width <= 0) continue;
            dc.DrawRectangle(new SolidColorBrush(part.Color), null, new Rect(x, 0, width, h));
            x += width;
        }
        dc.Pop();
    }
}
