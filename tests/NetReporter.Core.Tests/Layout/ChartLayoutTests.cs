using NetReporter.Core.Bands;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Layout;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Tests.Layout;

public sealed class ChartLayoutTests
{
    private static readonly StyleSheet Styles = new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Helvetica").FontSize(9))
        .Build();

    private static readonly Rect ChartBounds = new(0, 0, 400, 220);

    private static ChartElement Chart(ChartKind kind, params ChartSeries[] series) => new()
    {
        Bounds = ChartBounds,
        Kind = kind,
        Categories = ["Ene", "Feb", "Mar", "Abr"],
        Series = series.Length > 0 ? series : [new ChartSeries("Ventas", [10, 25, 15, 30])],
        SourcePath = "bands.0.elements.0"
    };

    private static List<RenderCommand> Layout(ChartElement chart)
    {
        var report = new ReportDefinition
        {
            Name = "Chart",
            Page = PageSetup.Letter.WithMargins(new Thickness(20)),
            Styles = Styles,
            Bands = [new ReportHeaderBand { Height = 240, Elements = [chart] }]
        };
        return new LayoutEngine().Layout(report).Pages.Single().Commands.ToList();
    }

    [Fact]
    public void BarChart_EmitsOneBarPerValue_WithinBounds()
    {
        var cmds = Layout(Chart(ChartKind.Bar));
        var blue = ChartElement.DefaultPalette[0];
        var bars = cmds.OfType<DrawRectangleCommand>().Where(r => r.Fill == blue).ToList();

        Assert.Equal(4, bars.Count);
        // Barras más altas para valores mayores (30 > 25 > 15 > 10).
        var heights = bars.Select(b => b.Bounds.Height).ToList();
        Assert.True(heights[3] > heights[1] && heights[1] > heights[2] && heights[2] > heights[0]);

        var area = ChartBounds.Offset(20, 20);
        Assert.All(cmds, c => Assert.True(c.Bounds.X >= area.X - 1 && c.Bounds.Right <= area.Right + 1,
            $"{c.GetType().Name} fuera del chart: {c.Bounds}"));
    }

    [Fact]
    public void AllCommands_CarryTheElementSourcePath() =>
        Assert.All(Layout(Chart(ChartKind.Line)), c => Assert.Equal("bands.0.elements.0", c.SourcePath));

    [Fact]
    public void Axis_ShowsNiceTickLabels_AndCategoryLabels()
    {
        var texts = Layout(Chart(ChartKind.Bar)).OfType<DrawTextCommand>().Select(t => t.Text).ToList();
        Assert.Contains("0", texts);
        Assert.Contains("30", texts);
        Assert.Contains("Ene", texts);
        Assert.Contains("Abr", texts);
    }

    [Fact]
    public void GroupedBars_TwoSeries_EmitLegendAndEightBars()
    {
        var chart = Chart(ChartKind.Bar,
            new ChartSeries("2025", [10, 20, 30, 40]),
            new ChartSeries("2026", [15, 25, 35, 45]) { Color = Color.FromHex("#111111") });
        var cmds = Layout(chart);

        Assert.Equal(4, cmds.OfType<DrawRectangleCommand>().Count(r => r.Fill == Color.FromHex("#111111") && r.Bounds.Height > 8));
        var texts = cmds.OfType<DrawTextCommand>().Select(t => t.Text).ToList();
        Assert.Contains("2025", texts);
        Assert.Contains("2026", texts);
    }

    [Fact]
    public void StackedBars_ScaleToTheSumOfSeries()
    {
        var chart = Chart(ChartKind.Bar,
            new ChartSeries("A", [50, 50, 50, 50]),
            new ChartSeries("B", [50, 50, 50, 50])) with { Stacked = true };
        var texts = Layout(chart).OfType<DrawTextCommand>().Select(t => t.Text).ToList();
        Assert.Contains("100", texts);
    }

    [Fact]
    public void LineChart_EmitsOpenPolyline_AndMarkers()
    {
        var cmds = Layout(Chart(ChartKind.Line));
        var paths = cmds.OfType<DrawPathCommand>().ToList();

        var line = Assert.Single(paths, p => !p.Closed);
        Assert.Equal(4, line.Points.Count);
        Assert.Equal(4, paths.Count(p => p.Closed)); // marcadores
    }

    [Fact]
    public void LineChart_NullValue_BreaksTheLine()
    {
        var cmds = Layout(Chart(ChartKind.Line, new ChartSeries("S", [10, null, 15, 30])) with { ShowMarkers = false });
        var segments = cmds.OfType<DrawPathCommand>().Where(p => !p.Closed).ToList();
        Assert.Single(segments); // [10] queda solo (sin segmento), [15, 30] forma una línea
        Assert.Equal(2, segments[0].Points.Count);
    }

