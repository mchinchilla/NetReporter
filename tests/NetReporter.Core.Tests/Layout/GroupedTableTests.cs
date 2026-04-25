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

public sealed class GroupedTableTests
{
    private sealed record Item(string Cat, string Code, double Total);

    private static readonly StyleSheet Styles = new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Helvetica").FontSize(10))
        .Add("TableHeader", s => s.BasedOn("Default").Bold())
        .Add("TableRow", s => s.BasedOn("Default"))
        .Add("GroupHeader", s => s.BasedOn("Default").Bold().Background(Color.FromHex("#0284C7")))
        .Add("GroupFooter", s => s.BasedOn("Default").Bold().Background(Color.FromHex("#E0F2FE")))
        .Build();

    private static IReadOnlyList<Item> SampleRows => new[]
    {
        new Item("Computadoras", "A001", 100.0),
        new Item("Computadoras", "A002", 200.0),
        new Item("Periféricos",  "B001", 50.0),
        new Item("Periféricos",  "B002", 75.0),
        new Item("Periféricos",  "B003", 25.0),
        new Item("Cables",       "C001", 10.0)
    };

    private static TableElement<Item> BuildTable(GroupHeader? header, GroupFooter? footer) => new()
    {
        Bounds = new Rect(0, 0, 300, 0),
        Rows = SampleRows,
        Columns = new[]
        {
            new TableColumn<Item>("Cat",   Bind.From<Item, object?>(i => i.Cat),   100),
            new TableColumn<Item>("Code",  Bind.From<Item, object?>(i => i.Code),  100),
            new TableColumn<Item>("Total", Bind.From<Item, object?>(i => i.Total), 100, "N2")
        },
        GroupBy = Bind.From<Item, object?>(i => i.Cat),
        GroupHeader = header,
        GroupFooter = footer
    };

    private static ReportDefinition BuildReport(TableElement<Item> table) => new()
    {
        Name = "Test",
        Page = PageSetup.Letter.WithMargins(new Thickness(20)),
        Styles = Styles,
        Bands = new Band[]
        {
            new DetailBand
            {
                Height = 0,
                AutoHeight = true,
                Elements = new ReportElement[] { table }
            }
        }
    };

    [Fact]
    public void GroupedTable_EmitsHeaderPerGroup()
    {
        var header = new GroupHeader
        {
            Height = 16,
            Content = Expr.Of(ctx => $"Categoría: {ctx.GroupKey}")
        };
        var report = BuildReport(BuildTable(header, footer: null));
        var rl = new LayoutEngine().Layout(report);

        var headerTexts = rl.Pages.SelectMany(p => p.Commands).OfType<DrawTextCommand>()
            .Where(c => c.Text.StartsWith("Categoría:"))
            .Select(c => c.Text).ToList();

        Assert.Equal(3, headerTexts.Count);
        Assert.Contains("Categoría: Computadoras", headerTexts);
        Assert.Contains("Categoría: Periféricos", headerTexts);
        Assert.Contains("Categoría: Cables", headerTexts);
    }

    [Fact]
    public void GroupedTable_GroupHeaderCount_MatchesGroupSize()
    {
        var header = new GroupHeader
        {
            Height = 16,
            Content = Expr.Of(ctx => $"{ctx.GroupKey}={ctx.GroupRowCount}")
        };
        var report = BuildReport(BuildTable(header, footer: null));
        var rl = new LayoutEngine().Layout(report);

        var headerTexts = rl.Pages.SelectMany(p => p.Commands).OfType<DrawTextCommand>()
            .Select(c => c.Text).ToList();

        Assert.Contains("Computadoras=2", headerTexts);
        Assert.Contains("Periféricos=3", headerTexts);
        Assert.Contains("Cables=1", headerTexts);
    }

    [Fact]
    public void GroupedTable_SumFooter_IsCorrect()
    {
        var footer = new GroupFooter
        {
            Height = 16,
            Cells = new GroupFooterCell?[]
            {
                new GroupFooterCell { Content = Expr.Of(ctx => $"Sub {ctx.GroupKey}:") },
                null,
                new GroupFooterCell { Aggregate = AggregateKind.Sum, Format = "N2" }
            }
        };
        var report = BuildReport(BuildTable(header: null, footer));
        var rl = new LayoutEngine().Layout(report);

        var texts = rl.Pages.SelectMany(p => p.Commands).OfType<DrawTextCommand>()
            .Select(c => c.Text).ToList();

        // Computadoras: 100 + 200 = 300.00
        Assert.Contains("300.00", texts);
        // Periféricos: 50 + 75 + 25 = 150.00
        Assert.Contains("150.00", texts);
        // Cables: 10
        Assert.Contains("10.00", texts);
    }

    [Fact]
    public void GroupedTable_CountFooter_IsCorrect()
    {
        var footer = new GroupFooter
        {
            Height = 16,
            Cells = new GroupFooterCell?[]
            {
                new GroupFooterCell { Aggregate = AggregateKind.Count, Format = "N0" }
            }
        };
        var report = BuildReport(BuildTable(header: null, footer));
        var rl = new LayoutEngine().Layout(report);

        var texts = rl.Pages.SelectMany(p => p.Commands).OfType<DrawTextCommand>()
            .Select(c => c.Text).ToList();

        // 3 groups → 3 footers; counts: 2, 3, 1.
        Assert.Contains("2", texts);
        Assert.Contains("3", texts);
        Assert.Contains("1", texts);
    }

    [Fact]
    public void GroupedTable_AvgFooter_IsCorrect()
    {
        var footer = new GroupFooter
        {
            Height = 16,
            Cells = new GroupFooterCell?[]
            {
                null, null,
                new GroupFooterCell { Aggregate = AggregateKind.Avg, Format = "N2" }
            }
        };
        var report = BuildReport(BuildTable(header: null, footer));
        var rl = new LayoutEngine().Layout(report);

        var texts = rl.Pages.SelectMany(p => p.Commands).OfType<DrawTextCommand>()
            .Select(c => c.Text).ToList();

        // Computadoras avg: (100+200)/2 = 150.00
        Assert.Contains("150.00", texts);
        // Periféricos avg: (50+75+25)/3 = 50.00
        Assert.Contains("50.00", texts);
    }

    [Fact]
    public void TableWithoutGroupBy_DoesNotEmitGroupBands()
    {
        var report = new ReportDefinition
        {
            Name = "NoGroup",
            Page = PageSetup.Letter.WithMargins(new Thickness(20)),
            Styles = Styles,
            Bands = new Band[]
            {
                new DetailBand
                {
                    Height = 0,
                    Elements = new ReportElement[]
                    {
                        new TableElement<Item>
                        {
                            Bounds = new Rect(0, 0, 300, 0),
                            Rows = SampleRows,
                            Columns = new[]
                            {
                                new TableColumn<Item>("Cat", Bind.From<Item, object?>(i => i.Cat), 100)
                            }
                            // no GroupBy / GroupHeader / GroupFooter
                        }
                    }
                }
            }
        };

        var rl = new LayoutEngine().Layout(report);
        var texts = rl.Pages.SelectMany(p => p.Commands).OfType<DrawTextCommand>()
            .Select(c => c.Text).ToList();

        // Solo el header de tabla + 6 filas de datos. No headers de grupo.
        Assert.DoesNotContain(texts, t => t.StartsWith("Categoría:"));
    }
}
