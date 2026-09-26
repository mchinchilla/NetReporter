using System.Globalization;
using NetReporter.Core.Elements;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Layout;

/// <summary>
/// Convierte un <see cref="ChartElement"/> en primitivas del <see cref="RenderList"/>:
/// rectángulos (barras, fondo, muestras de leyenda), líneas (grid y ejes), paths (líneas, áreas,
/// porciones) y textos (título, ejes, valores, leyenda). Todo absoluto, igual que el resto del layout.
/// </summary>
internal sealed class ChartLayout
{
    private static readonly Color GridColor = Color.FromHex("#E5E7EB");
    private static readonly Color AxisColor = Color.FromHex("#94A3B8");
    private static readonly Color MutedText = Color.FromHex("#475569");

    private readonly ChartElement _chart;
    private readonly ResolvedStyle _style;
    private readonly ITextMeasurer _measurer;
    private readonly CultureInfo _culture;
    private readonly List<RenderCommand> _out;
    private readonly string? _path;

    private ChartLayout(ChartElement chart, ResolvedStyle style, ITextMeasurer measurer, CultureInfo culture,
        List<RenderCommand> output)
    {
        _chart = chart;
        _style = style;
        _measurer = measurer;
        _culture = culture;
        _out = output;
        _path = chart.SourcePath;
    }

    public static void Emit(ChartElement chart, Rect bounds, ResolvedStyle style, ITextMeasurer measurer,
        CultureInfo culture, List<RenderCommand> output)
    {
        if (bounds.Width <= 4 || bounds.Height <= 4) return;
        new ChartLayout(chart, style, measurer, culture, output).Emit(bounds);
    }

    private bool IsRadial => _chart.Kind is ChartKind.Pie or ChartKind.Donut;

    private ChartLegendPosition LegendPosition =>
        _chart.Legend ?? (IsRadial || _chart.Series.Count > 1 ? ChartLegendPosition.Bottom : ChartLegendPosition.None);

    private void Emit(Rect bounds)
    {
        if (_chart.Background is not null || _chart.Border is not null)
            Add(new DrawRectangleCommand(bounds, _chart.Background, _chart.Border));

        var area = Inset(bounds, 6);
        var fontSize = _style.FontSize;

        if (!string.IsNullOrWhiteSpace(_chart.Title))
        {
            var titleStyle = _style with
            {
                FontSize = fontSize * 1.2,
                Weight = FontWeight.Bold,
                TextAlign = TextAlignment.Center,
                Background = Color.Transparent,
                Padding = Thickness.Zero
            };
            var titleH = _measurer.LineHeight(titleStyle) + 4;
            Add(new DrawTextCommand(new Rect(area.X, area.Y, area.Width, titleH), _chart.Title!, titleStyle));
            area = new Rect(area.X, area.Y + titleH, area.Width, area.Height - titleH);
        }

        var legendItems = BuildLegendItems();
        area = LayoutLegend(area, legendItems);
        if (area.Width <= 10 || area.Height <= 10) return;

        switch (_chart.Kind)
        {
            case ChartKind.Pie:
            case ChartKind.Donut:
                EmitRadial(area);
                break;
            case ChartKind.HorizontalBar:
                EmitHorizontalBars(area);
                break;
            default:
                EmitCartesian(area);
                break;
        }
    }

    // === Leyenda ===

    private List<(string Label, Color Color)> BuildLegendItems()
    {
        if (IsRadial)
            return _chart.Categories.Select((c, i) => (c, _chart.ColorAt(i))).ToList();
        return _chart.Series.Select((s, i) => (s.Name, SeriesColor(i))).ToList();
    }

