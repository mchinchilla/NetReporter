using NetReporter.Templates.Schema;

namespace NetReporter.Templates.Tests;

/// <summary>Operaciones del Designer v1.1: multi-selección, clipboard y patches de image/barcode/chart.</summary>
public sealed class YamlReportRewriterMultiTests
{
    private const string Yaml = """
        name: Test
        styles:
          Default: { fontFamily: Helvetica, fontSize: 10 }
        bands:
          - kind: ReportHeader
            height: 60
            elements:
              - { type: text, bounds: { x: 10, y: 5, width: 200, height: 20 }, content: Uno }
              - { type: text, bounds: { x: 10, y: 30, width: 200, height: 20 }, content: Dos }
              - { type: rectangle, bounds: { x: 300, y: 0, width: 50, height: 50 }, fill: "#EEEEEE" }
          - kind: Detail
            height: 40
            elements:
              - { type: text, bounds: { x: 0, y: 0, width: 100, height: 14 }, content: Tres }
        """;

    private static ReportYaml Parse(string yaml) => YamlReportLoader.ParseModel(yaml);

    [Fact]
    public void MoveElements_AppliesPerElementDeltas()
    {
        var result = YamlReportRewriter.MoveElements(Yaml,
        [
            ("bands.0.elements.0", 5, 1),
            ("bands.1.elements.0", -2, 10)
        ]);
        var m = Parse(result);
        Assert.Equal(15, m.Bands![0].Elements![0].Bounds!.X);
        Assert.Equal(6, m.Bands[0].Elements![0].Bounds!.Y);
        Assert.Equal(-2, m.Bands[1].Elements![0].Bounds!.X);
        Assert.Equal(10, m.Bands[1].Elements![0].Bounds!.Y);
        Assert.Equal(10, m.Bands[0].Elements![1].Bounds!.X); // no tocado
    }

    [Fact]
    public void DeleteElements_RemovesAllRegardlessOfOrder()
    {
        var result = YamlReportRewriter.DeleteElements(Yaml, ["bands.0.elements.0", "bands.0.elements.2", "bands.1.elements.0"]);
        var m = Parse(result);
        Assert.Equal("Dos", Assert.Single(m.Bands![0].Elements!).Content);
        Assert.Empty(m.Bands[1].Elements!);
    }

    [Fact]
    public void DeleteElements_InvalidPath_Throws() =>
        Assert.Throws<InvalidOperationException>(() => YamlReportRewriter.DeleteElements(Yaml, ["bands.0.elements.9"]));

    [Fact]
    public void CopyPaste_IntoOriginalBands_WithOffset()
    {
        var clip = YamlReportRewriter.CopyElements(Yaml, ["bands.1.elements.0", "bands.0.elements.1"]);
        Assert.Contains("netReporterClipboard: 1", clip);

        var (result, paths) = YamlReportRewriter.PasteElements(Yaml, clip, targetBand: null);
        Assert.Equal(new[] { "bands.0.elements.3", "bands.1.elements.1" }, paths);

        var m = Parse(result);
        var pasted = m.Bands![0].Elements![3];
        Assert.Equal("Dos", pasted.Content);
        Assert.Equal(20, pasted.Bounds!.X);
        Assert.Equal(40, pasted.Bounds.Y);
        Assert.Equal("Tres", m.Bands[1].Elements![1].Content);
    }

