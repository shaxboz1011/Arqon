using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Arqon.App.Controls;

public sealed record ChartSeries(string Name, Color Color, IReadOnlyList<double> Values);

/// <summary>Lightweight live line chart: right-aligned time axis, auto-scaled Y, soft area fill. No third-party libraries.</summary>
public sealed class LineChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<ChartSeries>), typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxPointsProperty = DependencyProperty.Register(
        nameof(MaxPoints), typeof(int), typeof(LineChart),
        new FrameworkPropertyMetadata(60, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(LineChart),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowAxisProperty = DependencyProperty.Register(
        nameof(ShowAxis), typeof(bool), typeof(LineChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StackedProperty = DependencyProperty.Register(
        nameof(Stacked), typeof(bool), typeof(LineChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ChartSeries>? Series { get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public int MaxPoints { get => (int)GetValue(MaxPointsProperty); set => SetValue(MaxPointsProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public bool ShowAxis { get => (bool)GetValue(ShowAxisProperty); set => SetValue(ShowAxisProperty, value); }
    public bool Stacked { get => (bool)GetValue(StackedProperty); set => SetValue(StackedProperty, value); }

    private static readonly Pen GridPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x24, 0x2B, 0x3B)), 1));

    private static Pen Frozen(Pen p)
    {
        p.Freeze();
        return p;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 4 || h < 4) return;

        var series = Series ?? [];
        var labelWidth = ShowAxis ? 44.0 : 0.0;
        var plot = new Rect(labelWidth, 6, Math.Max(1, w - labelWidth - 2), Math.Max(1, h - 12));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var points = Math.Max(2, MaxPoints);
        double dataMax = 0;
        for (var i = 0; i < points; i++)
        {
            double sum = 0;
            foreach (var s in series)
            {
                var v = ValueAt(s, i, points);
                if (Stacked) sum += v; else dataMax = Math.Max(dataMax, v);
            }
            if (Stacked) dataMax = Math.Max(dataMax, sum);
        }
        var max = NiceCeiling(dataMax);

        for (var g = 0; g <= 2; g++)
        {
            var y = plot.Top + plot.Height * g / 2.0;
            dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            if (ShowAxis)
            {
                var value = max * (1 - g / 2.0);
                var text = new FormattedText(Format(value), CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0xA7)), dpi);
                text.MaxTextWidth = labelWidth - 4;
                text.MaxLineCount = 1;
                dc.DrawText(text, new Point(0, Math.Min(plot.Bottom - text.Height, Math.Max(0, y - text.Height / 2))));
            }
        }

        if (series.Count == 0) return;

        // Only draw where there is data: a chart that has just started should not look like speed was zero before.
        var available = Math.Min(points, series.Max(s => s.Values.Count));
        var first = points - available;
        if (available < 2) return;

        var baseline = new double[points];
        foreach (var s in series)
        {
            var top = new Point[points];
            var bottom = new Point[points];
            for (var i = first; i < points; i++)
            {
                var v = ValueAt(s, i, points);
                var lo = Stacked ? baseline[i] : 0;
                var hi = lo + v;
                var x = plot.Left + plot.Width * i / (points - 1);
                top[i] = new Point(x, plot.Bottom - plot.Height * Math.Min(1, hi / max));
                bottom[i] = new Point(x, plot.Bottom - plot.Height * Math.Min(1, lo / max));
                if (Stacked) baseline[i] = hi;
            }

            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(bottom[first], true, true);
                for (var i = first; i < points; i++) ctx.LineTo(top[i], true, false);
                for (var i = points - 1; i >= first; i--) ctx.LineTo(bottom[i], true, false);
            }
            area.Freeze();
            var fill = new LinearGradientBrush(
                Color.FromArgb(Stacked ? (byte)110 : (byte)70, s.Color.R, s.Color.G, s.Color.B),
                Color.FromArgb(0, s.Color.R, s.Color.G, s.Color.B), 90);
            dc.DrawGeometry(fill, null, area);

            var line = new StreamGeometry();
            using (var ctx = line.Open())
            {
                ctx.BeginFigure(top[first], false, false);
                for (var i = first + 1; i < points; i++) ctx.LineTo(top[i], true, true);
            }
            line.Freeze();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(s.Color), 1.8) { LineJoin = PenLineJoin.Round }, line);
        }
    }

    /// <summary>Series are right-aligned: the newest value is always at the right edge, missing history reads as 0.</summary>
    private static double ValueAt(ChartSeries s, int slot, int points)
    {
        var index = s.Values.Count - points + slot;
        return index >= 0 && index < s.Values.Count ? Math.Max(0, s.Values[index]) : 0;
    }

    private static double NiceCeiling(double v)
    {
        if (v <= 0) return 1;
        var pow = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (var step in new[] { 1.0, 2.0, 2.5, 5.0, 10.0 })
            if (v <= step * pow) return step * pow;
        return 10 * pow;
    }

    private static string Format(double v) =>
        v >= 100 ? v.ToString("0", CultureInfo.InvariantCulture) : v.ToString("0.#", CultureInfo.InvariantCulture);
}