    private Rect LayoutLegend(Rect area, List<(string Label, Color Color)> items)
    {
        var position = LegendPosition;
        if (position == ChartLegendPosition.None || items.Count == 0) return area;

        var style = LabelStyle(TextAlignment.Left, MutedText);
        var lineH = _measurer.LineHeight(style);
        var swatch = Math.Max(6, style.FontSize * 0.8);
        const double gap = 4, itemGap = 12;

        if (position == ChartLegendPosition.Right)
        {
            var width = Math.Min(area.Width * 0.4,
                items.Max(i => _measurer.MeasureWidth(i.Label, style)) + swatch + gap + 6);
            var x = area.Right - width;
            var totalH = items.Count * (lineH + 2);
            var y = area.Y + Math.Max(0, (area.Height - totalH) / 2);
            foreach (var (label, color) in items)
            {
                Add(new DrawRectangleCommand(new Rect(x, y + (lineH - swatch) / 2, swatch, swatch), color, null, 1.5));
                Add(new DrawTextCommand(new Rect(x + swatch + gap, y, width - swatch - gap, lineH), label, style));
                y += lineH + 2;
            }
            return new Rect(area.X, area.Y, area.Width - width - 8, area.Height);
        }

        // Top / Bottom: una o varias filas centradas.
        var rows = new List<List<(string Label, Color Color, double Width)>> { new() };
        double rowWidth = 0;
        foreach (var (label, color) in items)
        {
            var w = swatch + gap + _measurer.MeasureWidth(label, style);
            if (rows[^1].Count > 0 && rowWidth + itemGap + w > area.Width)
            {
                rows.Add(new());
                rowWidth = 0;
            }
            rowWidth += (rows[^1].Count > 0 ? itemGap : 0) + w;
            rows[^1].Add((label, color, w));
        }

        var blockH = rows.Count * (lineH + 2) + 4;
        var top = position == ChartLegendPosition.Top ? area.Y : area.Bottom - blockH + 4;
        foreach (var row in rows)
        {
            var total = row.Sum(r => r.Width) + itemGap * (row.Count - 1);
            var x = area.X + Math.Max(0, (area.Width - total) / 2);
            foreach (var (label, color, w) in row)
            {
                Add(new DrawRectangleCommand(new Rect(x, top + (lineH - swatch) / 2, swatch, swatch), color, null, 1.5));
                Add(new DrawTextCommand(new Rect(x + swatch + gap, top, w - swatch - gap + 2, lineH), label, style));
                x += w + itemGap;
            }
            top += lineH + 2;
        }

        return position == ChartLegendPosition.Top
            ? new Rect(area.X, area.Y + blockH, area.Width, area.Height - blockH)
            : new Rect(area.X, area.Y, area.Width, area.Height - blockH);
    }

    // === Escala ===

    private (double Min, double Max, double Step) ValueScale()
    {
        double lo = 0, hi = 0;
        var any = false;
        if (_chart.Stacked && _chart.Kind != ChartKind.Line)
        {
            for (var c = 0; c < _chart.Categories.Count; c++)
            {
                double pos = 0, neg = 0;
                foreach (var s in _chart.Series)
                {
                    var v = ValueAt(s, c);
                    if (v is null) continue;
                    any = true;
                    if (v > 0) pos += v.Value; else neg += v.Value;
                }
                hi = Math.Max(hi, pos);
                lo = Math.Min(lo, neg);
            }
        }
        else
        {
            foreach (var s in _chart.Series)
                foreach (var v in s.Values)
                {
                    if (v is null) continue;
                    any = true;
                    hi = Math.Max(hi, v.Value);
                    lo = Math.Min(lo, v.Value);
                }
        }

        if (_chart.AxisMin is { } forcedMin) lo = forcedMin;
        if (_chart.AxisMax is { } forcedMax) hi = forcedMax;
        if (!any && _chart.AxisMax is null) hi = 1;
        if (hi <= lo) hi = lo + 1;

        var step = NiceStep((hi - lo) / 5);
        var min = _chart.AxisMin ?? Math.Floor(lo / step) * step;
        var max = _chart.AxisMax ?? Math.Ceiling(hi / step) * step;
        if (max <= min) max = min + step;
        return (min, max, step);
    }

    private static double NiceStep(double raw)
    {
        if (raw <= 0 || double.IsNaN(raw)) return 1;
        var exp = Math.Floor(Math.Log10(raw));
        var fraction = raw / Math.Pow(10, exp);
        var nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 2.5 ? 2.5 : fraction <= 5 ? 5 : 10;
        return nice * Math.Pow(10, exp);
    }

    private static double? ValueAt(ChartSeries s, int index) =>
        index < s.Values.Count ? s.Values[index] : null;

