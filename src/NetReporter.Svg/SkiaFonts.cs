using System.Collections.Concurrent;
using System.Text;
using NetReporter.Core.Styles;
using SkiaSharp;

namespace NetReporter.Svg;

/// <summary>Tramo de texto que se dibuja (y se mide) con una sola tipografía.</summary>
public readonly record struct SkiaTextRun(string Text, SKTypeface Typeface);

/// <summary>
/// Resolución de tipografías compartida por el medidor (<see cref="SkiaTextMeasurer"/>) y los
/// renderers SVG/PDF, para que lo que se mide y lo que se pinta sea siempre la misma fuente.
/// Si la fuente pedida no tiene un carácter (p. ej. "▸" en Helvetica), ese tramo usa la fuente
/// de respaldo que el sistema proponga para él.
/// </summary>
public static class SkiaFonts
{
    private static readonly ConcurrentDictionary<(string Family, FontWeight Weight, FontStyleKind Italic), SKTypeface>
        Typefaces = new();

    // Una instancia por fuente física: dos familias pedidas que el sistema resuelve a la misma
    // fuente (p. ej. "Helvetica" y "Arial" → Liberation Sans en Linux) comparten SKTypeface, y el
    // PdfRenderer las embebe una sola vez.
    private static readonly ConcurrentDictionary<string, SKTypeface> Canonical = new();

    private static readonly ConcurrentDictionary<(SKTypeface Primary, int CodePoint), SKTypeface?> Fallbacks = new();

    public static SKTypeface Resolve(ResolvedStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return Resolve(style.FontFamily, style.Weight, style.Italic);
    }

    /// <summary>
    /// SKFont para medir y dibujar. Sin hinting y con métricas lineales: en Linux FreeType redondea
    /// por defecto el avance de cada glifo a puntos enteros (una línea a 7 pt sale ~5 % más ancha y
    /// con espaciado irregular), algo pensado para pantallas, no para PDF/SVG vectoriales.
    /// En macOS no cambia nada: CoreText ya entrega avances lineales.
    /// </summary>
    public static SKFont CreateFont(SKTypeface typeface, float size) =>
        new(typeface, size) { Hinting = SKFontHinting.None, LinearMetrics = true, Subpixel = true };

    public static SKTypeface Resolve(string family, FontWeight weight, FontStyleKind italic) =>
        Typefaces.GetOrAdd((family, weight, italic), static k => Canonicalize(Load(k.Family, k.Weight, k.Italic)));

