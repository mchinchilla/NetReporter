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

/// <summary>
/// Control de huérfanos de tablas (<see cref="TableElement{TRow}.MinRowsBeforeBreak"/>): una sección
/// nunca debe quedar con su título y el encabezado de la tabla al pie de la página y las filas en la
/// siguiente.
/// </summary>
public sealed class TableOrphanControlTests
{
    private static readonly StyleSheet Styles = new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Helvetica").FontSize(10))
        .Add("TableHeader", s => s.BasedOn("Default").Bold())
        .Add("TableRow", s => s.BasedOn("Default"))
        .Add("GroupFooter", s => s.BasedOn("Default").Bold())
        .Build();

    // Página de 300pt con márgenes de 20 → 260pt útiles (y de 20 a 280).
    private static ReportDefinition Report(params Band[] bands) => new()
    {
        Name = "Orphans",
        Page = new PageSetup(new Size(400, 300), new Thickness(20)),
        Styles = Styles,
        Bands = bands
    };

    private static TextElement Text(string content, double y = 0, double height = 14) => new()
    {
        Bounds = new Rect(0, y, 200, height),
        Content = Expr.Str(content),
        WordWrap = false
    };

    /// <summary>Bloque fijo que consume <paramref name="height"/> puntos de la página.</summary>
    private static DetailBand Filler(double height) => new() { Height = height, Elements = [Text("relleno")] };

    private static TableElement<string> Table(int rows, double offsetY = 0, int minRows = 2, GroupFooter? summary = null) => new()
    {
        Bounds = new Rect(0, offsetY, 200, 0),
        Rows = Enumerable.Range(1, rows).Select(i => $"Fila {i}").ToArray(),
        RowHeight = 18,
        HeaderHeight = 20,
        MinRowsBeforeBreak = minRows,
        Summary = summary,
        Columns = [new TableColumn<string>("Detalle", Bind.From<string, object?>(r => r), 200)]
    };

    /// <summary>Título + nota arriba y la tabla debajo, en la misma banda (como un reporte real).</summary>
    private static DetailBand Section(TableElement<string> table, bool keepTogether = false) => new()
    {
        Height = 0,
        AutoHeight = true,
        KeepTogether = keepTogether,
        Elements = [Text("Morosidad"), table with { Bounds = table.Bounds with { Y = 30 } }]
    };

    private static IEnumerable<DrawTextCommand> Texts(RenderList.RenderList rl, int page) =>
        rl.Pages[page - 1].Commands.OfType<DrawTextCommand>();

    private static int PageOf(RenderList.RenderList rl, string text) =>
        rl.Pages.First(p => p.Commands.OfType<DrawTextCommand>().Any(c => c.Text == text)).PageNumber;

    [Fact]
    public void Section_WhoseHeaderFitsButNotItsFirstRows_MovesWithItsTitle()
    {
        // Quedan 60pt: caben el título (14) y el encabezado (30 + 20 = 50), pero no dos filas (86).
        var rl = new LayoutEngine().Layout(Report(Filler(200), Section(Table(30))));

        Assert.DoesNotContain(Texts(rl, 1), c => c.Text is "Morosidad" or "Detalle");
        Assert.Equal(2, PageOf(rl, "Morosidad"));
        Assert.Equal(2, PageOf(rl, "Fila 1"));
        // El encabezado de la página 2 es el de la tabla, no una repetición tras un salto.
        Assert.Single(Texts(rl, 2), c => c.Text == "Detalle");
    }

    [Fact]
    public void KeepTogetherSection_TallerThanAPage_StillDoesNotLeaveAnOrphanHeader()
    {
        // 30 filas no caben ni en una página vacía, así que KeepTogether no la mueve; el control de
        // huérfanos sí.
        var rl = new LayoutEngine().Layout(Report(Filler(200), Section(Table(30), keepTogether: true)));

        Assert.Equal(2, PageOf(rl, "Morosidad"));
        Assert.Equal(2, PageOf(rl, "Fila 1"));
    }

    [Fact]
    public void Section_WithRoomForTheFirstRows_StartsOnTheSamePage()
    {
        // Quedan 110pt ≥ 30 + 20 + 2×18 = 86: la tabla empieza aquí y pagina por fila.
        var rl = new LayoutEngine().Layout(Report(Filler(150), Section(Table(30))));

        Assert.Equal(1, PageOf(rl, "Morosidad"));
        Assert.Equal(1, PageOf(rl, "Fila 1"));
        Assert.Equal(1, PageOf(rl, "Fila 2"));
        Assert.True(rl.Pages.Count > 1);
    }

    [Fact]
    public void MinRowsZero_KeepsTheLegacyBehavior()
    {
        var rl = new LayoutEngine().Layout(Report(Filler(200), Section(Table(30, minRows: 0))));

        Assert.Equal(1, PageOf(rl, "Morosidad"));
        Assert.Contains(Texts(rl, 1), c => c.Text == "Detalle");
        Assert.Equal(2, PageOf(rl, "Fila 1"));
    }

    [Fact]
    public void StandaloneTable_MovesWhenOnlyItsHeaderFits()
    {
        // Sin título: quedan 50pt, caben el encabezado (20) y una fila, no las dos mínimas (56).
        var rl = new LayoutEngine().Layout(Report(Filler(210), new DetailBand { Height = 0, Elements = [Table(30)] }));

        Assert.DoesNotContain(Texts(rl, 1), c => c.Text == "Detalle");
        Assert.Equal(2, PageOf(rl, "Fila 1"));
    }

    [Fact]
    public void ShortTable_IsKeptWholeWithItsSummary()
    {
        var summary = new GroupFooter { Height = 22, Cells = [new GroupFooterCell { Content = Expr.Str("Total") }] };
        // Quedan 90pt: caben el encabezado y las dos filas (30 + 56 = 86) pero no el total (108).
        var rl = new LayoutEngine().Layout(Report(Filler(170), Section(Table(2, summary: summary))));

        Assert.Equal(2, PageOf(rl, "Morosidad"));
        Assert.Equal(2, PageOf(rl, "Fila 2"));
        Assert.Equal(2, PageOf(rl, "Total"));
    }

    [Fact]
    public void FirstTableOnAPage_NeverOpensAnEmptyPage()
    {
        // Una tabla que empieza en una página vacía se queda aunque su arranque mínimo no quepa.
        var rl = new LayoutEngine().Layout(Report(new DetailBand { Height = 0, Elements = [Table(30, minRows: 50)] }));

        Assert.Equal(1, PageOf(rl, "Detalle"));
        Assert.Equal(1, PageOf(rl, "Fila 1"));
    }

    [Fact]
    public void StartHeight_CountsHeaderGroupHeadersMinimumRowsAndSummaryOfShortTables()
    {
        var summary = new GroupFooter { Height = 22, Cells = [] };

        Assert.Equal(20 + 2 * 18, Table(30).StartHeight);
        Assert.Equal(20 + 2 * 18 + 22, Table(2, summary: summary).StartHeight);
        Assert.Equal(20 + 18 + 22, Table(1, summary: summary).StartHeight);
        Assert.Equal(20, Table(30, minRows: 0).StartHeight);
        Assert.Equal(20, Table(0).StartHeight);
    }
}
