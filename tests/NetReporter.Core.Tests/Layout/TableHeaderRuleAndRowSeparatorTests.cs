using NetReporter.Core.Bands;
using NetReporter.Core.DataBinding;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Layout;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Tests.Layout;

/// <summary>Covers the opt-in <see cref="TableElement{TRow}.HeaderRule"/> (a single full-width line
/// under the header, no per-cell box) and <see cref="TableElement{TRow}.RowSeparator"/> (a hairline at
/// the bottom of each data row) — the Northwind-style "clean financial table" treatment.</summary>
public sealed class TableHeaderRuleAndRowSeparatorTests
{
    private sealed record Row(string Name, decimal Amount);

    private static readonly StyleSheet Styles = new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Helvetica").FontSize(10))
        .Add("TableHeader", s => s.BasedOn("Default").Bold())   // no border → no box
        .Add("TableRow", s => s.BasedOn("Default"))
        .Build();

    private static TableElement<Row> Table(BorderLine? headerRule, BorderLine? rowSeparator) => new()
    {
        Bounds = new Rect(0, 0, 300, 0),
        Rows = new[] { new Row("A", 1m), new Row("B", 2m), new Row("C", 3m) },
        HeaderHeight = 20,
        RowHeight = 16,
        HeaderStyle = new StyleRef("TableHeader"),
        RowStyle = new StyleRef("TableRow"),
        HeaderRule = headerRule,
        RowSeparator = rowSeparator,
        Columns = new[]
        {
            new TableColumn<Row>("Name", Bind.From<Row, object?>(r => r.Name), Width: 200),
            new TableColumn<Row>("Amount", Bind.From<Row, object?>(r => r.Amount), Width: 100, Format: "N2"),
        }
    };

    private static ReportDefinition Report(TableElement<Row> table) => new()
    {
        Name = "Test",
        Page = PageSetup.Letter.WithMargins(new Thickness(0)),
        Styles = Styles,
        Bands = new Band[] { new DetailBand { Height = 0, AutoHeight = true, Elements = new[] { (ReportElement)table } } }
    };

    private static List<DrawLineCommand> Lines(ReportDefinition report) =>
        new LayoutEngine().Layout(report).Pages.SelectMany(p => p.Commands).OfType<DrawLineCommand>().ToList();

    [Fact]
    public void HeaderRule_DrawsOneFullWidthLineAtHeaderBottom()
    {
        var lines = Lines(Report(Table(new BorderLine(1, Color.FromHex("#1D4ED8")), null)));
        // Exactly one horizontal line, full table width, at the header's bottom edge (y = HeaderHeight = 20).
        var rule = Assert.Single(lines, l => l.From.Y == 20 && l.To.Y == 20);
        Assert.Equal(0, rule.From.X, 1);
        Assert.Equal(300, rule.To.X, 1);
        Assert.Equal("#1D4ED8", rule.Color.ToHexString(), ignoreCase: true);
    }

    [Fact]
    public void RowSeparator_DrawsOneHairlinePerRow()
    {
        var sep = new BorderLine(0.5, Color.FromHex("#CBD5E1"));
        var lines = Lines(Report(Table(null, sep)));
        // 3 rows → 3 separators, each full width, at the bottom edge of its row.
        // Header 20, rows 16 each → separators at y = 36, 52, 68.
        var seps = lines.Where(l => l.From.Y == l.To.Y).OrderBy(l => l.From.Y).ToList();
        Assert.Equal(3, seps.Count);
        Assert.Equal(new[] { 36d, 52d, 68d }, seps.Select(l => l.From.Y));
        Assert.All(seps, l => Assert.Equal(300, l.To.X - l.From.X, 1));
    }

    [Fact]
    public void NoRules_DrawsNoLines()
    {
        Assert.Empty(Lines(Report(Table(null, null))));
    }
}