    [Fact]
    public void AreaChart_EmitsClosedFilledPolygon()
    {
        var cmds = Layout(Chart(ChartKind.Area) with { ShowMarkers = false });
        var area = Assert.Single(cmds.OfType<DrawPathCommand>(), p => p.Closed);
        Assert.NotNull(area.Fill);
        Assert.True(area.Fill!.Value.A < 255);
    }

    [Fact]
    public void PieChart_OneSlicePerCategory_WithPercentLabels()
    {
        var chart = Chart(ChartKind.Pie, new ChartSeries("Mix", [50, 25, 25, 0])) with { ShowPercent = true };
        var cmds = Layout(chart);

        var slices = cmds.OfType<DrawPathCommand>().Where(p => p.Closed).ToList();
        Assert.Equal(3, slices.Count); // la categoría con 0 no dibuja porción
        var texts = cmds.OfType<DrawTextCommand>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t.Replace(" ", "") == "50%");
        Assert.Equal(2, texts.Count(t => t.Replace(" ", "") == "25%"));
        // Leyenda: una entrada por categoría.
        Assert.Contains("Abr", texts);
    }

    [Fact]
    public void DonutChart_SlicesAreRings()
    {
        var cmds = Layout(Chart(ChartKind.Donut, new ChartSeries("Mix", [1, 1, 1, 1])));
        var slices = cmds.OfType<DrawPathCommand>().Where(p => p.Closed).ToList();
        Assert.Equal(4, slices.Count);

        // Ningún vértice de una porción está en el centro (hay hueco).
        var cx = slices.SelectMany(s => s.Points).Average(p => p.X);
        var cy = slices.SelectMany(s => s.Points).Average(p => p.Y);
        var minDistance = slices.SelectMany(s => s.Points).Min(p => Math.Sqrt(Math.Pow(p.X - cx, 2) + Math.Pow(p.Y - cy, 2)));
        Assert.True(minDistance > 10);
    }

    [Fact]
    public void HorizontalBars_CategoriesOnTheLeft()
    {
        var cmds = Layout(Chart(ChartKind.HorizontalBar) with { ShowValues = true });
        var blue = ChartElement.DefaultPalette[0];
        var bars = cmds.OfType<DrawRectangleCommand>().Where(r => r.Fill == blue).ToList();
        Assert.Equal(4, bars.Count);
        Assert.True(bars[3].Bounds.Width > bars[0].Bounds.Width);
        var label = cmds.OfType<DrawTextCommand>().First(t => t.Text == "Ene");
        Assert.True(label.Bounds.Right <= bars[0].Bounds.X);
    }

    [Fact]
    public void TitleAndValueFormat_AreApplied()
    {
        var cmds = Layout(Chart(ChartKind.Bar) with { Title = "Ventas 2025", ShowValues = true, ValueFormat = "N1" });
        var texts = cmds.OfType<DrawTextCommand>().Select(t => t.Text).ToList();
        Assert.Contains("Ventas 2025", texts);
        Assert.Contains("25.0", texts);
    }

    [Fact]
    public void EmptyData_DoesNotThrow()
    {
        var chart = new ChartElement { Bounds = ChartBounds, Kind = ChartKind.Pie, Categories = [], Series = [] };
        Assert.NotNull(Layout(chart));
    }

    [Fact]
    public void From_BuildsCategoriesAndSeriesWithoutReflection()
    {
        var rows = new[] { (Mes: "Ene", Total: 10.0), (Mes: "Feb", Total: 20.0) };
        var (categories, series) = ChartElement.From(rows, r => r.Mes, ("Total", r => r.Total));
        Assert.Equal(new[] { "Ene", "Feb" }, categories);
        Assert.Equal(new double?[] { 10, 20 }, series[0].Values);
    }

    [Theory]
    [InlineData(ChartKind.Bar)]
    [InlineData(ChartKind.HorizontalBar)]
    [InlineData(ChartKind.Line)]
    public void ValueLabels_StayInsideTheChartBounds(ChartKind kind)
    {
        var chart = Chart(kind, new ChartSeries("Ventas", [1245, 812, 431, 300])) with { ShowValues = true, ValueFormat = "N0" };
        var area = ChartBounds.Offset(20, 20);
        Assert.All(Layout(chart), c =>
        {
            Assert.True(c.Bounds.X >= area.X - 0.5 && c.Bounds.Right <= area.Right + 0.5, $"{c} fuera en X");
            Assert.True(c.Bounds.Y >= area.Y - 0.5 && c.Bounds.Bottom <= area.Bottom + 0.5, $"{c} fuera en Y");
        });
    }

    // === Rounded bars, chart corner radius and soft color scheme ===

    private static List<DrawRectangleCommand> Bars(List<RenderCommand> cmds, Color fill) =>
        cmds.OfType<DrawRectangleCommand>().Where(r => r.Fill == fill && r.Bounds.Width > 8 && r.Bounds.Height > 8).ToList();

    [Fact]
    public void BarRadius_Zero_KeepsSquareBars()
    {
        var bars = Bars(Layout(Chart(ChartKind.Bar)), ChartElement.DefaultPalette[0]);
        Assert.Equal(4, bars.Count);
        Assert.All(bars, b => Assert.Equal(0, b.CornerRadius));
    }

    [Fact]
    public void BarRadius_RoundsTheFreeEnd_OfPositiveAndNegativeBars()
    {
        var chart = Chart(ChartKind.Bar, new ChartSeries("Neto", [10, -8, 15, 30])) with { BarRadius = 4 };
        var bars = Bars(Layout(chart), ChartElement.DefaultPalette[0]);

        Assert.Equal(4, bars.Count);
        Assert.All(bars, b => Assert.Equal(4, b.CornerRadius));
        Assert.Equal(RectCorners.Top, bars[0].Corners);
        Assert.Equal(RectCorners.Bottom, bars[1].Corners);   // negative value: the free end is the bottom
    }

    [Fact]
    public void BarRadius_IsClampedToHalfTheBarWidth()
    {
        var chart = Chart(ChartKind.Bar) with { BarRadius = 500 };
        var bars = Bars(Layout(chart), ChartElement.DefaultPalette[0]);
        Assert.All(bars, b => Assert.Equal(b.Bounds.Width / 2, b.CornerRadius, 6));
    }

    [Fact]
    public void BarRadius_HorizontalBars_RoundTheRightEnd()
    {
        var chart = Chart(ChartKind.HorizontalBar) with { BarRadius = 3 };
        var bars = Bars(Layout(chart), ChartElement.DefaultPalette[0]);
        Assert.Equal(4, bars.Count);
        Assert.All(bars, b => Assert.Equal(RectCorners.Right, b.Corners));
        Assert.All(bars, b => Assert.Equal(3, b.CornerRadius));
    }

    [Fact]
    public void BarRadius_StackedBars_RoundOnlyTheOuterSegment()
    {
        var bottom = Color.FromHex("#111111");
        var top = Color.FromHex("#222222");
        var chart = Chart(ChartKind.Bar,
            new ChartSeries("A", [10, 20, 30, 40]) { Color = bottom },
            new ChartSeries("B", [5, 5, null, 5]) { Color = top }) with { Stacked = true, BarRadius = 4 };
        var cmds = Layout(chart);

        var lower = Bars(cmds, bottom);
        var upper = Bars(cmds, top);
        Assert.Equal(3, upper.Count);
        Assert.All(upper, b => Assert.Equal(RectCorners.Top, b.Corners));
        // "Mar" has no value in B, so its A segment is the outer one and gets the rounded end.
        Assert.Equal(new[] { 0d, 0d, 4d, 0d }, lower.Select(b => b.CornerRadius));
    }

    [Fact]
    public void CornerRadius_RoundsTheChartBackground()
    {
        var chart = Chart(ChartKind.Line) with { Background = Color.FromHex("#F8FAFC"), CornerRadius = 10 };
        var background = Layout(chart).OfType<DrawRectangleCommand>().First(r => r.Fill == Color.FromHex("#F8FAFC"));
        Assert.Equal(10, background.CornerRadius);
        Assert.Equal(RectCorners.All, background.Corners);
    }

    [Fact]
    public void SoftScheme_UsesTheSoftPalette_AndDarkInsideLabels()
    {
        var chart = Chart(ChartKind.Pie) with { ColorScheme = ChartColorScheme.Soft, ShowPercent = true };
        var cmds = Layout(chart);

        var slices = cmds.OfType<DrawPathCommand>().Where(p => p.Fill is not null).Select(p => p.Fill).ToList();
        Assert.Equal(ChartElement.SoftPalette.Take(4).Cast<Color?>(), slices);
        var percent = cmds.OfType<DrawTextCommand>().First(t => t.Text.Contains('%'));
        Assert.Equal(Color.FromHex("#1E293B"), percent.Style.Foreground);
    }

    [Fact]
    public void DefaultScheme_KeepsWhiteInsideLabels_AndExplicitPaletteWins()
    {
        var pie = Layout(Chart(ChartKind.Pie) with { ShowPercent = true });
        Assert.Equal(Color.White, pie.OfType<DrawTextCommand>().First(t => t.Text.Contains('%')).Style.Foreground);

        var custom = Color.FromHex("#123456");
        var chart = Chart(ChartKind.Bar) with { ColorScheme = ChartColorScheme.Soft, Palette = [custom] };
        Assert.Equal(4, Bars(Layout(chart), custom).Count);
    }
}
