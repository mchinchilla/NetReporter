using NetReporter.Core.Layout;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Tests.Layout;

public sealed class EstimateTextMeasurerTests
{
    private static readonly ResolvedStyle Style = new(
        FontFamily: "Helvetica",
        FontSize: 10,
        Weight: FontWeight.Normal,
        Italic: FontStyleKind.Normal,
        Foreground: Color.Black,
        Background: Color.Transparent,
        TextAlign: NetReporter.Core.Styles.TextAlignment.Left,
        VerticalAlign: VerticalAlignment.Top,
        Padding: Thickness.Zero,
        Border: null,
        NumberFormat: null);

    private static readonly EstimateTextMeasurer Measurer = EstimateTextMeasurer.Instance;

    [Fact]
    public void MeasureWidth_Empty_ReturnsZero()
    {
        Assert.Equal(0, Measurer.MeasureWidth("", Style));
    }

    [Fact]
    public void MeasureWidth_LinearScaling()
    {
        var w1 = Measurer.MeasureWidth("a", Style);
        var w10 = Measurer.MeasureWidth("aaaaaaaaaa", Style);
        Assert.Equal(w1 * 10, w10, 3);
    }

    [Fact]
    public void LineHeight_IsFontSizeTimes12()
    {
        Assert.Equal(12, Measurer.LineHeight(Style), 1);
    }

    [Fact]
    public void WrapLines_Empty_ReturnsSingleEmpty()
    {
        var lines = Measurer.WrapLines("", Style, 100);
        Assert.Single(lines);
        Assert.Equal("", lines[0]);
    }

    [Fact]
    public void WrapLines_ShortText_FitsInOneLine()
    {
        var lines = Measurer.WrapLines("Hola", Style, 100);
        Assert.Single(lines);
        Assert.Equal("Hola", lines[0]);
    }

    [Fact]
    public void WrapLines_LongText_BreaksByWord()
    {
        var lines = Measurer.WrapLines(
            "Esta es una linea muy larga que deberia romperse en varias lineas porque excede el ancho",
            Style, 100);
        Assert.True(lines.Count > 1, $"expected >1 lines, got {lines.Count}");
        // Cada línea no excede el ancho (en estimación lineal).
        foreach (var line in lines)
            Assert.True(Measurer.MeasureWidth(line, Style) <= 100,
                $"line '{line}' width {Measurer.MeasureWidth(line, Style)} > 100");
    }

    [Fact]
    public void WrapLines_RespectsExplicitNewlines()
    {
        var lines = Measurer.WrapLines("primera\nsegunda\ntercera", Style, 200);
        Assert.Equal(3, lines.Count);
        Assert.Equal("primera", lines[0]);
        Assert.Equal("segunda", lines[1]);
        Assert.Equal("tercera", lines[2]);
    }

    [Fact]
    public void WrapLines_PreservesEmptyLines()
    {
        var lines = Measurer.WrapLines("a\n\nb", Style, 200);
        Assert.Equal(3, lines.Count);
        Assert.Equal("a", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Equal("b", lines[2]);
    }

    [Fact]
    public void WrapLines_VeryLongWord_BreaksByChar()
    {
        // Word de 50 chars con maxWidth pequeño → varias líneas, ninguna palabra individual.
        var word = new string('a', 50);
        var lines = Measurer.WrapLines(word, Style, 30);
        Assert.True(lines.Count >= 2);
        // Total chars preservados.
        var joined = string.Concat(lines);
        Assert.Equal(word, joined);
    }

    [Fact]
    public void WrapLines_HandlesCarriageReturns()
    {
        var lines = Measurer.WrapLines("uno\r\ndos", Style, 200);
        Assert.Equal(2, lines.Count);
        Assert.Equal("uno", lines[0]);
        Assert.Equal("dos", lines[1]);
    }

    [Fact]
    public void WrapLines_ZeroMaxWidth_ReturnsSingleLine()
    {
        // Defensivo: no entrar en loop infinito.
        var lines = Measurer.WrapLines("texto", Style, 0);
        Assert.Single(lines);
    }

    [Fact]
    public void WrapLines_BoldIsWiderThanNormal()
    {
        var bold = Style with { Weight = FontWeight.Bold };
        var normalWidth = Measurer.MeasureWidth("texto largo", Style);
        var boldWidth = Measurer.MeasureWidth("texto largo", bold);
        Assert.True(boldWidth > normalWidth, "bold should be wider than normal");
    }
}
