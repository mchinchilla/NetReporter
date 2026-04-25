using System.Collections.Concurrent;
using NetReporter.Core.Layout;
using NetReporter.Core.Styles;
using SkiaSharp;

namespace NetReporter.Svg;

/// <summary>
/// <see cref="ITextMeasurer"/> implementado con SkiaSharp — usa el mismo motor de tipografía
/// que el SvgRenderer / PdfRenderer, así que los anchos son consistentes con lo que se pinta.
/// </summary>
public sealed class SkiaTextMeasurer : ITextMeasurer
{
    public static readonly SkiaTextMeasurer Instance = new();

    // Cache de SKFont por (family, size, weight, italic) — SKFont no es thread-safe pero
    // las mediciones son rápidas y ConcurrentDictionary con SKFont thread-confined-de-facto
    // es aceptable para escenarios single-thread del layout (uno por LayoutEngine.Layout()).
    private readonly ConcurrentDictionary<(string Family, double Size, FontWeight Weight, FontStyleKind Italic), SKFont>
        _fontCache = new();

    public double MeasureWidth(string text, ResolvedStyle style)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var font = GetFont(style);
        return font.MeasureText(text);
    }

    public double LineHeight(ResolvedStyle style)
    {
        var font = GetFont(style);
        var m = font.Metrics;
        return -m.Ascent + m.Descent + m.Leading;
    }

    public IReadOnlyList<string> WrapLines(string text, ResolvedStyle style, double maxWidth)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrEmpty(text)) return new[] { string.Empty };
        if (maxWidth <= 0) return new[] { text };

        var font = GetFont(style);
        var result = new List<string>();
        foreach (var hardLine in text.Split('\n'))
        {
            var line = hardLine.TrimEnd('\r');
            if (line.Length == 0) { result.Add(string.Empty); continue; }
            WrapSingle(line, font, maxWidth, result);
        }
        return result;
    }

    private static void WrapSingle(string line, SKFont font, double maxWidth, List<string> output)
    {
        var words = line.Split(' ');
        var current = new System.Text.StringBuilder();
        double currentWidth = 0;
        var spaceWidth = font.MeasureText(" ");

        foreach (var word in words)
        {
            var wordWidth = font.MeasureText(word);
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
        string word, SKFont font, double maxWidth,
        List<string> output, System.Text.StringBuilder current, ref double currentWidth)
    {
        foreach (var ch in word)
        {
            var chWidth = font.MeasureText(ch.ToString());
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

    private SKFont GetFont(ResolvedStyle style)
    {
        var key = (style.FontFamily, style.FontSize, style.Weight, style.Italic);
        return _fontCache.GetOrAdd(key, k =>
        {
            var slant = k.Italic == FontStyleKind.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
            var weight = k.Weight == FontWeight.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
            var typeface = SKTypeface.FromFamilyName(k.Family, weight, SKFontStyleWidth.Normal, slant)
                           ?? SKTypeface.Default;
            return new SKFont(typeface, (float)k.Size);
        });
    }
}
