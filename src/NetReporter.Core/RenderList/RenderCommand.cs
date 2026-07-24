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

public sealed record DrawImageCommand(
    Rect Bounds,
    byte[] Data,
    string MimeType,
    Elements.ImageFit Fit = Elements.ImageFit.Contain) : RenderCommand(Bounds);
