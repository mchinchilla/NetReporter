using System.Runtime.InteropServices;
using SkiaSharp;

namespace NetReporter.Svg;

/// <summary>
/// Avances de los glifos de una tipografía a un tamaño, tal como los entrega Skia para la fuente de
/// <see cref="SkiaFonts.CreateFont"/>, guardados la primera vez que se ve cada glifo. Con la tabla
/// caliente, medir o posicionar un texto es sumar floats en memoria, en el mismo orden en que lo
/// hace <c>SKFont.MeasureText</c>, así que el resultado es el mismo bit a bit.
/// <para>
/// Existe porque Skia protege su strike cache (las métricas de los glifos) con un lock global:
/// cada <c>MeasureText</c> y cada <c>DrawText</c> lo tomaban, y con varios documentos en paralelo
/// renderizar en 8 hilos rendía menos que en uno.
/// </para>
/// Thread-safe. Una instancia por (tipografía, tamaño) para todo el proceso: <see cref="SkiaFonts.Advances"/>.
/// </summary>
public sealed class GlyphAdvances
{
    private readonly SKFont _font;
    private readonly float[] _advances;   // NaN = aún no consultado a Skia

    internal GlyphAdvances(SKTypeface typeface, float size)
    {
        _font = SkiaFonts.CreateFont(typeface, size);
        _advances = new float[Math.Max(1, typeface.GlyphCount)];
        Array.Fill(_advances, float.NaN);
    }

    /// <summary>Glifos de <paramref name="text"/> según el cmap de la tipografía. No toca el strike cache.</summary>
    public ushort[] Glyphs(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _font.GetGlyphs(text);
    }

    /// <summary>Avance del glifo, en puntos.</summary>
    public float Of(ushort glyph)
    {
        if (glyph >= _advances.Length) return Measure(glyph);
        var advance = _advances[glyph];
        if (float.IsNaN(advance))
            _advances[glyph] = advance = Measure(glyph);   // carrera benigna: todos los hilos escriben el mismo valor
        return advance;
    }

    /// <summary>Ancho de <paramref name="text"/>: la misma suma, en el mismo orden, que <c>SKFont.MeasureText</c>.</summary>
    public float Width(string text) => Width(Glyphs(text));

    public float Width(ReadOnlySpan<ushort> glyphs)
    {
        float width = 0;
        foreach (var glyph in glyphs) width += Of(glyph);
        return width;
    }

    /// <summary>
    /// Posición de cada glifo sobre la línea base partiendo de (0, 0), igual que
    /// <see cref="SKFont.GetGlyphPositions(string, SKPoint)"/>; el origen del texto se pasa aparte al dibujar.
    /// </summary>
    public void Position(ReadOnlySpan<ushort> glyphs, Span<SKPoint> positions)
    {
        float x = 0;
        for (var i = 0; i < glyphs.Length; i++)
        {
            positions[i] = new SKPoint(x, 0);
            x += Of(glyphs[i]);
        }
    }

    private float Measure(ushort glyph)
    {
        ReadOnlySpan<ushort> id = [glyph];
        lock (_font)
            return _font.GetGlyphWidths(MemoryMarshal.AsBytes(id), SKTextEncoding.GlyphId, null)[0];
    }
}
