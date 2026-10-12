using NetReporter.Core.Styles;

namespace NetReporter.Core.Layout;

/// <summary>
/// Medición ultra-simple de texto: una aproximación lineal. Para métricas reales se pasa
/// SkiaTextMeasurer al LayoutEngine (los renderers SVG/PDF dibujan con esas mismas fuentes).
/// </summary>
public static class TextMeasurer
{
    private const double AverageCharWidthFactor = 0.55; // aprox para Helvetica

    public static double EstimateWidth(string text, ResolvedStyle style) =>
        text.Length * style.FontSize * AverageCharWidthFactor;

    public static double EstimateHeight(ResolvedStyle style) =>
        style.FontSize * 1.2; // line-height aproximado
}
