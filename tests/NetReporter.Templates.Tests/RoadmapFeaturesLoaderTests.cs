using System.Text.Json;
using NetReporter.Core.Bands;
using NetReporter.Core.Elements;
using NetReporter.Core.Layout;
using NetReporter.Core.RenderList;

namespace NetReporter.Templates.Tests;

/// <summary>YAML → IR para las funciones de la v1.1: DSL, grupos anidados, charts, visible y estilo dinámico.</summary>
public sealed class RoadmapFeaturesLoaderTests
{
    private const string Data = """
        {
          "anio": 2025,
          "descuento": 0,
          "ventas": [
            { "region": "Norte", "cat": "HW", "mes": "Ene", "total": 100, "meta": 90 },
            { "region": "Norte", "cat": "HW", "mes": "Feb", "total": 150, "meta": 120 },
            { "region": "Norte", "cat": "SW", "mes": "Mar", "total": 80,  "meta": 100 },
            { "region": "Sur",   "cat": "HW", "mes": "Abr", "total": 60,  "meta": 70 }
          ]
        }
        """;

    private const string Styles = """
        styles:
          Default: { fontFamily: Helvetica, fontSize: 9 }
          TableHeader: { basedOn: Default, bold: true }
          TableRow: { basedOn: Default }
          GroupHeader: { basedOn: Default, bold: true }
          GroupFooter: { basedOn: Default, bold: true }
          TableSummary: { basedOn: Default, bold: true }
          Alta: { basedOn: Default, foreground: "#16A34A" }
          Baja: { basedOn: Default, foreground: "#DC2626" }
        """;

    private static Core.Definition.ReportDefinition Load(string bands) =>
        YamlReportLoader.Parse($"name: T\n{Styles}\nbands:\n{bands}").Bind(Data);

    private static List<string> Texts(Core.Definition.ReportDefinition report) =>
        new LayoutEngine().Layout(report).Pages.SelectMany(p => p.Commands)
            .OfType<DrawTextCommand>().Select(c => c.Text).ToList();

    [Fact]
    public void Chart_IsBuiltFromRowsCategoryAndSeries()
    {
        var report = Load("""
              - kind: ReportHeader
                height: 200
                elements:
                  - type: chart
                    chartType: line
                    bounds: { x: 0, y: 0, width: 300, height: 180 }
                    rows: "$.ventas"
                    category: "$.mes"
                    title: "Ventas {{ $.anio }}"
                    legend: right
                    valueFormat: N0
                    palette: ["#111111", "#222222"]
                    series:
                      - { name: "Real", value: "$.total" }
                      - { name: "Meta +10%", value: "= $.meta * 1.1", color: "#FF0000" }
            """);

        var chart = Assert.IsType<ChartElement>(report.Bands[0].Elements[0]);
        Assert.Equal(ChartKind.Line, chart.Kind);
        Assert.Equal(new[] { "Ene", "Feb", "Mar", "Abr" }, chart.Categories);
        Assert.Equal("Ventas 2025", chart.Title);
        Assert.Equal(ChartLegendPosition.Right, chart.Legend);
        Assert.Equal(new double?[] { 100, 150, 80, 60 }, chart.Series[0].Values);
        Assert.Equal(99, chart.Series[1].Values[0]!.Value, 6);
        Assert.Equal(Core.Primitives.Color.FromHex("#FF0000"), chart.Series[1].Color);
        Assert.Equal("bands.0.elements.0", chart.SourcePath);
        Assert.Contains("Ventas 2025", Texts(report));
    }

    [Theory]
    [InlineData("bar", ChartKind.Bar)]
    [InlineData("column", ChartKind.Bar)]
    [InlineData("horizontalBar", ChartKind.HorizontalBar)]
    [InlineData("hbar", ChartKind.HorizontalBar)]
    [InlineData("area", ChartKind.Area)]
    [InlineData("pie", ChartKind.Pie)]
    [InlineData("donut", ChartKind.Donut)]
    public void Chart_TypeAliases(string yamlType, ChartKind expected)
    {
        var report = Load($$"""
              - kind: ReportHeader
                height: 100
                elements:
                  - { type: chart, chartType: {{yamlType}}, rows: "$.ventas", category: "$.mes", series: [ { value: "$.total" } ], bounds: { x: 0, y: 0, width: 200, height: 100 } }
            """);
        Assert.Equal(expected, ((ChartElement)report.Bands[0].Elements[0]).Kind);
    }

    [Theory]
    [InlineData("{ type: chart, category: \"$.mes\", series: [ { value: \"$.total\" } ] }", "rows")]
    [InlineData("{ type: chart, rows: \"$.ventas\", series: [ { value: \"$.total\" } ] }", "category")]
    [InlineData("{ type: chart, rows: \"$.ventas\", category: \"$.mes\" }", "serie")]
    [InlineData("{ type: chart, chartType: radar, rows: \"$.ventas\", category: \"$.mes\", series: [ { value: \"$.total\" } ] }", "chartType")]
    public void Chart_InvalidDefinitions_ThrowHelpfulErrors(string element, string mentions)
    {
        var ex = Assert.ThrowsAny<FormatException>(() => Load($"""
              - kind: ReportHeader
                height: 100
                elements:
                  - {element}
            """));
        Assert.Contains(mentions, ex.Message);
    }

