using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Elements;

public sealed record RectangleElement : ReportElement
{
    public Color? Fill { get; init; }
    public BorderLine? BorderLine { get; init; }
}
