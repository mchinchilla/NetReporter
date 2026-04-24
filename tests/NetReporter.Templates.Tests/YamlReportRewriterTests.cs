using NetReporter.Templates;
using NetReporter.Templates.Schema;

namespace NetReporter.Templates.Tests;

public sealed class YamlReportRewriterTests
{
    private const string BasicYaml = """
        name: Test
        page: { size: Letter, margins: { all: 1cm } }
        styles:
          Default: { fontFamily: Helvetica, fontSize: 10 }
          TableHeader: { basedOn: Default, bold: true }
          TableRow: { basedOn: Default }
        bands:
          - kind: ReportHeader
            height: 40
            elements:
              - type: text
                bounds: { x: 10, y: 5, width: 200, height: 20 }
                content: Hola
                style: Default
              - type: line
                bounds: { x: 0, y: 30, width: 500, height: 0 }
                color: "#000000"
                thickness: 0.5
          - kind: Detail
            height: 0
            elements:
              - type: table
                bounds: { x: 0, y: 0, width: 300, height: 0 }
                rows: "$.items"
                headerStyle: TableHeader
                rowStyle: TableRow
                headerHeight: 22
                rowHeight: 18
                columns:
                  - { header: A, binding: "$.a", width: 100, align: left }
                  - { header: B, binding: "$.b", width: 100, align: left }
                  - { header: C, binding: "$.c", width: 100, align: right }
        """;

    private static ReportYaml Parse(string yaml) => YamlReportLoader.ParseModel(yaml);

    // === MoveElement ===

    [Fact]
    public void MoveElement_AppliesDeltaToBounds()
    {
        var result = YamlReportRewriter.MoveElement(BasicYaml, "bands.0.elements.0", 25, 7);
        var model = Parse(result);
        var el = model.Bands![0].Elements![0];
        Assert.Equal(35, el.Bounds!.X);
        Assert.Equal(12, el.Bounds.Y);
    }

