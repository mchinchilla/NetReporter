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

public sealed class DetailKeepTogetherTests
{
    private static readonly StyleSheet Styles = new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Helvetica").FontSize(10))
        .Add("TableHeader", s => s.BasedOn("Default").Bold())
        .Add("TableRow", s => s.BasedOn("Default"))
        .Add("Alert", s => s.BasedOn("Default").Bold().Foreground(Color.FromHex("#DC2626")))
        .Build();

    // Página de 300pt con márgenes de 20 → 260pt útiles.
    private static ReportDefinition Report(params Band[] bands) => new()
    {
        Name = "KT",
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

    private static DetailBand Block(string name, double height, bool keepTogether = false) => new()
    {
        Height = height,
        KeepTogether = keepTogether,
        Elements = [Text($"{name}-top"), Text($"{name}-bottom", height - 14)]
    };

    private static int PageOf(RenderList.RenderList rl, string text) =>
        rl.Pages.First(p => p.Commands.OfType<DrawTextCommand>().Any(c => c.Text == text)).PageNumber;

    [Fact]
    public void DetailBand_ThatDoesNotFit_MovesWholeToNextPage()
    {
        // 150 + 150 > 260: el segundo bloque no cabe y no puede partirse → página 2 completa.
        var rl = new LayoutEngine().Layout(Report(Block("A", 150), Block("B", 150)));

        Assert.Equal(2, rl.Pages.Count);
        Assert.Equal(2, PageOf(rl, "B-top"));
        Assert.Equal(2, PageOf(rl, "B-bottom"));
        // Nada se duplica (bug histórico: el contenido previo quedaba copiado en la página siguiente).
        Assert.Single(rl.Pages.SelectMany(p => p.Commands).OfType<DrawTextCommand>(), c => c.Text == "A-top");
    }

    [Fact]
    public void DetailBand_WithTable_AndKeepTogether_MovesTableToNextPageWhenItFitsThere()
    {
        var rows = Enumerable.Range(1, 8).Select(i => $"Fila {i}").ToArray();
        var table = new TableElement<string>
        {
            Bounds = new Rect(0, 0, 200, 0),
            Rows = rows,
            RowHeight = 18,
            HeaderHeight = 20,
            Columns = [new TableColumn<string>("Detalle", Bind.From<string, object?>(r => r), 200)]
        };
        // La tabla mide 20 + 8×18 = 164pt: no cabe tras un bloque de 150pt, pero sí en una página vacía.
        var withTable = new DetailBand { Height = 0, KeepTogether = true, Elements = [table] };

        var rl = new LayoutEngine().Layout(Report(Block("A", 150), withTable));

        Assert.Equal(2, PageOf(rl, "Detalle"));
        Assert.Equal(2, PageOf(rl, "Fila 8"));
        Assert.Single(rl.Pages.SelectMany(p => p.Commands).OfType<DrawTextCommand>(), c => c.Text == "Detalle");
    }

    [Fact]
    public void DetailBand_WithTable_WithoutKeepTogether_SplitsAcrossPages()
    {
        var rows = Enumerable.Range(1, 8).Select(i => $"Fila {i}").ToArray();
        var table = new TableElement<string>
        {
            Bounds = new Rect(0, 0, 200, 0),
            Rows = rows,
            RowHeight = 18,
            HeaderHeight = 20,
            Columns = [new TableColumn<string>("Detalle", Bind.From<string, object?>(r => r), 200)]
        };
        var rl = new LayoutEngine().Layout(Report(Block("A", 150), new DetailBand { Height = 0, Elements = [table] }));

        Assert.Equal(1, PageOf(rl, "Fila 1"));
        Assert.Equal(2, PageOf(rl, "Fila 8"));
    }

    [Fact]
    public void KeepTogether_BandTallerThanAPage_IsEmittedWithoutEmptyPage()
    {
        var rl = new LayoutEngine().Layout(Report(Block("Huge", 400, keepTogether: true)));
        Assert.Equal(1, PageOf(rl, "Huge-top"));
    }

    [Fact]
    public void AutoHeightDetail_IsMeasuredBeforeDeciding()
    {
        // Declara 20pt pero su texto con wrap ocupa mucho más: la medición en seco lo detecta.
        var longText = string.Join(' ', Enumerable.Repeat("palabra", 50));
        var tall = new DetailBand
        {
            Height = 20,
            AutoHeight = true,
            KeepTogether = true,
            Elements = [new TextElement { Bounds = new Rect(0, 0, 200, 14), Content = Expr.Str(longText), AutoHeight = true }]
        };
        var rl = new LayoutEngine().Layout(Report(Block("A", 150), tall));

        Assert.Equal(2, rl.Pages.Count);
        Assert.Equal(1, PageOf(rl, "A-top"));
        Assert.All(rl.Pages[1].Commands.OfType<DrawTextCommand>(), c => Assert.StartsWith("palabra", c.Text));
        Assert.DoesNotContain(rl.Pages[0].Commands.OfType<DrawTextCommand>(), c => c.Text.StartsWith("palabra"));
    }

    [Fact]
    public void Visible_False_SkipsElementAndBand()
    {
        var hiddenElement = Text("oculto") with { Visible = Expr.Of(_ => false) };
        var hiddenBand = new DetailBand { Height = 50, Visible = Expr.Of(_ => false), Elements = [Text("banda oculta")] };
        var rl = new LayoutEngine().Layout(Report(
            new DetailBand { Height = 30, Elements = [Text("visible"), hiddenElement] },
            hiddenBand));

        var texts = rl.Pages.SelectMany(p => p.Commands).OfType<DrawTextCommand>().Select(c => c.Text).ToList();
        Assert.Contains("visible", texts);
        Assert.DoesNotContain("oculto", texts);
        Assert.DoesNotContain("banda oculta", texts);
    }

    [Fact]
    public void StyleSelector_PicksNamedStyle_FallsBackWhenUnknown()
    {
        var alert = Text("alerta") with { StyleSelector = Expr.Of(_ => "Alert") };
        var unknown = Text("normal") with { StyleSelector = Expr.Of(_ => "NoExiste") };
        var rl = new LayoutEngine().Layout(Report(new DetailBand { Height = 40, Elements = [alert, unknown with { Bounds = new Rect(0, 20, 200, 14) }] }));

        var cmds = rl.Pages[0].Commands.OfType<DrawTextCommand>().ToList();
        Assert.Equal(Color.FromHex("#DC2626"), cmds.Single(c => c.Text == "alerta").Style.Foreground);
        Assert.Equal(Color.Black, cmds.Single(c => c.Text == "normal").Style.Foreground);
    }

    [Fact]
    public void PageHeader_ResolvesTotalPages()
    {
        var header = new PageHeaderBand
        {
            Height = 20,
            Elements = [new TextElement
            {
                Bounds = new Rect(0, 0, 200, 14),
                Content = Expr.Of(ctx => $"Página {ctx.PageNumber} de {ctx.TotalPages}"),
                WordWrap = false
            }]
        };
        var rl = new LayoutEngine().Layout(Report(header, Block("A", 150), Block("B", 150)));

        var headers = rl.Pages.Select(p => p.Commands.OfType<DrawTextCommand>().First().Text).ToList();
        Assert.Equal(new[] { "Página 1 de 2", "Página 2 de 2" }, headers);
    }

    [Fact]
    public void PageFooter_PrintOnFirstPageFalse_SkipsFirstPage()
    {
        var footer = new PageFooterBand { Height = 20, PrintOnFirstPage = false, Elements = [Text("pie")] };
        var rl = new LayoutEngine().Layout(Report(footer, Block("A", 150), Block("B", 150)));

        Assert.DoesNotContain(rl.Pages[0].Commands.OfType<DrawTextCommand>(), c => c.Text == "pie");
        Assert.Contains(rl.Pages[1].Commands.OfType<DrawTextCommand>(), c => c.Text == "pie");
    }

    [Fact]
    public void Table_BoundsY_IsAnOffsetFromTheBandTop()
    {
        var table = new TableElement<string>
        {
            Bounds = new Rect(0, 30, 200, 0),
            Rows = ["fila"],
            HeaderHeight = 20,
            RowHeight = 16,
            Columns = [new TableColumn<string>("Col", Bind.From<string, object?>(r => r), 200)]
        };
        var rl = new LayoutEngine().Layout(Report(new DetailBand { Height = 0, AutoHeight = true, Elements = [Text("Título"), table] }));

        var cmds = rl.Pages[0].Commands.OfType<DrawTextCommand>().ToList();
        var title = cmds.Single(c => c.Text == "Título");
        var header = cmds.Single(c => c.Text == "Col");
        Assert.Equal(title.Bounds.Y + 30, header.Bounds.Y, 3);
    }
}
