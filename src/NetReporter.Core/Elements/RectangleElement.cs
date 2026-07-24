using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Elements;

public sealed record RectangleElement : ReportElement
{
    public Color? Fill { get; init; }
    public BorderLine? BorderLine { get; init; }

    /// <summary>Corner radius in points. 0 = square (default, unchanged for all existing reports).</summary>
    public double CornerRadius { get; init; }

    /// <summary>
    /// Esquinas a las que se aplica <see cref="CornerRadius"/>. Por defecto las cuatro, que es
    /// el comportamiento previo a que existiera este campo.
    /// </summary>
    public RectCorners Corners { get; init; } = RectCorners.All;
}
