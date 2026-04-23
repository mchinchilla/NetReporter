using NetReporter.Core.Styles;

namespace NetReporter.Core.Layout;

/// <summary>
/// Medición ultra-simple de texto. En producción medimos con SkiaSharp o System.Drawing
/// para obtener métricas reales. Para el prototipo, una aproximación lineal es suficiente
/// porque QuestPDF hará el text layout real al pintar.
/// </summary>
public static class TextMeasurer
{
    private const double AverageCharWidthFactor = 0.55; // aprox para Helvetica

    public static double EstimateWidth(string text, ResolvedStyle style) =>
        text.Length * style.FontSize * AverageCharWidthFactor;

    public static double EstimateHeight(ResolvedStyle style) =>
        style.FontSize * 1.2; // line-height aproximado
}