    [Fact]
    public void NestedGroups_WithSummary_AndExpressionColumn()
    {
        var texts = Texts(Load("""
              - kind: Detail
                height: 0
                elements:
                  - type: table
                    bounds: { x: 0, y: 0, width: 300, height: 0 }
                    rows: "$.ventas"
                    columns:
                      - { header: "Mes", binding: "$.mes", width: 100 }
                      - { header: "Desvío", binding: "= $.total - $.meta", width: 100, align: right }
                      - { header: "Total", binding: "$.total", width: 100, align: right, format: N0 }
                    groups:
                      - by: "$.region"
                        header: { height: 16, content: "Región {{ #group }}" }
                        footer:
                          height: 16
                          cells: [ { content: "Total {{ #group }}" }, null, { aggregate: sum, format: N0 } ]
                      - by: "$.cat"
                        header: { height: 14, content: "{{ group(0) }} › {{ #group }} ({{ #count }})" }
                    summary:
                      height: 18
                      cells: [ { content: "General ({{ #count }})" }, null, { aggregate: max, format: N0 } ]
            """));

        Assert.Contains("Región Norte", texts);
        Assert.Contains("Norte › HW (2)", texts);
        Assert.Contains("Sur › HW (1)", texts);
        Assert.Contains("10", texts);   // 100 - 90
        Assert.Contains("-20", texts);  // 80 - 100
        Assert.Equal("330", texts[texts.IndexOf("Total Norte") + 2]); // +1 es la celda null (vacía)
        Assert.Equal("General (4)", texts[^3]);
        Assert.Equal("150", texts[^1]);
    }

    [Fact]
    public void Visible_OnElementsAndBands()
    {
        var texts = Texts(Load("""
              - kind: ReportHeader
                height: 40
                elements:
                  - { type: text, content: "siempre", bounds: { x: 0, y: 0, width: 100, height: 14 } }
                  - { type: text, content: "con descuento", visible: "$.descuento > 0", bounds: { x: 0, y: 14, width: 100, height: 14 } }
              - kind: Detail
                height: 20
                visible: "{{ count($.ventas[*]) > 3 }}"
                elements:
                  - { type: text, content: "muchas ventas", bounds: { x: 0, y: 0, width: 100, height: 14 } }
            """));

        Assert.Contains("siempre", texts);
        Assert.DoesNotContain("con descuento", texts);
        Assert.Contains("muchas ventas", texts);
    }

    [Fact]
    public void DynamicStyle_ResolvesAtRenderTime()
    {
        var report = Load("""
              - kind: ReportHeader
                height: 20
                elements:
                  - type: text
                    content: "Cumplimiento"
                    style: "{{ sum($.ventas[*].total) >= sum($.ventas[*].meta) ? 'Alta' : 'Baja' }}"
                    bounds: { x: 0, y: 0, width: 100, height: 14 }
            """);
        var cmd = new LayoutEngine().Layout(report).Pages[0].Commands.OfType<DrawTextCommand>().Single();
        // 390 total vs 380 meta → Alta (verde)
        Assert.Equal(Core.Primitives.Color.FromHex("#16A34A"), cmd.Style.Foreground);
        Assert.NotNull(report.Bands[0].Elements[0].StyleSelector);
    }

    [Fact]
    public void TextFlags_WordWrapAndAutoHeight_AreRead()
    {
        var report = Load("""
              - kind: ReportHeader
                height: 20
                elements:
                  - { type: text, content: "x", wordWrap: false, autoHeight: true, bounds: { x: 0, y: 0, width: 100, height: 14 } }
            """);
        var text = Assert.IsType<TextElement>(report.Bands[0].Elements[0]);
        Assert.False(text.WordWrap);
        Assert.True(text.AutoHeight);
    }

    [Fact]
    public void BandFlags_PrintOnFirstAndLastPage_AreRead()
    {
        var report = Load("""
              - kind: PageFooter
                height: 20
                printOnFirstPage: false
                printOnLastPage: false
                elements: []
            """);
        var band = Assert.IsType<PageFooterBand>(report.Bands[0]);
        Assert.False(band.PrintOnFirstPage);
        Assert.False(band.PrintOnLastPage);
    }

    [Fact]
    public void InvalidExpression_FailsAtLoadTime_WithPosition()
    {
        var ex = Assert.ThrowsAny<FormatException>(() => Load("""
              - kind: ReportHeader
                height: 20
                elements:
                  - { type: text, content: "{{ round() }}", bounds: { x: 0, y: 0, width: 100, height: 14 } }
            """));
        Assert.Contains("round()", ex.Message);
        Assert.Contains("posición", ex.Message);
    }

    [Fact]
    public void GroupLevel_WithoutBy_Throws() =>
        Assert.ThrowsAny<FormatException>(() => Load("""
              - kind: Detail
                height: 0
                elements:
                  - type: table
                    rows: "$.ventas"
                    columns: [ { header: "Mes", binding: "$.mes", width: 100 } ]
                    groups: [ { header: { height: 14, content: "x" } } ]
            """));

    [Fact]
    public void JsonDataUsedByFixture_IsValid() => Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(Data).RootElement.ValueKind);

    [Fact]
    public void Title_ResolvesTemplateStrings()
    {
        var report = YamlReportLoader.Parse("name: T\ntitle: \"Ventas {{ $.anio }}\"\nbands: []").Bind(Data);
        Assert.Equal("Ventas 2025", report.Title);
    }
}