    // Las tres familias estándar de PDF no suelen existir en Linux ni en Windows; se prueba su
    // equivalente métrico habitual, que fontconfig resuelve a Liberation si está instalada.
    private static readonly Dictionary<string, string> MetricEquivalents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Helvetica"] = "Arial",
        ["Courier"] = "Courier New",
        ["Times"] = "Times New Roman"
    };

    private static SKTypeface Load(string family, FontWeight weight, FontStyleKind italic)
    {
        var slant = italic == FontStyleKind.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var skWeight = weight == FontWeight.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var style = new SKFontStyle(skWeight, SKFontStyleWidth.Normal, slant);
        var fonts = SKFontManager.Default;

        // MatchFamily devuelve null cuando la familia no existe. FromFamilyName, en cambio, cae a la
        // fuente por defecto y en Linux la entrega en regular aunque se pida negrita, así que solo
        // se usa como último recurso. Cada paso conserva el peso y la inclinación pedidos.
        return fonts.MatchFamily(family, style)
               ?? (MetricEquivalents.TryGetValue(family, out var equivalent) ? fonts.MatchFamily(equivalent, style) : null)
               ?? fonts.MatchFamily(GenericFamily(family), style)          // Linux: sans-serif / serif / monospace
               ?? fonts.MatchFamily(SKTypeface.Default.FamilyName, style)  // macOS/Windows no conocen los genéricos
               ?? SKTypeface.FromFamilyName(family, skWeight, SKFontStyleWidth.Normal, slant)
               ?? SKTypeface.Default;
    }

    private static string GenericFamily(string family)
    {
        var name = family.ToLowerInvariant();
        if (name.Contains("mono") || name.Contains("courier") || name.Contains("consolas") || name.Contains("menlo"))
            return "monospace";
        if ((name.Contains("serif") && !name.Contains("sans")) || name.Contains("times") || name.Contains("georgia"))
            return "serif";
        return "sans-serif";
    }

    /// <summary>
    /// Parte <paramref name="text"/> en tramos por tipografía. El caso común (la fuente principal
    /// tiene todos los caracteres) devuelve un único tramo sin recorrer el texto.
    /// </summary>
    public static IReadOnlyList<SkiaTextRun> SplitRuns(string text, SKTypeface primary)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(primary);
        using var font = new SKFont(primary);
        return SplitRuns(text, primary, font);
    }

    // La tipografía se pasa aparte de su SKFont: SKFont.Typeface puede devolver otro envoltorio
    // administrado de la misma fuente, y el PdfRenderer identifica las fuentes por referencia.
    private static IReadOnlyList<SkiaTextRun> SplitRuns(string text, SKTypeface primary, SKFont primaryFont)
    {
        if (text.Length == 0 || primaryFont.ContainsGlyphs(text))
            return [new SkiaTextRun(text, primary)];

        var runs = new List<SkiaTextRun>();
        var current = new StringBuilder();
        var currentTypeface = primary;

        foreach (var rune in text.EnumerateRunes())
        {
            var typeface = TypefaceFor(rune, primary, primaryFont);
            if (!ReferenceEquals(typeface, currentTypeface) && current.Length > 0)
            {
                runs.Add(new SkiaTextRun(current.ToString(), currentTypeface));
                current.Clear();
            }
            currentTypeface = typeface;
            current.Append(rune.ToString());
        }

        if (current.Length > 0) runs.Add(new SkiaTextRun(current.ToString(), currentTypeface));
        return runs;
    }

    /// <summary>Ancho de <paramref name="text"/> con <paramref name="font"/>, sumando los tramos de respaldo.</summary>
    public static float MeasureText(string text, SKFont font)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        if (text.Length == 0) return 0;
        if (font.ContainsGlyphs(text)) return font.MeasureText(text);

        var primary = font.Typeface;
        float width = 0;
        foreach (var run in SplitRuns(text, primary, font))
        {
            if (ReferenceEquals(run.Typeface, primary))
            {
                width += font.MeasureText(run.Text);
                continue;
            }
            using var fallback = CreateFont(run.Typeface, font.Size);
            width += fallback.MeasureText(run.Text);
        }
        return width;
    }

    private static SKTypeface TypefaceFor(Rune rune, SKTypeface primary, SKFont primaryFont)
    {
        // Controles y espacios nunca justifican buscar otra fuente: van con la principal.
        if (Rune.IsControl(rune) || Rune.IsWhiteSpace(rune) || primaryFont.ContainsGlyph(rune.Value))
            return primary;

        return Fallbacks.GetOrAdd((primary, rune.Value), static key =>
            FindFallback(key.Primary, key.CodePoint) is { } fallback ? Canonicalize(fallback) : null) ?? primary;
    }

    private static readonly Lazy<string[]> SystemFamilies = new(() =>
        ["sans-serif", "serif", "monospace", .. SKFontManager.Default.FontFamilies.Order(StringComparer.Ordinal)]);

    private static SKTypeface? FindFallback(SKTypeface primary, int codePoint)
    {
        var fonts = SKFontManager.Default;
        var match = fonts.MatchCharacter(primary.FamilyName, primary.FontStyle, null, codePoint);
        if (Covers(match, codePoint)) return match;

        // En Linux (fontconfig) MatchCharacter devuelve null aunque haya fuentes instaladas con el
        // carácter, así que se recorren las familias del sistema (genéricas primero, luego en orden
        // alfabético para que la elección sea estable) con el estilo de la fuente principal.
        foreach (var family in SystemFamilies.Value)
        {
            var candidate = fonts.MatchFamily(family, primary.FontStyle);
            if (Covers(candidate, codePoint)) return candidate;
        }
        return null;
    }

    private static bool Covers(SKTypeface? typeface, int codePoint)
    {
        if (typeface is null) return false;
        using var font = new SKFont(typeface);
        return font.ContainsGlyph(codePoint);
    }

    private static SKTypeface Canonicalize(SKTypeface typeface)
    {
        var key = $"{typeface.FamilyName}|{typeface.FontWeight}|{typeface.FontWidth}|{(int)typeface.FontSlant}";
        return Canonical.GetOrAdd(key, typeface);
    }
}
