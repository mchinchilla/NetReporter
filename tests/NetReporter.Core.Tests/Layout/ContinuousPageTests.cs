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
/// Papel continuo (rollo POS): una sola página sin saltos, cuya altura final sale del contenido.
/// El PageFooter se imprime una vez, justo después del contenido, y el RenderList lleva la altura.
/// </summary>
public sealed class ContinuousPageTests
{
    private sealed record Row(string Name);

    private static readonly StyleSheet Styles = new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Helvetica").FontSize(9))
        .Add("TableHeader", s => s.BasedOn("Default").Bold())
        .Add("TableRow", s => s.BasedOn("Default"))
        .Build();

    private static ReportDefinition Report(PageSetup page, int rowCount, bool withChrome = true, bool keepTogether = false)
    {
        var rows = Enumerable.Range(1, rowCount).Select(i => new Row($"ROW-{i:000}")).ToArray();
        var table = new TableElement<Row>
        {
            Bounds = new Rect(0, 0, 200, 0),
            Rows = rows,
            HeaderHeight = 20,
            RowHeight = 16,
            HeaderMode = TableHeaderMode.RepeatOnPageBreak,
            HeaderStyle = new StyleRef("TableHeader"),
            RowStyle = new StyleRef("TableRow"),
            Columns = new[] { new TableColumn<Row>("Name", Bind.From<Row, object?>(r => r.Name), Width: 200) },
        };
        var bands = new List<Band>();
        if (withChrome)
            bands.Add(new PageHeaderBand { Height = 30, Elements = new ReportElement[]
            {
                new TextElement { Bounds = new Rect(0, 0, 200, 14), Content = Expr.Str("TICKET"), Style = new StyleRef("Default") },
            } });
        bands.Add(new DetailBand { Height = 0, AutoHeight = true, KeepTogether = keepTogether, Elements = new[] { (ReportElement)table } });
        if (withChrome)
            bands.Add(new PageFooterBand { Height = 20, Elements = new ReportElement[]
            {
                // Expr.TotalPages se resuelve en la pasada 2: en papel continuo debe ser "1".
                new TextElement { Bounds = new Rect(0, 0, 200, 12), Content = Expr.TotalPages, Style = new StyleRef("Default") },
            } });
        return new ReportDefinition { Name = "Ticket", Page = page, Styles = Styles, Bands = bands };
    }

    [Fact]
    public void LongTable_IsOnePage_WithHeightFromContent()
    {
        const int n = 400;   // on Letter this would be several pages
        var page = PageSetup.Roll80;
        var rl = new LayoutEngine().Layout(Report(page, n));

        Assert.Single(rl.Pages);
        Assert.Equal(page.Size.Width, rl.Page.Size.Width);
        Assert.True(rl.Page.Continuous);

        var texts = rl.Pages[0].Commands.OfType<DrawTextCommand>().Select(t => t.Text).ToList();
        for (var i = 1; i <= n; i++) Assert.Equal(1, texts.Count(t => t == $"ROW-{i:000}"));

        // Altura = margen superior + cabecera (30) + tabla (20 + 400·16) + pie (20) + margen inferior.
        var expected = page.Margins.Top + 30 + 20 + n * 16 + 20 + page.Margins.Bottom;
        Assert.Equal(expected, rl.Page.Size.Height, 3);
        // Nada se dibuja por debajo del borde inferior.
        var bottomMost = rl.Pages[0].Commands.OfType<DrawTextCommand>().Max(t => t.Bounds.Y + t.Bounds.Height);
        Assert.True(bottomMost <= rl.Page.Size.Height - page.Margins.Bottom + 0.01);
    }

    [Fact]
    public void PageFooter_FollowsTheContent_AndCountsOnePage()
    {
        var page = PageSetup.Roll58;
        var rl = new LayoutEngine().Layout(Report(page, 5));

        var contentBottom = page.Margins.Top + 30 + 20 + 5 * 16;
        var footer = Assert.Single(rl.Pages[0].Commands.OfType<DrawTextCommand>(), t => t.Bounds.Y >= contentBottom - 0.01);
        Assert.Equal("1", footer.Text);
        Assert.Equal(contentBottom, footer.Bounds.Y, 3);
    }

    [Fact]
    public void EmptyReport_HeightIsMarginsPlusChrome()
    {
        var page = PageSetup.Roll80;
        var rl = new LayoutEngine().Layout(Report(page, 0));
        Assert.Single(rl.Pages);
        Assert.Equal(page.Margins.Top + 30 + 20 + 20 + page.Margins.Bottom, rl.Page.Size.Height, 3);
    }

    [Fact]
    public void WithoutPageChrome_HeightIsContentPlusMargins()
    {
        var page = PageSetup.Roll(200, new Thickness(10));
        var rl = new LayoutEngine().Layout(Report(page, 3, withChrome: false));
        Assert.Equal(10 + 20 + 3 * 16 + 10, rl.Page.Size.Height, 3);
        Assert.Equal(200, rl.Page.Size.Width);
    }

    [Fact]
    public void KeepTogetherDetail_NeverOpensAPage()
    {
        var rl = new LayoutEngine().Layout(Report(PageSetup.Roll80, 50, keepTogether: true));
        Assert.Single(rl.Pages);
        Assert.Equal(50, rl.Pages[0].Commands.OfType<DrawTextCommand>().Count(t => t.Text.StartsWith("ROW-", StringComparison.Ordinal)));
    }

    [Fact]
    public void FixedPage_StillPaginates()
    {
        var rl = new LayoutEngine().Layout(Report(PageSetup.Letter.WithMargins(new Thickness(36)), 400));
        Assert.True(rl.Pages.Count > 1);
        Assert.Equal(PageSetup.Letter.Size.Height, rl.Page.Size.Height);
        Assert.False(rl.Page.Continuous);
    }

    [Fact]
    public void RollPresets_AreContinuousWithPrintableMargins()
    {
        Assert.True(PageSetup.Roll80.Continuous);
        Assert.Equal(Units.Mm(80), PageSetup.Roll80.Size.Width, 6);
        Assert.Equal(Units.Mm(72), PageSetup.Roll80.ContentWidth, 6);     // 576 dots at 203 dpi
        Assert.Equal(Units.Mm(58), PageSetup.Roll58.Size.Width, 6);
        Assert.Equal(Units.Mm(48), PageSetup.Roll58.ContentWidth, 6);     // 384 dots at 203 dpi
        Assert.Equal(150, PageSetup.Roll(150).Size.Width);
        Assert.Throws<ArgumentOutOfRangeException>(() => PageSetup.Roll(0));
        Assert.Throws<InvalidOperationException>(() => PageSetup.Roll80.Landscape());
        Assert.False(PageSetup.Letter.Continuous);
    }
}
