using SkiaSharp;

namespace NetReporter.Svg.Tests;

public sealed class GlyphAdvancesTests
{
    private const string Sample = "Factura 001-002 ÁÉÍÓÚÑ áéíóúñ 0123456789 Total: L 1,234.50 Distribuidora";

    private static readonly SKTypeface Lato =
        SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "Fonts", "Lato-Regular.ttf"))
        ?? throw new InvalidOperationException("No se pudo cargar Lato");

    [Theory]
    [InlineData(7f)]
    [InlineData(9f)]
    [InlineData(10.5f)]
    [InlineData(12f)]
    [InlineData(18f)]
    [InlineData(36f)]
    [InlineData(72f)]
    public void Width_IsBitForBitEqualToSkFontMeasureText(float size)
    {
        var advances = SkiaFonts.Advances(Lato, size);
        using var font = SkiaFonts.CreateFont(Lato, size);

        Assert.Equal(font.MeasureText(Sample), advances.Width(Sample));
        Assert.Equal(font.MeasureText(Sample), advances.Width(Sample));   // segunda vez: tabla ya caliente
        foreach (var word in Sample.Split(' '))
            Assert.Equal(font.MeasureText(word), advances.Width(word));
    }

    [Fact]
    public void Position_MatchesSkFontGetGlyphPositions()
    {
        var advances = SkiaFonts.Advances(Lato, 10);
        using var font = SkiaFonts.CreateFont(Lato, 10);
        var glyphs = advances.Glyphs(Sample);
        var positions = new SKPoint[glyphs.Length];

        advances.Position(glyphs, positions);

        Assert.Equal(font.GetGlyphs(Sample), glyphs);
        Assert.Equal(font.GetGlyphPositions(Sample), positions);
    }

    [Fact]
    public void Advances_SameTypefaceAndSize_ShareOneTable_AndMeasureTheSameFromManyThreads()
    {
        var table = SkiaFonts.Advances(Lato, 11);
        Assert.Same(table, SkiaFonts.Advances(Lato, 11));
        Assert.NotSame(table, SkiaFonts.Advances(Lato, 12));

        using var font = SkiaFonts.CreateFont(Lato, 11);
        var expected = font.MeasureText(Sample);
        var results = new float[64];
        Parallel.For(0, results.Length, i => results[i] = SkiaFonts.Advances(Lato, 11).Width(Sample));

        Assert.All(results, width => Assert.Equal(expected, width));
    }

    [Fact]
    public void MeasureText_WithFallbackRuns_MatchesMeasuringEachRunWithSkia()
    {
        // ▸ no está en Lato: ese tramo se mide con la tabla de su fuente de respaldo.
        const string text = "Total ▸ 1,234.50";
        using var font = SkiaFonts.CreateFont(Lato, 10);
        float expected = 0;
        foreach (var run in SkiaFonts.SplitRuns(text, Lato))
        {
            using var runFont = SkiaFonts.CreateFont(run.Typeface, 10);
            expected += runFont.MeasureText(run.Text);
        }

        Assert.Equal(expected, SkiaFonts.MeasureText(text, Lato, font));
        Assert.Equal(expected, SkiaFonts.MeasureText(text, font));
    }
}