    private Color SeriesColor(int index) => _chart.Series[index].Color ?? _chart.ColorAt(index);

    // === Cartesiano vertical (bar / line / area) ===

    private void EmitCartesian(Rect area)
    {
        var (min, max, step) = ValueScale();
        var tickStyle = LabelStyle(TextAlignment.Right, MutedText);
        var catStyle = LabelStyle(TextAlignment.Center, MutedText);
        var lineH = _measurer.LineHeight(tickStyle);

        var ticks = Ticks(min, max, step);
        var tickLabelW = ticks.Max(t => _measurer.MeasureWidth(Format(t), tickStyle));
        // Con etiquetas de valor sobre barras/puntos se reserva una línea extra arriba para no salir del chart.
        var topPad = _chart.ShowValues ? lineH + 2 : lineH / 2;
        var plot = new Rect(area.X + tickLabelW + 6, area.Y + topPad,
            area.Width - tickLabelW - 10, area.Height - topPad - lineH - 4);
        if (plot.Width <= 4 || plot.Height <= 4) return;

        double Y(double v) => plot.Bottom - (v - min) / (max - min) * plot.Height;

        foreach (var t in ticks)
        {
            var y = Y(t);
            if (_chart.ShowGrid)
                Add(new DrawLineCommand(new Point(plot.X, y), new Point(plot.Right, y), 0.5, GridColor));
            Add(new DrawTextCommand(new Rect(area.X, y - lineH / 2, tickLabelW, lineH), Format(t), tickStyle));
        }

        var baseline = Y(Math.Clamp(0, min, max));
        Add(new DrawLineCommand(new Point(plot.X, plot.Y), new Point(plot.X, plot.Bottom), 0.75, AxisColor));
        Add(new DrawLineCommand(new Point(plot.X, baseline), new Point(plot.Right, baseline), 0.75, AxisColor));

        var n = Math.Max(1, _chart.Categories.Count);
        var slot = plot.Width / n;
        EmitCategoryLabels(_chart.Categories, i => plot.X + slot * i, slot, plot.Bottom + 3, lineH, catStyle);

        if (_chart.Kind == ChartKind.Bar)
            EmitVerticalBars(plot, slot, baseline, Y);
        else
            EmitLines(plot, slot, baseline, Y, _chart.Kind == ChartKind.Area);
    }

    private void EmitVerticalBars(Rect plot, double slot, double baseline, Func<double, double> y)
    {
        var seriesCount = Math.Max(1, _chart.Series.Count);
        var groupW = slot * 0.72;
        var barW = _chart.Stacked ? groupW : groupW / seriesCount;
        var valueStyle = LabelStyle(TextAlignment.Center, _style.Foreground) with { FontSize = _style.FontSize * 0.85 };
        var valueH = _measurer.LineHeight(valueStyle);

        for (var c = 0; c < _chart.Categories.Count; c++)
        {
            var groupX = plot.X + slot * c + (slot - groupW) / 2;
            double pos = 0, neg = 0;
            for (var s = 0; s < _chart.Series.Count; s++)
            {
                if (ValueAt(_chart.Series[s], c) is not { } v) continue;
                double from, to;
                if (_chart.Stacked)
                {
                    from = v >= 0 ? pos : neg;
                    to = from + v;
                    if (v >= 0) pos = to; else neg = to;
                }
                else
                {
                    from = 0;
                    to = v;
                }
                var x = _chart.Stacked ? groupX : groupX + barW * s;
                var top = Math.Min(y(from), y(to));
                var height = Math.Abs(y(to) - y(from));
                Add(new DrawRectangleCommand(new Rect(x + 0.5, top, Math.Max(1, barW - 1), height), SeriesColor(s), null));

                if (_chart.ShowValues)
                {
                    var labelY = _chart.Stacked
                        ? top + (height - valueH) / 2
                        : v >= 0 ? top - valueH - 1 : top + height + 1;
                    var style = _chart.Stacked ? valueStyle with { Foreground = Color.White } : valueStyle;
                    if (!_chart.Stacked || height >= valueH)
                        Add(new DrawTextCommand(new Rect(x - 10, labelY, barW + 20, valueH), Format(v), style));
                }
            }
        }
        _ = baseline;
    }

