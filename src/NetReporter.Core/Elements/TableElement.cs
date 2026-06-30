using NetReporter.Core.DataBinding;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Elements;

public enum TableHeaderMode { PrintOnce, RepeatOnPageBreak }

/// <summary>
/// Tabla con columnas tipadas. Las filas provienen del DataSource del DetailBand que la contiene,
/// O se pueden suministrar inline aquí (para el prototipo usamos inline).
/// </summary>
public sealed record TableElement<TRow> : ReportElement
{
    public required IReadOnlyList<TRow> Rows { get; init; }
    public required IReadOnlyList<TableColumn<TRow>> Columns { get; init; }
    public double RowHeight { get; init; } = 18;
    public double HeaderHeight { get; init; } = 22;
    public TableHeaderMode HeaderMode { get; init; } = TableHeaderMode.RepeatOnPageBreak;
    public StyleRef HeaderStyle { get; init; } = new("TableHeader");
    public StyleRef RowStyle { get; init; } = new("TableRow");
    public StyleRef? AlternateRowStyle { get; init; }

    /// <summary>Optional border drawn around the whole table (per page segment). Null = none.</summary>
    public BorderLine? OuterBorder { get; init; }

    /// <summary>Corner radius (points) for <see cref="OuterBorder"/>. 0 = square.</summary>
    public double CornerRadius { get; init; }

    /// <summary>
    /// Si está presente, las filas consecutivas con el mismo valor de <see cref="GroupBy"/>
    /// se agrupan. Entre grupos se emite <see cref="GroupHeader"/> y al final <see cref="GroupFooter"/>.
    /// </summary>
    public IDataBinding<TRow, object?>? GroupBy { get; init; }

    public GroupHeader? GroupHeader { get; init; }
    public GroupFooter? GroupFooter { get; init; }
}

public sealed record TableColumn<TRow>(
    string Header,
    IDataBinding<TRow, object?> Binding,
    double Width,                // puntos
    string? Format = null,
    TextAlignment Align = TextAlignment.Left);
