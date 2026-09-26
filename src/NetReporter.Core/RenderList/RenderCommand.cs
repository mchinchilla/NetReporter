using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.RenderList;

public abstract record RenderCommand(Rect Bounds)
{
    /// <summary>
    /// Path en el template fuente del elemento que generó este comando.
    /// Se copia desde <see cref="Elements.ReportElement.SourcePath"/> al emitir.
    /// </summary>
    public string? SourcePath { get; init; }
}

public sealed record DrawTextCommand(
    Rect Bounds,
    string Text,
    ResolvedStyle Style) : RenderCommand(Bounds);

public sealed record DrawLineCommand(
    Point From,
    Point To,
    double Thickness,
    Color Color) : RenderCommand(new Rect(
        Math.Min(From.X, To.X),
        Math.Min(From.Y, To.Y),
        Math.Abs(To.X - From.X),
        Math.Abs(To.Y - From.Y)));

public sealed record DrawRectangleCommand(
    Rect Bounds,
    Color? Fill,
    BorderLine? Border,
    double CornerRadius = 0,
    RectCorners Corners = RectCorners.All) : RenderCommand(Bounds);

/// <summary>
/// Polilínea (abierta) o polígono (cerrado) de segmentos rectos, con relleno y/o trazo opcionales.
/// Lo usan los charts (líneas, áreas, porciones de pastel aproximadas con arcos finos). Los arcos se
/// discretizan en el layout, así que los renderers solo necesitan "move/line/close".
/// </summary>
public sealed record DrawPathCommand(
    Rect Bounds,
    IReadOnlyList<Point> Points,
    bool Closed,
    Color? Fill,
    BorderLine? Stroke) : RenderCommand(Bounds)
{
    /// <summary>Crea el comando calculando <see cref="RenderCommand.Bounds"/> a partir de los puntos.</summary>
    public static DrawPathCommand Create(IReadOnlyList<Point> points, bool closed, Color? fill, BorderLine? stroke)
    {
        if (points.Count == 0) return new DrawPathCommand(Rect.Empty, points, closed, fill, stroke);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        return new DrawPathCommand(new Rect(minX, minY, maxX - minX, maxY - minY), points, closed, fill, stroke);
    }
}

public sealed record DrawImageCommand(
    Rect Bounds,
    byte[] Data,
    string MimeType,
    Elements.ImageFit Fit = Elements.ImageFit.Contain) : RenderCommand(Bounds);