    private void EmitLines(Rect plot, double slot, double baseline, Func<double, double> y, bool filled)
    {
        var valueStyle = LabelStyle(TextAlignment.Center, _style.Foreground) with { FontSize = _style.FontSize * 0.85 };
        var valueH = _measurer.LineHeight(valueStyle);
        var cumulative = new double[_chart.Categories.Count];

        for (var s = 0; s < _chart.Series.Count; s++)
        {
            var color = SeriesColor(s);
            // Segmentos continuos: un hueco (null) corta la línea.
            var segments = new List<List<(Point Point, double Value)>> { new() };
            var lowers = new List<List<Point>> { new() };
            for (var c = 0; c < _chart.Categories.Count; c++)
            {
                if (ValueAt(_chart.Series[s], c) is not { } v)
                {
                    if (segments[^1].Count > 0) { segments.Add(new()); lowers.Add(new()); }
                    continue;
                }
                var x = plot.X + slot * c + slot / 2;
                var stackedArea = filled && _chart.Stacked;
                var lowerValue = stackedArea ? cumulative[c] : 0;
                var upperValue = stackedArea ? cumulative[c] + v : v;
                if (stackedArea) cumulative[c] = upperValue;
                segments[^1].Add((new Point(x, y(upperValue)), v));
                lowers[^1].Add(new Point(x, stackedArea ? y(lowerValue) : baseline));
            }

            for (var i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (seg.Count == 0) continue;
                var points = seg.Select(p => p.Point).ToList();

                if (filled)
                {
                    var polygon = new List<Point>(points);
                    polygon.AddRange(Enumerable.Reverse(lowers[i]));
                    Add(DrawPathCommand.Create(polygon, closed: true, WithAlpha(color, 90), null));
                }

                if (points.Count > 1)
                    Add(DrawPathCommand.Create(points, closed: false, null, new BorderLine(_chart.LineWidth, color)));

                foreach (var (point, value) in seg)
                {
                    if (_chart.ShowMarkers)
                        Add(DrawPathCommand.Create(Circle(point, Math.Max(2, _chart.LineWidth * 1.4), 16),
                            closed: true, Color.White, new BorderLine(Math.Max(1, _chart.LineWidth * 0.75), color)));
                    if (_chart.ShowValues)
                        Add(new DrawTextCommand(new Rect(point.X - slot / 2, point.Y - valueH - 4, slot, valueH),
                            Format(value), valueStyle));
                }
            }
        }
    }

    // === Barras horizontales ===

