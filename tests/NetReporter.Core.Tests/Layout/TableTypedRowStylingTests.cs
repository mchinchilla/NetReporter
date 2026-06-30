using NetReporter.Core.Bands;
using NetReporter.Core.DataBinding;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Layout;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Tests.Layout;

/// <summary>Covers the "typed rows" features used by financial statements:
/// <see cref="TableElement{TRow}.RowStyleSelector"/> + <see cref="TableElement{TRow}.RowStyleMap"/>
/// (per-row style by kind), <see cref="TableElement{TRow}.FullRowBackground"/> (edge-to-edge band),
/// per-column <see cref="TableColumn{TRow}.Style"/> (mono/muted columns), and the header text following
/// each column's alignment.</summary>
public sealed class TableTypedRowStylingTests
{
    private sealed record Row(string Kind, string Code, string Name, string Amount);

    private static readonly Color Band = Color.FromHex("#DBEAFE");
    private static readonly Color Mono = Color.FromHex("#94A3B8");

    private static readonly StyleSheet Styles = new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Helvetica").FontSize(10))
        .Add("TableHeader", s => s.BasedOn("Default").Bold())
        .Add("AccountRow", s => s.BasedOn("Default"))
        .Add("TotalRow", s => s.BasedOn("Default").Bold().Background(Band))
        .Add("CodeCell", s => s.BasedOn("Default").FontFamily("Courier").Foreground(Mono))
        .Build();

    private static TableElement<Row> Table(bool fullRowBg, StyleRef? codeColStyle = null) => new()
    {
        Bounds = new Rect(0, 0, 300, 0),
        Rows = new[]
        {
            new Row("account", "1101", "Caja", "100.00"),
            new Row("total", "", "TOTAL", "100.00"),
        },
        HeaderHeight = 20,
        RowHeight = 18,
        HeaderStyle = new StyleRef("TableHeader"),
        RowStyle = new StyleRef("AccountRow"),
        FullRowBackground = fullRowBg,
        RowStyleSelector = Bind.From<Row, object?>(r => r.Kind),
        RowStyleMap = new Dictionary<string, StyleRef>
        {
            ["account"] = new StyleRef("AccountRow"),
            ["total"] = new StyleRef("TotalRow"),
        },
        Columns = new[]
        {
            new TableColumn<Row>("Código", Bind.From<Row, object?>(r => r.Code), Width: 80, Align: TextAlignment.Left, Style: codeColStyle),
            new TableColumn<Row>("Cuenta", Bind.From<Row, object?>(r => r.Name), Width: 140),
            new TableColumn<Row>("Importe", Bind.From<Row, object?>(r => r.Amount), Width: 80, Align: TextAlignment.Right),
        }
    };

    private static ReportDefinition Report(TableElement<Row> table) => new()
    {
        Name = "Test",
        Page = PageSetup.Letter.WithMargins(new Thickness(0)),
        Styles = Styles,
        Bands = new Band[] { new DetailBand { Height = 0, AutoHeight = true, Elements = new[] { (ReportElement)table } } }
    };

    private static List<DrawRectangleCommand> Rects(ReportDefinition report) =>
        new LayoutEngine().Layout(report).Pages.SelectMany(p => p.Commands).OfType<DrawRectangleCommand>().ToList();

    private static List<DrawTextCommand> Texts(ReportDefinition report) =>
        new LayoutEngine().Layout(report).Pages.SelectMany(p => p.Commands).OfType<DrawTextCommand>().ToList();

    [Fact]
    public void TotalRow_GetsBandBackground_AccountRow_DoesNot()
    {
        var bandRects = Rects(Report(Table(fullRowBg: true))).Where(r => r.Fill == Band).ToList();
        // The "total" row paints one band; the "account" row has no background.
        Assert.Single(bandRects);
    }

    [Fact]
    public void FullRowBackground_PaintsOneWideBand_NotPerCell()
    {
        var band = Rects(Report(Table(fullRowBg: true))).Single(r => r.Fill == Band);
        Assert.Equal(300, band.Bounds.Width, 1);   // full table width, one rect

        // Per-cell mode: the same band is painted as 3 column-width rects instead of one.
        var perCell = Rects(Report(Table(fullRowBg: false))).Where(r => r.Fill == Band).ToList();
        Assert.Equal(3, perCell.Count);
        Assert.All(perCell, r => Assert.True(r.Bounds.Width < 300));
    }

    [Fact]
    public void UnmappedFallback_UsesRowStyle_NoBand()
    {
        // A row whose kind isn't in the map falls back to the plain row style (no band).
        var t = Table(fullRowBg: true) with
        {
            Rows = new[] { new Row("mystery", "9", "X", "1.00") },
        };
        Assert.DoesNotContain(Rects(Report(t)), r => r.Fill == Band);
    }

    [Fact]
    public void ColumnStyle_AppliesMonoFont_OnNonBandedRow()
    {
        var texts = Texts(Report(Table(fullRowBg: true, codeColStyle: new StyleRef("CodeCell"))));
        // The account-row "1101" cell uses the column's Courier font + muted color.
        var codeCell = texts.First(t => t.Text == "1101");
        Assert.Equal("Courier", codeCell.Style.FontFamily);
        Assert.Equal(Mono, codeCell.Style.Foreground);
    }

    [Fact]
    public void HeaderText_FollowsColumnAlignment()
    {
        var texts = Texts(Report(Table(fullRowBg: true)));
        Assert.Equal(TextAlignment.Left,  texts.First(t => t.Text == "Código").Style.TextAlign);
        Assert.Equal(TextAlignment.Right, texts.First(t => t.Text == "Importe").Style.TextAlign);
    }
}
