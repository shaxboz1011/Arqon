using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Arqon.App.Controls;

/// <summary>Stacked column chart for per-day traffic: one column per label, one coloured segment per series. Values are bytes.</summary>
public sealed class BarChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<ChartSeries>), typeof(BarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
        nameof(Labels), typeof(IReadOnlyList<string>), typeof(BarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TipsProperty = DependencyProperty.Register(
        nameof(Tips), typeof(IReadOnlyList<string>), typeof(BarChart),
        new FrameworkPropertyMetadata(null));

    public IReadOnlyList<ChartSeries>? Series { get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public IReadOnlyList<string>? Labels { get => (IReadOnlyList<string>?)GetValue(LabelsProperty); set => SetValue(LabelsProperty, value); }

    /// <summary>Hover text per column.</summary>
    public IReadOnlyList<string>? Tips { get => (IReadOnlyList<string>?)GetValue(TipsProperty); set => SetValue(TipsProperty, value); }

    private const double AxisWidth = 56, LabelHeight = 20, Top = 8;
    private static readonly Pen GridPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x24, 0x2B, 0x3B)), 1));
    private static readonly Brush MutedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0xA7)));
    private int _hover = -1;

    private static Pen Frozen(Pen p) { p.Freeze(); return p; }
    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    private int Columns => Math.Max(Labels?.Count ?? 0, Series?.Max(s => s.Values.Count) ?? 0);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < AxisWidth + 20 || h < LabelHeight + 30) return;

        var plot = new Rect(AxisWidth, Top, w - AxisWidth - 4, h - Top - LabelHeight);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var series = Series ?? [];
        var columns = Columns;

        double dataMax = 0;
        for (var i = 0; i < columns; i++) dataMax = Math.Max(dataMax, series.Sum(s => Value(s, i)));
        var max = NiceCeiling(dataMax);

        for (var g = 0; g <= 2; g++)
        {
            var y = plot.Top + plot.Height * g / 2.0;
            dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(g == 2 ? "0" : FormatBytes(max * (1 - g / 2.0)), 10, dpi);
            dc.DrawText(label, new Point(0, Math.Min(plot.Bottom - label.Height, Math.Max(0, y - label.Height / 2))));
        }

        if (columns == 0) return;
        var slot = plot.Width / columns;
        var barWidth = Math.Max(3, slot * 0.62);

        for (var i = 0; i < columns; i++)
        {
            var x = plot.Left + slot * i + (slot - barWidth) / 2;
            if (i == _hover) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)), null,
                new Rect(plot.Left + slot * i, plot.Top, slot, plot.Height), 4, 4);

            var bottom = plot.Bottom;
            var latest = i == columns - 1;
            foreach (var s in series)
            {
                var v = Value(s, i);
                if (v <= 0) continue;
                var height = Math.Max(1.5, plot.Height * Math.Min(1, v / max));
                var color = s.Color;
                var brush = new SolidColorBrush(Color.FromArgb(latest ? (byte)255 : (byte)205, color.R, color.G, color.B));
                dc.DrawRoundedRectangle(brush, null, new Rect(x, bottom - height, barWidth, height), 2, 2);
                bottom -= height;
            }

            if (Labels is { } labels && i < labels.Count && (columns <= 16 || i % 2 == columns % 2 || latest))
            {
                var t = Text(labels[i], 10, dpi, latest ? Brushes.White : MutedBrush);
                dc.DrawText(t, new Point(plot.Left + slot * i + (slot - t.Width) / 2, plot.Bottom + 4));
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var columns = Columns;
        if (columns == 0) return;

        var slot = (ActualWidth - AxisWidth - 4) / columns;
        var index = (int)Math.Floor((e.GetPosition(this).X - AxisWidth) / slot);
        var valid = index >= 0 && index < columns ? index : -1;
        if (valid == _hover) return;

        _hover = valid;
        ToolTip = valid >= 0 && Tips is { } tips && valid < tips.Count ? tips[valid] : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover < 0) return;
        _hover = -1;
        ToolTip = null;
        InvalidateVisual();
    }

    private static double Value(ChartSeries s, int index) => index < s.Values.Count ? Math.Max(0, s.Values[index]) : 0;

    private static FormattedText Text(string text, double size, double dpi, Brush? brush = null) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush ?? MutedBrush, dpi);

    private static double NiceCeiling(double v)
    {
        if (v <= 0) return 1_000_000;
        var pow = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (var step in new[] { 1.0, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0 })
            if (v <= step * pow) return step * pow;
        return 10 * pow;
    }

    /// <summary>Decimal units (1 GB = 1000 MB), because the axis ceilings are decimal round numbers.</summary>
    public static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        while (bytes >= 1000 && i < units.Length - 1) { bytes /= 1000; i++; }
        return bytes.ToString(bytes >= 100 || i == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + " " + units[i];
    }
}
