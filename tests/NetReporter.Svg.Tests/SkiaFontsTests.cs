using NetReporter.Core.Styles;
using NetReporter.Svg;
using SkiaSharp;

namespace NetReporter.Svg.Tests;

public sealed class SkiaFontsTests
{
    // Lato no tiene U+25B8 (▸), el marcador que usan los encabezados de grupo de los samples.
    private const int Triangle = 0x25B8;

    private static readonly SKTypeface Lato =
        SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "Fonts", "Lato-Regular.ttf"))
        ?? throw new InvalidOperationException("No se pudo cargar Lato");

    [Fact]
    public void SplitRuns_TextCoveredByTheFont_IsASingleRunWithThatFont()
    {
        var run = Assert.Single(SkiaFonts.SplitRuns("Factura 001 Peñas Ángel", Lato));

        Assert.Equal("Factura 001 Peñas Ángel", run.Text);
        Assert.Same(Lato, run.Typeface);
    }

    [Fact]
    public void SplitRuns_CharacterMissingFromTheFont_UsesASystemFallbackForThatCharacterOnly()
    {
        const string text = "▸ Computadoras";

        var runs = SkiaFonts.SplitRuns(text, Lato);

        Assert.Equal(text, string.Concat(runs.Select(r => r.Text)));
        if (!SystemHasFallbackFor(Triangle))
        {
            // Sin fuente de respaldo en el sistema el carácter se queda en Lato (se verá como .notdef).
            Assert.Same(Lato, Assert.Single(runs).Typeface);
            return;
        }

        Assert.Equal(2, runs.Count);
        Assert.Equal("▸", runs[0].Text);
        Assert.NotSame(Lato, runs[0].Typeface);
        using var fallback = new SKFont(runs[0].Typeface);
        Assert.True(fallback.ContainsGlyph(Triangle));
        Assert.Equal(" Computadoras", runs[1].Text);
        Assert.Same(Lato, runs[1].Typeface);
    }

    [Fact]
    public void MeasureText_AddsTheWidthOfEveryRun()
    {
        const string text = "▸ Computadoras";
        using var font = SkiaFonts.CreateFont(Lato, 12);

        double expected = 0;
        foreach (var run in SkiaFonts.SplitRuns(text, Lato))
        {
            using var runFont = SkiaFonts.CreateFont(run.Typeface, 12);
            expected += runFont.MeasureText(run.Text);
        }

        Assert.Equal(expected, SkiaFonts.MeasureText(text, font), 3);
    }

    [Fact]
    public void CreateFont_UsesLinearMetrics_SoWidthsScaleWithTheSize()
    {
        // Con hinting (el default de SKFont en Linux) los avances se redondean a puntos enteros: con
        // Lato, esta línea a 7 pt mide ~1.3 % más por punto que a 10 pt. Sin hinting la diferencia
        // es solo el redondeo de punto fijo de FreeType (~0.003 %).
        const string text = "Esta factura es generada electrónicamente y es válida sin firma.";
        using var small = SkiaFonts.CreateFont(Lato, 7);
        using var large = SkiaFonts.CreateFont(Lato, 10);

        var ratio = (small.MeasureText(text) / 7) / (large.MeasureText(text) / 10);
        Assert.InRange(ratio, 0.999, 1.001);
    }

    [Fact]
    public void Resolve_SameStyle_ReturnsTheSameTypefaceInstance()
    {
        var first = SkiaFonts.Resolve("Helvetica", FontWeight.Bold, FontStyleKind.Normal);
        var second = SkiaFonts.Resolve("Helvetica", FontWeight.Bold, FontStyleKind.Normal);

        Assert.Same(first, second);
    }

    [Fact]
    public void Resolve_UnknownFamily_KeepsTheRequestedWeight()
    {
        // En Linux, SKTypeface.FromFamilyName con una familia inexistente (p. ej. "Helvetica")
        // devuelve la fuente por defecto en regular aunque se pida negrita.
        var bold = new SKFontStyle(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        var systemBold = SKFontManager.Default.MatchFamily(SKTypeface.Default.FamilyName, bold);
        if (systemBold is null || systemBold.FontWeight < 600) return;   // el sistema no tiene ninguna negrita

        var typeface = SkiaFonts.Resolve("NetReporter Fuente Inexistente", FontWeight.Bold, FontStyleKind.Normal);

        Assert.True(typeface.FontWeight >= 600, $"{typeface.FamilyName} con peso {typeface.FontWeight}");
    }

    private static bool SystemHasFallbackFor(int codePoint)
    {
        var fonts = SKFontManager.Default;
        return Covers(fonts.MatchCharacter(Lato.FamilyName, Lato.FontStyle, null, codePoint), codePoint)
               || fonts.FontFamilies.Any(family => Covers(fonts.MatchFamily(family, Lato.FontStyle), codePoint));
    }

    private static bool Covers(SKTypeface? typeface, int codePoint)
    {
        if (typeface is null) return false;
        using var font = new SKFont(typeface);
        return font.ContainsGlyph(codePoint);
    }
}
