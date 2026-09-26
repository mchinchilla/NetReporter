using NetReporter.Core.Bands;
using NetReporter.Core.DataBinding;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Expressions;
using NetReporter.Core.Layout;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Tests.Layout;

public sealed class NestedGroupTests
{
    private sealed record Sale(string Region, string Category, string Product, double Amount);

    private static readonly StyleSheet Styles = new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Helvetica").FontSize(10))
        .Add("TableHeader", s => s.BasedOn("Default").Bold())
        .Add("TableRow", s => s.BasedOn("Default"))
        .Add("GroupHeader", s => s.BasedOn("Default").Bold())
        .Add("GroupFooter", s => s.BasedOn("Default").Bold())
        .Add("TableSummary", s => s.BasedOn("Default").Bold())
        .Build();

    private static readonly Sale[] Rows =
    [
        new("Norte", "Hardware", "Laptop", 1000),
        new("Norte", "Hardware", "Mouse", 50),
        new("Norte", "Software", "Office", 300),
        new("Sur", "Hardware", "Monitor", 400),
        new("Sur", "Software", "Antivirus", 100),
        new("Sur", "Software", "Backup", 200),
    ];

    private static GroupFooter Footer(string label, AggregateKind kind = AggregateKind.Sum) => new()
    {
        Height = 16,
        Cells = new GroupFooterCell?[]
        {
            new() { Content = Expr.Of(ctx => $"{label} {ctx.GroupKey}") },
            new() { Aggregate = kind, Format = "N0" }
        }
    };

    private static TableElement<Sale> Table(GroupFooter? summary = null) => new()
    {
        Bounds = new Rect(0, 0, 300, 0),
        Rows = Rows,
        Columns =
        [
            new TableColumn<Sale>("Producto", Bind.From<Sale, object?>(s => s.Product), 200),
            new TableColumn<Sale>("Monto", Bind.From<Sale, object?>(s => s.Amount), 100, "N0")
        ],
        Groups =
        [
            new TableGroupLevel<Sale>
            {
                By = Bind.From<Sale, object?>(s => s.Region),
                Header = new GroupHeader { Height = 18, Content = Expr.Of(ctx => $"Región {ctx.GroupKey} ({ctx.GroupRowCount})") },
                Footer = Footer("Total región")
            },
            new TableGroupLevel<Sale>
            {
                By = Bind.From<Sale, object?>(s => s.Category),
                Header = new GroupHeader
                {
                    Height = 16,
                    Content = Expr.Of(ctx => $"{ctx.GroupKeys[0]} / {ctx.GroupKey} ({ctx.GroupRowCount})")
                },
                Footer = Footer("Subtotal")
            }
        ],
        Summary = summary
    };

    private static List<string> Texts(ReportDefinition report) =>
        new LayoutEngine().Layout(report).Pages.SelectMany(p => p.Commands)
            .OfType<DrawTextCommand>().Select(c => c.Text).ToList();

    private static ReportDefinition Report(ReportElement table) => new()
    {
        Name = "Nested",
        Page = PageSetup.Letter.WithMargins(new Thickness(20)),
        Styles = Styles,
        Bands = [new DetailBand { Height = 0, AutoHeight = true, Elements = [table] }]
    };

    [Fact]
    public void TwoLevels_EmitHeadersInOuterThenInnerOrder()
    {
        var texts = Texts(Report(Table()));
        var headers = texts.Where(t => t.StartsWith("Región") || t.Contains(" / ")).ToList();

        Assert.Equal(new[]
        {
            "Región Norte (3)", "Norte / Hardware (2)", "Norte / Software (1)",
            "Región Sur (3)", "Sur / Hardware (1)", "Sur / Software (2)"
        }, headers);
    }

    [Fact]
    public void Footers_AggregateTheirOwnLevel()
    {
        var texts = Texts(Report(Table()));

        // Subtotales por categoría dentro de cada región.
        var norteHardware = texts.IndexOf("Subtotal Hardware");
        Assert.True(norteHardware >= 0);
        Assert.Equal("1,050", texts[norteHardware + 1]);

        // Totales de región después de sus subtotales, con el contexto del nivel externo restaurado.
        var norte = texts.IndexOf("Total región Norte");
        var sur = texts.IndexOf("Total región Sur");
        Assert.Equal("1,350", texts[norte + 1]);
        Assert.Equal("700", texts[sur + 1]);
        Assert.True(norte > norteHardware);
    }

    [Fact]
    public void Summary_AggregatesAllRows_AtTheEnd()
    {
        var summary = new GroupFooter
        {
            Height = 18,
            Style = new StyleRef("TableSummary"),
            Cells = new GroupFooterCell?[]
            {
                new() { Content = Expr.Of(ctx => $"Total general ({ctx.GroupRowCount})") },
                new() { Aggregate = AggregateKind.Sum, Format = "N0" }
            }
        };
        var texts = Texts(Report(Table(summary)));

        Assert.Equal("Total general (6)", texts[^2]);
        Assert.Equal("2,050", texts[^1]);
    }

    [Theory]
    [InlineData(AggregateKind.Min, "50")]
    [InlineData(AggregateKind.Max, "1,000")]
    [InlineData(AggregateKind.Avg, "525")]
    public void MinMaxAvg_Aggregates(AggregateKind kind, string expectedNorteHardware)
    {
        var table = Table() with
        {
            Groups = [ new TableGroupLevel<Sale>
            {
                By = Bind.From<Sale, object?>(s => s.Region + "/" + s.Category),
                Footer = Footer("Agg", kind)
            }]
        };
        var texts = Texts(Report(table));
        Assert.Equal(expectedNorteHardware, texts[texts.IndexOf("Agg Norte/Hardware") + 1]);
    }

    [Fact]
    public void LegacyGroupBy_StillMapsToSingleLevel()
    {
        var table = Table() with
        {
            Groups = null,
            GroupBy = Bind.From<Sale, object?>(s => s.Region),
            GroupHeader = new GroupHeader { Height = 16, Content = Expr.Of(ctx => $"G:{ctx.GroupKey}") }
        };
        Assert.Single(table.EffectiveGroups);
        var texts = Texts(Report(table));
        Assert.Equal(new[] { "G:Norte", "G:Sur" }, texts.Where(t => t.StartsWith("G:")));
    }

    [Fact]
    public void GroupHeader_IsNeverOrphanedAtPageBottom()
    {
        // Página pequeña: el header de "Sur" caería justo al final; keep-with-next lo empuja con su 1ª fila.
        var many = Enumerable.Range(0, 40).Select(i => new Sale(i < 20 ? "A" : "B", "X", $"P{i}", i)).ToArray();
        var table = new TableElement<Sale>
        {
            Bounds = new Rect(0, 0, 300, 0),
            Rows = many,
            RowHeight = 18,
            Columns = [new TableColumn<Sale>("Producto", Bind.From<Sale, object?>(s => s.Product), 300)],
            Groups = [new TableGroupLevel<Sale>
            {
                By = Bind.From<Sale, object?>(s => s.Region),
                Header = new GroupHeader { Height = 18, Content = Expr.Of(ctx => $"Grupo {ctx.GroupKey}") }
            }]
        };
        var report = Report(table) with { Page = new PageSetup(new Size(400, 300), new Thickness(20)) };
        var rl = new LayoutEngine().Layout(report);

        foreach (var page in rl.Pages)
        {
            var texts = page.Commands.OfType<DrawTextCommand>().ToList();
            var last = texts[^1];
            Assert.False(last.Text.StartsWith("Grupo"), $"Header huérfano al pie de la página {page.PageNumber}");
        }
    }
}
