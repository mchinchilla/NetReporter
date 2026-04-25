using NetReporter.Core.Elements;
using NetReporter.Core.Expressions;

namespace NetReporter.Core.Bands;

public enum BandKind
{
    ReportHeader,
    PageHeader,
    Detail,
    PageFooter,
    ReportFooter
}

public abstract record Band(BandKind Kind)
{
    public required double Height { get; init; }
    public bool PrintOnFirstPage { get; init; } = true;
    public bool PrintOnLastPage  { get; init; } = true;
    public IExpression<bool>? Visible { get; init; }
    public required IReadOnlyList<ReportElement> Elements { get; init; }

    /// <summary>
    /// Si <c>true</c>, la banda crece dinámicamente para acomodar todos sus elementos.
    /// El cursor Y avanza por <c>max(Height, naturalHeight)</c>, donde naturalHeight es el
    /// max bottom de los elementos (tras aplicar word-wrap y auto-height de TextElement).
    /// </summary>
    public bool AutoHeight { get; init; } = false;
}

public sealed record ReportHeaderBand() : Band(BandKind.ReportHeader);
public sealed record PageHeaderBand()   : Band(BandKind.PageHeader);
public sealed record PageFooterBand()   : Band(BandKind.PageFooter);
public sealed record ReportFooterBand() : Band(BandKind.ReportFooter);

/// <summary>
/// En el prototipo, el DetailBand es una banda de contenido fijo (no itera filas).
/// La "tabla de líneas" es un TableElement dentro de esta banda que sí itera sus filas internas.
/// Versión completa iteraría con IDataSource&lt;TRow&gt;.
/// </summary>
public sealed record DetailBand() : Band(BandKind.Detail);
