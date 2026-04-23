using NetReporter.Core.Primitives;

namespace NetReporter.Core.Styles;

public enum FontWeight      { Normal, Bold }
public enum FontStyleKind   { Normal, Italic }
public enum TextAlignment   { Left, Center, Right, Justify }
public enum VerticalAlignment { Top, Center, Bottom }
public enum BorderStyleKind { Solid, Dashed, Dotted }

public readonly record struct BorderLine(
    double Thickness,
    Color Color,
    BorderStyleKind Style = BorderStyleKind.Solid);

public sealed record BorderSet(
    BorderLine? Left = null,
    BorderLine? Top = null,
    BorderLine? Right = null,
    BorderLine? Bottom = null)
{
    public static BorderSet All(double thickness, Color color)
    {
        var line = new BorderLine(thickness, color);
        return new BorderSet(line, line, line, line);
    }

    public static BorderSet OnlyTop(double thickness, Color color) =>
        new(Top: new BorderLine(thickness, color));

    public static BorderSet OnlyBottom(double thickness, Color color) =>
        new(Bottom: new BorderLine(thickness, color));

    public static BorderSet Horizontal(double thickness, Color color)
    {
        var line = new BorderLine(thickness, color);
        return new BorderSet(Top: line, Bottom: line);
    }
}
