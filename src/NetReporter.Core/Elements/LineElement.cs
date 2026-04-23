using NetReporter.Core.Primitives;

namespace NetReporter.Core.Elements;

public enum LineOrientation { Horizontal, Vertical }

public sealed record LineElement : ReportElement
{
    public LineOrientation Orientation { get; init; } = LineOrientation.Horizontal;
    public double Thickness { get; init; } = 0.5;
    public Color Color { get; init; } = Color.Black;
}