    private void EmitHorizontalBars(Rect area)
    {
        var (min, max, step) = ValueScale();
        var catStyle = LabelStyle(TextAlignment.Right, MutedText);
        var tickStyle = LabelStyle(TextAlignment.Center, MutedText);
        var lineH = _measurer.LineHeight(catStyle);

        var catW = Math.Min(area.Width * 0.35,
            _chart.Categories.Count == 0 ? 0 : _chart.Categories.Max(c => _measurer.MeasureWidth(c, catStyle)));
        var ticks = Ticks(min, max, step);
        var lastTickW = _measurer.MeasureWidth(Format(ticks[^1]), tickStyle);
        // Con etiquetas de valor, la barra más larga necesita espacio a su derecha para su número.
        var labelStyle = LabelStyle(TextAlignment.Left, _style.Foreground) with { FontSize = _style.FontSize * 0.85 };
        var valueLabelW = _chart.ShowValues && !_chart.Stacked
            ? _chart.Series.SelectMany(s => s.Values).Where(v => v is not null)
                .Select(v => _measurer.MeasureWidth(Format(v!.Value), labelStyle) + 6).DefaultIfEmpty(0).Max()
            : 0;
        var rightPad = Math.Max(lastTickW / 2, valueLabelW);
        var plot = new Rect(area.X + catW + 6, area.Y, area.Width - catW - 6 - rightPad, area.Height - lineH - 4);
        if (plot.Width <= 4 || plot.Height <= 4) return;

        double X(double v) => plot.X + (v - min) / (max - min) * plot.Width;

        foreach (var t in ticks)
        {
            var x = X(t);
            if (_chart.ShowGrid)
                Add(new DrawLineCommand(new Point(x, plot.Y), new Point(x, plot.Bottom), 0.5, GridColor));
            var w = _measurer.MeasureWidth(Format(t), tickStyle) + 8;
            Add(new DrawTextCommand(new Rect(x - w / 2, plot.Bottom + 3, w, lineH), Format(t), tickStyle));
        }

        var baseline = X(Math.Clamp(0, min, max));
        Add(new DrawLineCommand(new Point(baseline, plot.Y), new Point(baseline, plot.Bottom), 0.75, AxisColor));
        Add(new DrawLineCommand(new Point(plot.X, plot.Bottom), new Point(plot.Right, plot.Bottom), 0.75, AxisColor));

        var n = Math.Max(1, _chart.Categories.Count);
        var slot = plot.Height / n;
        var seriesCount = Math.Max(1, _chart.Series.Count);
        var groupH = slot * 0.72;
        var barH = _chart.Stacked ? groupH : groupH / seriesCount;
        var valueStyle = LabelStyle(TextAlignment.Left, _style.Foreground) with { FontSize = _style.FontSize * 0.85 };
        var valueH = _measurer.LineHeight(valueStyle);

        for (var c = 0; c < _chart.Categories.Count; c++)
        {
            var slotTop = plot.Y + slot * c;
            Add(new DrawTextCommand(new Rect(area.X, slotTop + (slot - lineH) / 2, catW, lineH), _chart.Categories[c], catStyle));

            var groupY = slotTop + (slot - groupH) / 2;
            double pos = 0, neg = 0;
            for (var s = 0; s < _chart.Series.Count; s++)
            {
                if (ValueAt(_chart.Series[s], c) is not { } v) continue;
                double from, to;
                if (_chart.Stacked)
                {
                    from = v >= 0 ? pos : neg;
                    to = from + v;
                    if (v >= 0) pos = to; else neg = to;
                }
                else
                {
                    from = 0;
                    to = v;
                }
                var y = _chart.Stacked ? groupY : groupY + barH * s;
                var left = Math.Min(X(from), X(to));
                var width = Math.Abs(X(to) - X(from));
                Add(new DrawRectangleCommand(new Rect(left, y + 0.5, width, Math.Max(1, barH - 1)), SeriesColor(s), null));
                if (_chart.ShowValues && !_chart.Stacked)
                {
                    var text = Format(v);
                    var tw = _measurer.MeasureWidth(text, valueStyle) + 4;
                    Add(new DrawTextCommand(new Rect(left + width + 3, y + (barH - valueH) / 2, tw, valueH), text, valueStyle));
                }
            }
        }
    }

    // === Pie / Donut ===