    [Fact]
    public void MoveElement_InvalidBandIndex_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            YamlReportRewriter.MoveElement(BasicYaml, "bands.99.elements.0", 1, 1));
    }

    [Theory]
    [InlineData("not.a.path")]
    [InlineData("bands.x.elements.0")]
    [InlineData("bands.0.elements.x")]
    public void MoveElement_InvalidPathFormat_Throws(string path)
    {
        Assert.Throws<FormatException>(() => YamlReportRewriter.MoveElement(BasicYaml, path, 0, 0));
    }

    // === ApplyPatch ===

    [Fact]
    public void ApplyPatch_UpdatesBounds()
    {
        var patch = new ElementPatch { X = 50, Y = 15, Width = 300, Height = 25 };
        var result = YamlReportRewriter.ApplyPatch(BasicYaml, "bands.0.elements.0", patch);
        var el = Parse(result).Bands![0].Elements![0];
        Assert.Equal(50, el.Bounds!.X);
        Assert.Equal(15, el.Bounds.Y);
        Assert.Equal(300, el.Bounds.Width);
        Assert.Equal(25, el.Bounds.Height);
    }

    [Fact]
    public void ApplyPatch_TextContentAndStyle()
    {
        var patch = new ElementPatch { Content = "Nuevo", Style = "Title" };
        var result = YamlReportRewriter.ApplyPatch(BasicYaml, "bands.0.elements.0", patch);
        var el = Parse(result).Bands![0].Elements![0];
        Assert.Equal("Nuevo", el.Content);
        Assert.Equal("Title", el.Style);
    }

    [Fact]
    public void ApplyPatch_ContentOnLine_Throws()
    {
        var patch = new ElementPatch { Content = "nope" };
        Assert.Throws<InvalidOperationException>(() =>
            YamlReportRewriter.ApplyPatch(BasicYaml, "bands.0.elements.1", patch));
    }

    [Fact]
    public void ApplyPatch_LineColorAndThickness()
    {
        var patch = new ElementPatch { Color = "#ff0000", Thickness = 2 };
        var result = YamlReportRewriter.ApplyPatch(BasicYaml, "bands.0.elements.1", patch);
        var el = Parse(result).Bands![0].Elements![1];
        Assert.Equal("#ff0000", el.Color);
        Assert.Equal(2, el.Thickness);
    }

    [Fact]
    public void ApplyPatch_TableRowsAndHeaderMode()
    {
        var patch = new ElementPatch { Rows = "$.records", HeaderMode = "PrintOnce", RowHeight = 22 };
        var result = YamlReportRewriter.ApplyPatch(BasicYaml, "bands.1.elements.0", patch);
        var el = Parse(result).Bands![1].Elements![0];
        Assert.Equal("$.records", el.Rows);
        Assert.Equal("PrintOnce", el.HeaderMode);
        Assert.Equal(22, el.RowHeight);
    }

    [Fact]
    public void ApplyPatch_TableFieldOnText_Throws()
    {
        var patch = new ElementPatch { Rows = "$.x" };
        Assert.Throws<InvalidOperationException>(() =>
            YamlReportRewriter.ApplyPatch(BasicYaml, "bands.0.elements.0", patch));
    }

    [Fact]
    public void ApplyPatch_ResizeTable_RedistributesColumnWidths()
    {
        var patch = new ElementPatch { Width = 600 };  // originalmente 300
        var result = YamlReportRewriter.ApplyPatch(BasicYaml, "bands.1.elements.0", patch);
        var el = Parse(result).Bands![1].Elements![0];
        Assert.Equal(600, el.Bounds!.Width);
        Assert.Equal(3, el.Columns!.Count);
        // Cada columna era 100, factor=2, ahora 200 cada una.
        Assert.All(el.Columns, c => Assert.Equal(200, c.Width));
    }

    // === AddElement ===

    [Theory]
    [InlineData("text")]
    [InlineData("line")]
    [InlineData("rectangle")]
    [InlineData("table")]
    public void AddElement_CreatesWithDefaults(string kind)
    {
        var (yaml, newPath) = YamlReportRewriter.AddElement(BasicYaml, 0, kind, 50, 30);
        Assert.Equal("bands.0.elements.2", newPath);
        var el = Parse(yaml).Bands![0].Elements![2];
        Assert.Equal(kind, el.Type);
        Assert.Equal(50, el.Bounds!.X);
        Assert.Equal(30, el.Bounds.Y);
    }

    [Fact]
    public void AddElement_Table_HasTwoDefaultColumns()
    {
        var (yaml, _) = YamlReportRewriter.AddElement(BasicYaml, 0, "table", 0, 0);
        var el = Parse(yaml).Bands![0].Elements![2];
        Assert.Equal("table", el.Type);
        Assert.Equal(2, el.Columns!.Count);
        Assert.Equal("$.items", el.Rows);
    }

    [Fact]
    public void AddElement_UnknownKind_Throws()
    {
        Assert.Throws<FormatException>(() =>
            YamlReportRewriter.AddElement(BasicYaml, 0, "unknown", 0, 0));
    }

    // === DeleteElement ===

    [Fact]
    public void DeleteElement_RemovesAndShiftsIndices()
    {
        var result = YamlReportRewriter.DeleteElement(BasicYaml, "bands.0.elements.0");
        var band = Parse(result).Bands![0];
        Assert.Single(band.Elements!);
        Assert.Equal("line", band.Elements![0].Type);
    }

    [Fact]
    public void DeleteElement_InvalidIndex_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            YamlReportRewriter.DeleteElement(BasicYaml, "bands.0.elements.99"));
    }

    // === DuplicateElement ===

    [Fact]
    public void DuplicateElement_InsertsAfterWithOffset()
    {
        var (yaml, newPath) = YamlReportRewriter.DuplicateElement(BasicYaml, "bands.0.elements.0");
        Assert.Equal("bands.0.elements.1", newPath);
        var model = Parse(yaml);
        var elements = model.Bands![0].Elements!;
        Assert.Equal(3, elements.Count);           // original + clone + line
        var orig = elements[0];
        var clone = elements[1];
        Assert.Equal(orig.Content, clone.Content);
        Assert.Equal(orig.Bounds!.X + 10, clone.Bounds!.X);
        Assert.Equal(orig.Bounds.Y + 10, clone.Bounds.Y);
    }

    // === Bands ===

    [Fact]
    public void AddBand_AppendsWithDefaults()
    {
        var (yaml, idx) = YamlReportRewriter.AddBand(BasicYaml, "PageFooter", 25);
        Assert.Equal(2, idx);
        var model = Parse(yaml);
        Assert.Equal(3, model.Bands!.Count);
        Assert.Equal("PageFooter", model.Bands[2].Kind);
        Assert.Equal(25, model.Bands[2].Height);
    }

    [Fact]
    public void AddBand_UnknownKind_Throws()
    {
        Assert.Throws<FormatException>(() =>
            YamlReportRewriter.AddBand(BasicYaml, "Footer", 40));
    }

    [Fact]
    public void AddBand_LowercaseKind_Normalizes()
    {
        var (yaml, _) = YamlReportRewriter.AddBand(BasicYaml, "pagefooter", 25);
        Assert.Equal("PageFooter", Parse(yaml).Bands![2].Kind);
    }

    [Fact]
    public void DeleteBand_Removes()
    {
        var result = YamlReportRewriter.DeleteBand(BasicYaml, 1);
        var model = Parse(result);
        Assert.Single(model.Bands!);
        Assert.Equal("ReportHeader", model.Bands![0].Kind);
    }

    [Fact]
    public void DeleteBand_InvalidIndex_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            YamlReportRewriter.DeleteBand(BasicYaml, 99));
    }

    [Fact]
    public void MoveBand_SwapsOrder()
    {
        var result = YamlReportRewriter.MoveBand(BasicYaml, 0, 1);
        var model = Parse(result);
        Assert.Equal("Detail", model.Bands![0].Kind);
        Assert.Equal("ReportHeader", model.Bands[1].Kind);
    }

    [Fact]
    public void MoveBand_SameIndex_NoOp()
    {
        var result = YamlReportRewriter.MoveBand(BasicYaml, 1, 1);
        var model = Parse(result);
        Assert.Equal("ReportHeader", model.Bands![0].Kind);
        Assert.Equal("Detail", model.Bands[1].Kind);
    }

    // === UpdateColumns ===

    [Fact]
    public void UpdateColumns_ReplacesAndAdjustsBounds()
    {
        var newCols = new[]
        {
            new TableColumnYaml { Header = "X", Binding = "$.x", Width = 50, Align = "left" },
            new TableColumnYaml { Header = "Y", Binding = "$.y", Width = 150, Align = "right" }
        };
        var result = YamlReportRewriter.UpdateColumns(BasicYaml, "bands.1.elements.0", newCols);
        var el = Parse(result).Bands![1].Elements![0];
        Assert.Equal(2, el.Columns!.Count);
        Assert.Equal("X", el.Columns[0].Header);
        Assert.Equal(200, el.Bounds!.Width);     // 50 + 150
    }

    [Fact]
    public void UpdateColumns_OnNonTable_Throws()
    {
        var cols = new[] { new TableColumnYaml { Header = "x", Binding = "$.x", Width = 50 } };
        Assert.Throws<InvalidOperationException>(() =>
            YamlReportRewriter.UpdateColumns(BasicYaml, "bands.0.elements.0", cols));
    }

    [Fact]
    public void UpdateColumns_ColumnMissingBinding_Throws()
    {
        var cols = new[] { new TableColumnYaml { Header = "X", Binding = null, Width = 50 } };
        Assert.Throws<ArgumentException>(() =>
            YamlReportRewriter.UpdateColumns(BasicYaml, "bands.1.elements.0", cols));
    }

    [Fact]
    public void UpdateColumns_ZeroWidth_Throws()
    {
        var cols = new[] { new TableColumnYaml { Header = "X", Binding = "$.x", Width = 0 } };
        Assert.Throws<ArgumentException>(() =>
            YamlReportRewriter.UpdateColumns(BasicYaml, "bands.1.elements.0", cols));
    }
}
