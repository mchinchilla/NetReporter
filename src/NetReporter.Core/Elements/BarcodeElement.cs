using NetReporter.Core.Expressions;
using NetReporter.Core.Primitives;

namespace NetReporter.Core.Elements;

/// <summary>Tipos de código de barra/QR soportados (subset común).</summary>
public enum BarcodeFormat
{
    QrCode,
    Code128,
    Code39,
    Ean13
}

/// <summary>
/// Elemento que codifica un valor en un código de barra o QR. El layout engine lo
/// convierte en N <see cref="RenderList.DrawRectangleCommand"/> (módulos), generando
/// un código vectorial — escalable, perfecto en PDF/SVG/HTML.
/// </summary>
public sealed record BarcodeElement : ReportElement
{
    /// <summary>El valor a codificar — evaluado contra el contexto al emitir.</summary>
    public required IExpression<string> Value { get; init; }

    public BarcodeFormat Format { get; init; } = BarcodeFormat.QrCode;

    /// <summary>Color de los módulos (default negro).</summary>
    public Color Foreground { get; init; } = Color.Black;

    /// <summary>Color del fondo (default transparente).</summary>
    public Color Background { get; init; } = Color.Transparent;

    /// <summary>Margen interno (quiet zone) en módulos. Default 1.</summary>
    public int QuietZoneModules { get; init; } = 1;
}
