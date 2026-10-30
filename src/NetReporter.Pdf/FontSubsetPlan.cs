using System.Runtime.InteropServices;
using System.Text;
using NetReporter.Core.RenderList;
using NetReporter.Svg;
using SkiaSharp;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Pdf;

/// <summary>
/// Fuentes recortadas para un documento: junta los glifos que usa cada tipografía en todas las
/// páginas y crea, para cada una, una versión con solo esos glifos (<see cref="TrueTypeSubsetter"/>).
/// <see cref="Map"/> se pasa a <see cref="SkiaPagePainter"/> para dibujar con ellas.
/// </summary>
internal sealed class FontSubsetPlan : IDisposable
{
    private readonly Dictionary<SKTypeface, SKTypeface> _subsets = new(ReferenceEqualityComparer.Instance);
    private readonly List<IDisposable> _owned = [];

    public static FontSubsetPlan Empty() => new();

    public static FontSubsetPlan Build(RlRenderList renderList)
    {
        // Un SKFont por tipografía (no dos por texto) y cada texto distinto se procesa una sola vez:
        // en un listado de 300 páginas los encabezados de tabla se repiten en todas. Esta pasada
        // llegaba a costar tanto como pintar el documento entero.
        var fonts = new Dictionary<SKTypeface, SKFont>(ReferenceEqualityComparer.Instance);
        var seen = new Dictionary<SKTypeface, HashSet<string>>(ReferenceEqualityComparer.Instance);
        var codePoints = new Dictionary<SKTypeface, HashSet<int>>(ReferenceEqualityComparer.Instance);
        try
        {
            foreach (var page in renderList.Pages)
            {
                foreach (var cmd in page.Commands)
                {
                    if (cmd is not DrawTextCommand { Text.Length: > 0 } text) continue;
                    var primary = SkiaFonts.Resolve(text.Style);
                    if (!seen.TryGetValue(primary, out var texts))
                        seen[primary] = texts = [];
                    if (!texts.Add(text.Text)) continue;

                    foreach (var run in SkiaFonts.SplitRuns(text.Text, primary, FontFor(primary)))
                    {
                        if (!codePoints.TryGetValue(run.Typeface, out var set))
                            codePoints[run.Typeface] = set = [];
                        foreach (var rune in run.Text.EnumerateRunes()) set.Add(rune.Value);
                    }
                }
            }

            // El cmap mapea punto de código → glifo sin shaping, así que un GetGlyphs sobre los
            // caracteres únicos de cada tipografía da los mismos glifos que mapear texto por texto.
            var used = new Dictionary<SKTypeface, HashSet<ushort>>(ReferenceEqualityComparer.Instance);
            var buffer = new StringBuilder();
            Span<char> utf16 = stackalloc char[2];
            foreach (var (typeface, set) in codePoints)
            {
                buffer.Clear();
                foreach (var codePoint in set)
                    buffer.Append(utf16[..new Rune(codePoint).EncodeToUtf16(utf16)]);
                used[typeface] = [.. FontFor(typeface).GetGlyphs(buffer.ToString())];
            }
            return FromGlyphUsage(used);
        }
        finally
        {
            foreach (var font in fonts.Values) font.Dispose();
        }

        SKFont FontFor(SKTypeface typeface)
        {
            if (!fonts.TryGetValue(typeface, out var font))
                fonts[typeface] = font = SkiaFonts.CreateFont(typeface, 12);
            return font;
        }
    }

    /// <summary>Crea las fuentes recortadas a partir de los glifos que usa cada tipografía.</summary>
    public static FontSubsetPlan FromGlyphUsage(IReadOnlyDictionary<SKTypeface, HashSet<ushort>> used)
    {
        var plan = new FontSubsetPlan();
        foreach (var (typeface, glyphs) in used)
            plan.TryAddSubset(typeface, glyphs);
        return plan;
    }

    public SKTypeface Map(SKTypeface typeface) =>
        _subsets.TryGetValue(typeface, out var subset) ? subset : typeface;

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        _owned.Clear();
        _subsets.Clear();
    }

    private void TryAddSubset(SKTypeface original, HashSet<ushort> glyphs)
    {
        byte[]? bytes;
        try
        {
            using var stream = original.OpenStream(out var faceIndex);
            if (stream is null) return;
            using var source = SKData.Create(stream);
            if (source is null) return;
            bytes = TrueTypeSubsetter.Subset(source.AsSpan(), faceIndex, glyphs);
        }
        catch (Exception)
        {
            // El recorte solo reduce el tamaño del archivo: ante una fuente que no sabemos leer
            // se embebe completa en lugar de fallar el documento.
            return;
        }
        if (bytes is null) return;

        var data = SKData.CreateCopy(bytes);
        var subset = SKTypeface.FromData(data);
        if (subset is null || !SameAdvances(original, subset, glyphs))
        {
            subset?.Dispose();
            data.Dispose();
            return;
        }

        _subsets[original] = subset;
        _owned.Add(subset);
        _owned.Add(data);
    }

    // Red de seguridad: si la fuente recortada no reproduce exactamente los avances de los glifos
    // usados, se embebe la original. Un PDF más pesado es preferible a uno mal maquetado.
    private static bool SameAdvances(SKTypeface original, SKTypeface subset, HashSet<ushort> glyphs)
    {
        if (subset.GlyphCount != original.GlyphCount) return false;

        var ids = MemoryMarshal.AsBytes(glyphs.ToArray().AsSpan());
        using var originalFont = SkiaFonts.CreateFont(original, 1000);
        using var subsetFont = SkiaFonts.CreateFont(subset, 1000);
        var expected = originalFont.GetGlyphWidths(ids, SKTextEncoding.GlyphId, null);
        var actual = subsetFont.GetGlyphWidths(ids, SKTextEncoding.GlyphId, null);
        return expected.AsSpan().SequenceEqual(actual);
    }
}