    [Fact]
    public void Paste_IntoTargetBand_AndIntoAnotherTemplate()
    {
        var clip = YamlReportRewriter.CopyElements(Yaml, ["bands.0.elements.0", "bands.0.elements.2"]);
        const string other = """
            name: Otro
            bands:
              - kind: Detail
                height: 10
                elements: []
            """;
        var (result, paths) = YamlReportRewriter.PasteElements(other, clip, targetBand: 5, offsetX: 0, offsetY: 0);

        var m = Parse(result);
        Assert.Equal(2, m.Bands![0].Elements!.Count); // banda 5 no existe → se ajusta a la última
        Assert.Equal(new[] { "bands.0.elements.0", "bands.0.elements.1" }, paths);
        Assert.Equal("#EEEEEE", m.Bands[0].Elements![1].Fill);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hola: mundo")]
    [InlineData("netReporterClipboard: 1\nitems: []")]
    public void Paste_InvalidClipboard_Throws(string clipboard) =>
        Assert.ThrowsAny<FormatException>(() => YamlReportRewriter.PasteElements(Yaml, clipboard, null));

    [Theory]
    [InlineData("image")]
    [InlineData("barcode")]
    [InlineData("chart")]
    public void AddElement_NewKinds_ProduceLoadableTemplates(string kind)
    {
        var (result, path) = YamlReportRewriter.AddElement(Yaml, 1, kind, 20, 5);
        Assert.Equal("bands.1.elements.1", path);
        var el = Parse(result).Bands![1].Elements![1];
        Assert.Equal(kind, el.Type);

        // El template resultante carga y enlaza sin errores (con datos vacíos).
        var report = YamlReportLoader.Parse(result).Bind("{}");
        Assert.NotNull(report);
    }

    [Fact]
    public void ApplyPatch_ImageBarcodeChart_AndVisible()
    {
        var (withImage, imagePath) = YamlReportRewriter.AddElement(Yaml, 0, "image", 0, 0);
        var (withBarcode, barcodePath) = YamlReportRewriter.AddElement(withImage, 0, "barcode", 0, 0);
        var (withChart, chartPath) = YamlReportRewriter.AddElement(withBarcode, 0, "chart", 0, 0);

        var result = YamlReportRewriter.ApplyPatch(withChart, imagePath, new ElementPatch { Source = "{{ $.logo }}", Fit = "Fill" });
        result = YamlReportRewriter.ApplyPatch(result, barcodePath, new ElementPatch
        {
            Value = "{{ $.cai }}", BarcodeFormat = "CODE128", BarcodeForeground = "#111111", Visible = "$.cai != null"
        });
        result = YamlReportRewriter.ApplyPatch(result, chartPath, new ElementPatch
        {
            ChartType = "donut", Rows = "$.ventas", Category = "$.mes", Title = "", ShowPercent = true, ValueFormat = "N0"
        });
        result = YamlReportRewriter.UpdateSeries(result, chartPath,
            [new ChartSeriesYaml { Name = "Total", Value = "$.total", Color = "#2563EB" }]);

        var els = Parse(result).Bands![0].Elements!;
        var image = els[3];
        Assert.Equal("{{ $.logo }}", image.Source);
        Assert.Equal("fill", image.Fit);

        var barcode = els[4];
        Assert.Equal("code128", barcode.Format);
        Assert.Equal("{{ $.cai }}", barcode.Value);
        Assert.Equal("$.cai != null", barcode.Visible);

        var chart = els[5];
        Assert.Equal("donut", chart.ChartType);
        Assert.Equal("$.ventas", chart.Rows);
        Assert.Null(chart.Title);
        Assert.True(chart.ShowPercent);
        Assert.Equal("#2563EB", Assert.Single(chart.Series!).Color);
    }

    [Fact]
    public void ApplyPatch_ChartFieldsOnText_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            YamlReportRewriter.ApplyPatch(Yaml, "bands.0.elements.0", new ElementPatch { ChartType = "pie" }));

    [Fact]
    public void ApplyPatch_EmptyVisible_RemovesCondition()
    {
        var withCondition = YamlReportRewriter.ApplyPatch(Yaml, "bands.0.elements.0", new ElementPatch { Visible = "$.x > 0" });
        var cleared = YamlReportRewriter.ApplyPatch(withCondition, "bands.0.elements.0", new ElementPatch { Visible = "" });
        Assert.Null(Parse(cleared).Bands![0].Elements![0].Visible);
        Assert.DoesNotContain("visible", cleared);
    }

    [Fact]
    public void UpdateSeries_RequiresValue() =>
        Assert.Throws<ArgumentException>(() =>
        {
            var (withChart, path) = YamlReportRewriter.AddElement(Yaml, 0, "chart", 0, 0);
            YamlReportRewriter.UpdateSeries(withChart, path, [new ChartSeriesYaml { Name = "x" }]);
        });
}
