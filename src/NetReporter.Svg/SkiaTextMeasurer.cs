using System.Collections.Concurrent;
using NetReporter.Core.Layout;
using NetReporter.Core.Styles;
using SkiaSharp;

namespace NetReporter.Svg;

/// <summary>
/// <see cref="ITextMeasurer"/> implementado con SkiaSharp — resuelve las fuentes con
/// <see cref="SkiaFonts"/>, igual que el SvgRenderer / PdfRenderer, así que los anchos (incluidos
/// los tramos con fuente de respaldo) son consistentes con lo que se pinta.
/// </summary>
public sealed class SkiaTextMeasurer : ITextMeasurer
{
    public static readonly SkiaTextMeasurer Instance = new();

    // Una entrada por (family, size, weight, italic). El SKFont solo se usa en operaciones de
    // lectura (ContainsGlyphs, GetGlyphs), que Skia admite desde varios hilos; los anchos salen de
    // GlyphAdvances y las métricas se leen una sola vez, así que medir no pasa por el strike cache
    // de Skia (un lock global) y el layout de varios documentos en paralelo escala.
    private sealed record Entry(SKTypeface Typeface, SKFont Font, SKFontMetrics Metrics);

    private readonly ConcurrentDictionary<(string Family, double Size, FontWeight Weight, FontStyleKind Italic), Entry>
        _cache = new();

    public double MeasureWidth(string text, ResolvedStyle style)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var entry = GetEntry(style);
        return SkiaFonts.MeasureText(text, entry.Typeface, entry.Font);
    }

    public double LineHeight(ResolvedStyle style)
    {
        var m = GetEntry(style).Metrics;
        return -m.Ascent + m.Descent + m.Leading;
    }

    public IReadOnlyList<string> WrapLines(string text, ResolvedStyle style, double maxWidth)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrEmpty(text)) return new[] { string.Empty };
        if (maxWidth <= 0) return new[] { text };

        var entry = GetEntry(style);
        var result = new List<string>();
        foreach (var hardLine in text.Split('\n'))
        {
            var line = hardLine.TrimEnd('\r');
            if (line.Length == 0) { result.Add(string.Empty); continue; }
            WrapSingle(line, entry, maxWidth, result);
        }
        return result;
    }

    private static void WrapSingle(string line, Entry font, double maxWidth, List<string> output)
    {
        var words = line.Split(' ');
        var current = new System.Text.StringBuilder();
        double currentWidth = 0;
        var spaceWidth = Measure(" ", font);

        foreach (var word in words)
        {
            var wordWidth = Measure(word, font);
            var addSpace = current.Length > 0;
            var widthIfAdded = currentWidth + (addSpace ? spaceWidth : 0) + wordWidth;

            if (widthIfAdded <= maxWidth || (current.Length == 0 && wordWidth <= maxWidth))
            {
                if (addSpace) { current.Append(' '); currentWidth += spaceWidth; }
                current.Append(word);
                currentWidth += wordWidth;
                continue;
            }

            if (current.Length > 0)
            {
                output.Add(current.ToString());
                current.Clear();
                currentWidth = 0;
            }

            if (wordWidth > maxWidth)
            {
                BreakWordByChar(word, font, maxWidth, output, current, ref currentWidth);
            }
            else
            {
                current.Append(word);
                currentWidth = wordWidth;
            }
        }

        if (current.Length > 0) output.Add(current.ToString());
    }

    private static void BreakWordByChar(
        string word, Entry font, double maxWidth,
        List<string> output, System.Text.StringBuilder current, ref double currentWidth)
    {
        foreach (var ch in word)
        {
            var chWidth = Measure(ch.ToString(), font);
            if (currentWidth + chWidth > maxWidth && current.Length > 0)
            {
                output.Add(current.ToString());
                current.Clear();
                currentWidth = 0;
            }
            current.Append(ch);
            currentWidth += chWidth;
        }
    }

    private static float Measure(string text, Entry font) => SkiaFonts.MeasureText(text, font.Typeface, font.Font);

    private Entry GetEntry(ResolvedStyle style)
    {
        var key = (style.FontFamily, style.FontSize, style.Weight, style.Italic);
        return _cache.GetOrAdd(key, static k =>
        {
            var typeface = SkiaFonts.Resolve(k.Family, k.Weight, k.Italic);
            var font = SkiaFonts.CreateFont(typeface, (float)k.Size);
            return new Entry(typeface, font, font.Metrics);
        });
    }
}
