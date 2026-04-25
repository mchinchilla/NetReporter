using NetReporter.Core.Styles;

namespace NetReporter.Core.Layout;

/// <summary>
/// Abstracción para medir y dividir texto. La implementación default usa estimación
/// lineal (sin dependencias). Para medición precisa, el cliente puede pasar
/// SkiaTextMeasurer (en NetReporter.Svg) que usa SkiaSharp.
/// </summary>
public interface ITextMeasurer
{
    /// <summary>Mide el ancho aproximado del texto con el estilo dado, en puntos.</summary>
    double MeasureWidth(string text, ResolvedStyle style);

    /// <summary>Altura de una línea con el estilo dado, en puntos (line-height).</summary>
    double LineHeight(ResolvedStyle style);

    /// <summary>
    /// Divide <paramref name="text"/> en líneas que caben en <paramref name="maxWidth"/>.
    /// Respeta line breaks explícitos (<c>\n</c>) y rompe por palabra. Si una palabra excede
    /// el ancho disponible, se rompe por carácter.
    /// </summary>
    IReadOnlyList<string> WrapLines(string text, ResolvedStyle style, double maxWidth);
}

/// <summary>
/// Medidor por estimación lineal (chars × fontSize × factor). Sin dependencias externas.
/// Suficiente para layout aproximado; para medición real usa SkiaTextMeasurer.
/// </summary>
public sealed class EstimateTextMeasurer : ITextMeasurer
{
    public static readonly EstimateTextMeasurer Instance = new();

    // Factor promedio para Helvetica/Arial (proporcional al tipo de letra).
    private const double AverageCharWidthFactor = 0.55;

    public double MeasureWidth(string text, ResolvedStyle style)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var factor = style.Weight == FontWeight.Bold ? AverageCharWidthFactor * 1.05 : AverageCharWidthFactor;
        return text.Length * style.FontSize * factor;
    }

    public double LineHeight(ResolvedStyle style) => style.FontSize * 1.2;

    public IReadOnlyList<string> WrapLines(string text, ResolvedStyle style, double maxWidth)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrEmpty(text)) return new[] { string.Empty };
        if (maxWidth <= 0) return new[] { text };

        var result = new List<string>();
        // Respetar \n explícitos: cada segmento se wrappea independientemente.
        foreach (var hardLine in text.Split('\n'))
        {
            var line = hardLine.TrimEnd('\r');
            if (line.Length == 0) { result.Add(string.Empty); continue; }
            WrapSingle(line, style, maxWidth, result);
        }
        return result;
    }

    private void WrapSingle(string line, ResolvedStyle style, double maxWidth, List<string> output)
    {
        // Algoritmo word-by-word. Acumula palabras hasta exceder maxWidth.
        // Si una palabra individual excede, se corta por carácter.
        var words = line.Split(' ');
        var current = new System.Text.StringBuilder();
        double currentWidth = 0;
        var spaceWidth = MeasureWidth(" ", style);

        foreach (var word in words)
        {
            var wordWidth = MeasureWidth(word, style);

            // Caso 1: palabra cabe (con espacio si no es la primera).
            var addSpace = current.Length > 0;
            var widthIfAdded = currentWidth + (addSpace ? spaceWidth : 0) + wordWidth;
            if (widthIfAdded <= maxWidth || current.Length == 0 && wordWidth <= maxWidth)
            {
                if (addSpace) { current.Append(' '); currentWidth += spaceWidth; }
                current.Append(word);
                currentWidth += wordWidth;
                continue;
            }

            // Caso 2: la línea actual ya tiene contenido → cerrarla y empezar nueva con la palabra.
            if (current.Length > 0)
            {
                output.Add(current.ToString());
                current.Clear();
                currentWidth = 0;
            }

            // Caso 3: la palabra por sí sola excede → corte por carácter.
            if (wordWidth > maxWidth)
            {
                BreakWordByChar(word, style, maxWidth, output, current, ref currentWidth);
            }
            else
            {
                current.Append(word);
                currentWidth = wordWidth;
            }
        }

        if (current.Length > 0) output.Add(current.ToString());
    }

    private void BreakWordByChar(
        string word, ResolvedStyle style, double maxWidth,
        List<string> output, System.Text.StringBuilder current, ref double currentWidth)
    {
        foreach (var ch in word)
        {
            var chWidth = MeasureWidth(ch.ToString(), style);
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
}
