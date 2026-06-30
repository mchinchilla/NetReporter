using NetReporter.Core.Bands;
using NetReporter.Core.DataBinding;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Layout;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Tests.Layout;

public sealed class TableOuterBorderTests
{
    private sealed record Row(string Name, decimal Amount);

    private static readonly StyleSheet Styles = new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Helvetica").FontSize(10))
        .Add("TableHeader", s => s.BasedOn("Default").Bold().Border(BorderSet.OnlyBottom(1, Color.Black)))
        .Add("TableRow", s => s.BasedOn("Default"))
        .Build();

    private static TableElement<Row> Table(BorderLine? outer, double radius) => new()
    {
        Bounds = new Rect(0, 0, 300, 0),
        Rows = new[] { new Row("A", 1m), new Row("B", 2m) },
        HeaderHeight = 20,
        RowHeight = 16,
        HeaderStyle = new StyleRef("TableHeader"),
        RowStyle = new StyleRef("TableRow"),
        OuterBorder = outer,
        CornerRadius = radius,
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

    [Fact]
    public void Table_WithOuterBorder_DrawsOneRoundedBorderAroundTable()
    {
        var border = new BorderLine(0.75, Color.FromHex("#D1D5DB"));
        var rl = new LayoutEngine().Layout(Report(Table(border, 6)));
        var rounded = rl.Pages.SelectMany(p => p.Commands)
            .OfType<DrawRectangleCommand>()
            .Where(r => r.CornerRadius > 0)
            .ToList();

        Assert.Single(rounded);
        var box = rounded[0];
        Assert.Equal(6, box.CornerRadius, 3);
        Assert.Null(box.Fill);
        Assert.NotNull(box.Border);
        Assert.Equal(300, box.Bounds.Width, 1);
        // header (20) + 2 rows (16 each) = 52pt tall
        Assert.Equal(52, box.Bounds.Height, 1);
    }

    [Fact]
    public void Table_WithoutOuterBorder_DrawsNoRoundedRect()
    {
        var rl = new LayoutEngine().Layout(Report(Table(null, 0)));
        var rounded = rl.Pages.SelectMany(p => p.Commands)
            .OfType<DrawRectangleCommand>()
            .Where(r => r.CornerRadius > 0)
            .ToList();

        Assert.Empty(rounded);
    }
}
