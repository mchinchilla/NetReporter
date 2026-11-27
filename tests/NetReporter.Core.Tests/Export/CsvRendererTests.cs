using System.Globalization;
using System.Text;
using NetReporter.Core.Bands;
using NetReporter.Core.DataBinding;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Expressions;
using NetReporter.Core.Export;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Tests.Export;

public sealed class CsvRendererTests
{
    private sealed record Item(string Cat, string Code, decimal Total, DateTime When);

    private static readonly Item[] Rows =
    [
        new("Computadoras", "A001", 1234.5m, new DateTime(2026, 11, 3, 8, 30, 0)),
        new("Computadoras", "=SUM(A1)", 200m, new DateTime(2026, 11, 4)),
        new("Periféricos, varios", "+15551234", -3.25m, new DateTime(2026, 11, 5)),
    ];

    private static TableElement<Item> Table(string firstHeader = "Categoría", GroupFooter? summary = null) => new()
    {
        Bounds = new Rect(0, 0, 400, 0),
        Rows = Rows,
        Columns =
        [
            new TableColumn<Item>(firstHeader, Bind.From<Item, object?>(i => i.Cat), 100),
            new TableColumn<Item>("Código", Bind.From<Item, object?>(i => i.Code), 100),
            new TableColumn<Item>("Total", Bind.From<Item, object?>(i => i.Total), 100, "N2"),
            new TableColumn<Item>("Fecha \"local\"", Bind.From<Item, object?>(i => i.When), 100, "d"),
        ],
        GroupBy = Bind.From<Item, object?>(i => i.Cat),
        GroupHeader = new GroupHeader { Height = 14, Content = Expr.Of(ctx => $"Grupo {ctx.GroupKey}") },
        Summary = summary,
    };

    private static ReportDefinition Report(params Band[] bands) => new()
    {
        Name = "Inventario",
        Page = PageSetup.Letter,
        Styles = new StyleSheetBuilder().Add("Default", s => s.FontFamily("Helvetica")).Build(),
        Bands = bands,
        Culture = CultureInfo.GetCultureInfo("es-HN"),
    };

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    [Fact]
    public void FirstTable_RawInvariantValues_QuotedAndFormulaSafe()
    {
        var report = Report(
            new ReportHeaderBand { Height = 20, Elements = [new TextElement { Bounds = new Rect(0, 0, 100, 12), Content = Expr.Str("Título") }] },
            new DetailBand { Height = 0, Elements = [Table()] },
            new DetailBand { Height = 0, Elements = [Table("Otra tabla")] });

        var bytes = new CsvRenderer().Render(report);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var lines = Text(bytes[3..]).Split("\r\n");
        Assert.Equal(
        [
            "Categoría,Código,Total,\"Fecha \"\"local\"\"\"",
            "Computadoras,A001,1234.5,2026-11-03 08:30:00",
            "Computadoras,'=SUM(A1),200,2026-11-04",
            "\"Periféricos, varios\",+15551234,-3.25,2026-11-05",
            "",
        ], lines);
    }

    [Fact]
    public void Summary_IsTheLastLine_AggregatesRawAndContentAsIs()
    {
        var summary = new GroupFooter
        {
            Height = 14,
            Cells =
            [
                new GroupFooterCell { Content = Expr.Of(ctx => $"Total ({ctx.GroupRowCount})") },
                null,
                new GroupFooterCell { Aggregate = AggregateKind.Sum },
            ],
        };
        var report = Report(new DetailBand { Height = 0, Elements = [Table(summary: summary)] });

        var lines = Text(new CsvRenderer().Render(report, new CsvRenderOptions { ByteOrderMark = false })).Split("\r\n");

        Assert.Equal("Total (3),,1431.25,", lines[^2]);
        Assert.Equal(6, lines.Length);
        var without = Text(new CsvRenderer().Render(report, new CsvRenderOptions { ByteOrderMark = false, IncludeSummary = false })).Split("\r\n");
        Assert.Equal(5, without.Length);
    }

    [Fact]
    public void HiddenBandsAreSkipped_AndAReportWithoutTablesHasNothing()
    {
        var hidden = new DetailBand { Height = 0, Visible = Expr.Of(_ => false), Elements = [Table("Oculta")] };
        var report = Report(hidden, new DetailBand { Height = 0, Elements = [Table()] });
        var options = new CsvRenderOptions { ByteOrderMark = false, Delimiter = ';' };

        Assert.StartsWith("Categoría;Código;Total;", Text(new CsvRenderer().Render(report, options)));
        Assert.True(CsvRenderer.HasTable(report));

        var empty = Report(new DetailBand { Height = 10, Elements = [new TextElement { Bounds = new Rect(0, 0, 10, 10), Content = Expr.Str("x") }] });
        Assert.False(CsvRenderer.HasTable(empty));
        Assert.Empty(new CsvRenderer().Render(empty, options));
    }
}
