using System.Runtime.InteropServices;
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
        var used = new Dictionary<SKTypeface, HashSet<ushort>>(ReferenceEqualityComparer.Instance);
        foreach (var page in renderList.Pages)
        {
            foreach (var cmd in page.Commands)
            {
                if (cmd is not DrawTextCommand { Text.Length: > 0 } text) continue;
                foreach (var run in SkiaFonts.SplitRuns(text.Text, SkiaFonts.Resolve(text.Style)))
                {
                    if (!used.TryGetValue(run.Typeface, out var glyphs))
                        used[run.Typeface] = glyphs = [];
                    using var font = new SKFont(run.Typeface);
                    glyphs.UnionWith(font.GetGlyphs(run.Text));
                }
            }
        }

        return FromGlyphUsage(used);
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
