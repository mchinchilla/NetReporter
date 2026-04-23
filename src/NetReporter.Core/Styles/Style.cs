using NetReporter.Core.Primitives;

namespace NetReporter.Core.Styles;

public sealed record Style(
    string? FontFamily = null,
    double? FontSize = null,
    FontWeight? Weight = null,
    FontStyleKind? Italic = null,
    Color? Foreground = null,
    Color? Background = null,
    TextAlignment? TextAlign = null,
    VerticalAlignment? VerticalAlign = null,
    Thickness? Padding = null,
    BorderSet? Border = null,
    string? NumberFormat = null,
    string? BasedOn = null);

/// <summary>
/// Estilo sin nullables — todos los campos resueltos. Lo que consumen los renderers.
/// </summary>
public sealed record ResolvedStyle(
    string FontFamily,
    double FontSize,
    FontWeight Weight,
    FontStyleKind Italic,
    Color Foreground,
    Color Background,
    TextAlignment TextAlign,
    VerticalAlignment VerticalAlign,
    Thickness Padding,
    BorderSet? Border,
    string? NumberFormat);

public readonly record struct StyleRef(string Name)
{
    public static readonly StyleRef Default = new("Default");
    public bool IsEmpty => string.IsNullOrEmpty(Name);
}
