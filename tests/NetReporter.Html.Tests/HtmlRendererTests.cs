using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;
using NetReporter.Html;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Html.Tests;

public sealed class HtmlRendererTests
{
    private static readonly ResolvedStyle DefaultStyle = new(
        FontFamily: "Helvetica",
        FontSize: 10,
        Weight: FontWeight.Normal,
        Italic: FontStyleKind.Normal,
        Foreground: Color.Black,
        Background: Color.Transparent,
        TextAlign: Core.Styles.TextAlignment.Left,
        VerticalAlign: VerticalAlignment.Top,
        Padding: Thickness.Zero,
        Border: null,
        NumberFormat: null);

    private static RlRenderList BuildList(params RenderCommand[] commands) =>
        new(PageSetup.Letter, new[] { new RenderPage(1, commands) });

    [Fact]
    public void Render_Empty_ProducesValidHtmlShell()
    {
        var list = new RlRenderList(PageSetup.Letter, Array.Empty<RenderPage>());
        var html = new HtmlRenderer().Render(list);
        Assert.Contains("<!doctype html>", html);
        Assert.Contains("<title>Report</title>", html);
        Assert.Contains("@page { size: 612pt 792pt; margin: 0; }", html);
        Assert.Contains("</html>", html);
    }

    [Fact]
    public void Render_Text_EmitsSpan()
    {
        var list = BuildList(new DrawTextCommand(
            new Rect(10, 20, 100, 14),
            "Hola",
            DefaultStyle));

        var html = new HtmlRenderer().Render(list);
        Assert.Contains("<span class=\"nr-el nr-text\"", html);
        Assert.Contains("left:10px", html);
        Assert.Contains("top:20px", html);
        Assert.Contains("font-size:10px", html);
        Assert.Contains("color:#000000", html);
        Assert.Contains(">Hola</span>", html);
    }

    [Fact]
    public void Render_TextEscapesSpecialChars()
    {
        var list = BuildList(new DrawTextCommand(
            new Rect(0, 0, 100, 14),
            "<script>&\"'",
            DefaultStyle));
        var html = new HtmlRenderer().Render(list);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&amp;", html);
    }

    [Fact]
    public void Render_BoldItalicAppliesStyles()
    {
        var style = DefaultStyle with { Weight = FontWeight.Bold, Italic = FontStyleKind.Italic };
        var list = BuildList(new DrawTextCommand(new Rect(0, 0, 100, 14), "X", style));
        var html = new HtmlRenderer().Render(list);
        Assert.Contains("font-weight:700", html);
        Assert.Contains("font-style:italic", html);
    }

    [Fact]
    public void Render_TextAlignRight_EmitsCss()
    {
        var style = DefaultStyle with { TextAlign = Core.Styles.TextAlignment.Right };
        var list = BuildList(new DrawTextCommand(new Rect(0, 0, 100, 14), "X", style));
        var html = new HtmlRenderer().Render(list);
        Assert.Contains("text-align:right", html);
    }

    [Fact]
    public void Render_HorizontalLine_EmitsDivWithBackground()
    {
        var list = BuildList(new DrawLineCommand(
            new Point(10, 50),
            new Point(200, 50),
            1,
            Color.FromHex("#FF0000")));
        var html = new HtmlRenderer().Render(list);
        Assert.Contains("<div class=\"nr-el\"", html);
        Assert.Contains("width:190px", html);     // 200 - 10
        Assert.Contains("height:1px", html);
        Assert.Contains("#FF0000", html);
    }

    [Fact]
    public void Render_VerticalLine_EmitsDivWithHeight()
    {
        var list = BuildList(new DrawLineCommand(
            new Point(50, 10),
            new Point(50, 200),
            2,
            Color.Black));
        var html = new HtmlRenderer().Render(list);
        Assert.Contains("width:2px", html);
        Assert.Contains("height:190px", html);
    }

    [Fact]
    public void Render_Rectangle_WithFillAndBorder()
    {
        var fill = Color.FromHex("#F1F5F9");
        var border = new BorderLine(0.5, Color.Black);
        var list = BuildList(new DrawRectangleCommand(
            new Rect(5, 5, 100, 50), fill, border));
        var html = new HtmlRenderer().Render(list);
        Assert.Contains("background:#F1F5F9", html);
        Assert.Contains("border:0.5px solid #000000", html);
        Assert.Contains("width:100px", html);
        Assert.Contains("height:50px", html);
    }

    [Fact]
    public void Render_MultiplePages_EmitsOneSectionEach()
    {
        var list = new RlRenderList(PageSetup.Letter, new[]
        {
            new RenderPage(1, new[] { new DrawTextCommand(new Rect(0, 0, 10, 10), "A", DefaultStyle) }),
            new RenderPage(2, new[] { new DrawTextCommand(new Rect(0, 0, 10, 10), "B", DefaultStyle) })
        });
        var html = new HtmlRenderer().Render(list);
        Assert.Equal(2, CountOccurrences(html, "<section class=\"nr-page\""));
        Assert.Contains("data-page=\"1\"", html);
        Assert.Contains("data-page=\"2\"", html);
    }

    [Fact]
    public void Render_PrintCssIncluded()
    {
        var list = new RlRenderList(PageSetup.Letter, Array.Empty<RenderPage>());
        var html = new HtmlRenderer().Render(list);
        Assert.Contains("@media print", html);
        Assert.Contains("page-break-after: always", html);
    }

    [Fact]
    public void Render_FragmentModeOmitsShell()
    {
        var list = BuildList(new DrawTextCommand(new Rect(0, 0, 10, 10), "X", DefaultStyle));
        var html = new HtmlRenderer().Render(list, new HtmlRenderOptions { FullDocument = false });
        Assert.DoesNotContain("<!doctype html>", html);
        Assert.DoesNotContain("<html", html);
        Assert.Contains("<section class=\"nr-page\"", html);
    }

    [Fact]
    public void Render_CustomTitle()
    {
        var list = new RlRenderList(PageSetup.Letter, Array.Empty<RenderPage>());
        var html = new HtmlRenderer().Render(list, new HtmlRenderOptions { Title = "Factura #123" });
        Assert.Contains("<title>Factura #123</title>", html);
    }

    [Fact]
    public void Render_A4Page_UsesCorrectSize()
    {
        var list = new RlRenderList(PageSetup.A4, new[] { new RenderPage(1, Array.Empty<RenderCommand>()) });
        var html = new HtmlRenderer().Render(list);
        // A4: ~595 x 841.89 pt
        Assert.Contains("width: 595", html);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