    private void EmitRadial(Rect area)
    {
        var series = _chart.Series.FirstOrDefault();
        if (series is null) return;

        var values = _chart.Categories.Select((_, i) => Math.Max(0, ValueAt(series, i) ?? 0)).ToList();
        var total = values.Sum();
        var radius = Math.Min(area.Width, area.Height) / 2 - 2;
        if (radius <= 2) return;
        var center = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
        var inner = _chart.Kind == ChartKind.Donut ? radius * Math.Clamp(_chart.InnerRadius, 0, 0.9) : 0;

        if (total <= 0)
        {
            Add(DrawPathCommand.Create(Ring(center, radius, inner, 0, 360), closed: true, GridColor, null));
            return;
        }

        var labelStyle = LabelStyle(TextAlignment.Center, Color.White) with { Weight = FontWeight.Bold };
        var labelH = _measurer.LineHeight(labelStyle);
        var start = -90.0;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] <= 0) continue;
            var sweep = values[i] / total * 360;
            var points = Ring(center, radius, inner, start, sweep);
            Add(DrawPathCommand.Create(points, closed: true, _chart.ColorAt(i), new BorderLine(1, Color.White)));

            var label = _chart.ShowPercent
                ? (values[i] / total).ToString("P0", _culture)
                : _chart.ShowValues ? Format(values[i]) : null;
            if (label is not null && sweep >= 12)
            {
                var mid = (start + sweep / 2) * Math.PI / 180;
                var r = inner > 0 ? (radius + inner) / 2 : radius * 0.62;
                var p = new Point(center.X + Math.Cos(mid) * r, center.Y + Math.Sin(mid) * r);
                var w = _measurer.MeasureWidth(label, labelStyle) + 6;
                Add(new DrawTextCommand(new Rect(p.X - w / 2, p.Y - labelH / 2, w, labelH), label, labelStyle));
            }
            start += sweep;
        }

        if (inner > 0 && _chart.ShowValues)
        {
            var totalStyle = LabelStyle(TextAlignment.Center, _style.Foreground) with
            {
                Weight = FontWeight.Bold,
                FontSize = Math.Min(_style.FontSize * 1.4, inner * 0.5)
            };
            var h = _measurer.LineHeight(totalStyle);
            Add(new DrawTextCommand(new Rect(center.X - inner, center.Y - h / 2, inner * 2, h), Format(total), totalStyle));
        }
    }

    /// <summary>Porción de anillo (o de pastel si <paramref name="inner"/> = 0) como polígono.</summary>
    private static List<Point> Ring(Point c, double outer, double inner, double startDeg, double sweepDeg)
    {
        var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweepDeg) / 3));
        var points = new List<Point>(steps * 2 + 2);
        for (var i = 0; i <= steps; i++)
        {
            var a = (startDeg + sweepDeg * i / steps) * Math.PI / 180;
            points.Add(new Point(c.X + Math.Cos(a) * outer, c.Y + Math.Sin(a) * outer));
        }
        if (inner <= 0)
        {
            if (sweepDeg < 360) points.Add(c);
            return points;
        }
        for (var i = steps; i >= 0; i--)
        {
            var a = (startDeg + sweepDeg * i / steps) * Math.PI / 180;
            points.Add(new Point(c.X + Math.Cos(a) * inner, c.Y + Math.Sin(a) * inner));
        }
        return points;
    }

    private static List<Point> Circle(Point c, double r, int segments)
    {
        var points = new List<Point>(segments);
        for (var i = 0; i < segments; i++)
        {
            var a = 2 * Math.PI * i / segments;
            points.Add(new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r));
        }
        return points;
    }

    // === Helpers ===

    private void EmitCategoryLabels(IReadOnlyList<string> categories, Func<int, double> slotX, double slot,
        double top, double lineH, ResolvedStyle style)
    {
        if (categories.Count == 0) return;
        // Si las etiquetas no caben, se muestra una de cada k para que no se encimen.
        var widest = categories.Max(c => _measurer.MeasureWidth(c, style)) + 6;
        var every = Math.Max(1, (int)Math.Ceiling(widest / Math.Max(1, slot)));
        for (var i = 0; i < categories.Count; i += every)
        {
            var w = Math.Max(slot, widest);
            var x = slotX(i) + slot / 2 - w / 2;
            Add(new DrawTextCommand(new Rect(x, top, w, lineH), categories[i], style));
        }
    }

    private static List<double> Ticks(double min, double max, double step)
    {
        var ticks = new List<double>();
        for (var v = min; v <= max + step * 1e-6; v += step)
            ticks.Add(Math.Abs(v) < step * 1e-9 ? 0 : Math.Round(v, 10));
        if (ticks.Count == 0) ticks.Add(min);
        return ticks;
    }

    private string Format(double v) =>
        _chart.ValueFormat is { } f ? v.ToString(f, _culture)
        : Math.Abs(v - Math.Round(v)) < 1e-9 ? v.ToString("#,##0", _culture)
        : v.ToString("#,##0.##", _culture);

    private ResolvedStyle LabelStyle(TextAlignment align, Color color) => _style with
    {
        TextAlign = align,
        VerticalAlign = VerticalAlignment.Top,
        Foreground = color,
        Background = Color.Transparent,
        Padding = Thickness.Zero,
        Border = null,
        FontSize = Math.Max(5, _style.FontSize * 0.9),
        Weight = FontWeight.Normal
    };

    private static Color WithAlpha(Color c, byte alpha) => c with { A = alpha };

    private static Rect Inset(Rect r, double d) =>
        new(r.X + d, r.Y + d, Math.Max(0, r.Width - 2 * d), Math.Max(0, r.Height - 2 * d));

    private void Add(RenderCommand cmd) => _out.Add(cmd with { SourcePath = _path });
}
