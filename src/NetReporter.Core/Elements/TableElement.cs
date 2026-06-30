using NetReporter.Core.DataBinding;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Elements;

public enum TableHeaderMode { PrintOnce, RepeatOnPageBreak }

/// <summary>Non-generic layout hints the band layout reads without knowing the row type.</summary>
public interface ITableLayoutHints
{
    /// <summary>See <see cref="TableElement{TRow}.SuppressAdvance"/>.</summary>
    bool SuppressAdvance { get; }
}

/// <summary>
/// Tabla con columnas tipadas. Las filas provienen del DataSource del DetailBand que la contiene,
/// O se pueden suministrar inline aquí (para el prototipo usamos inline).
/// </summary>
public sealed record TableElement<TRow> : ReportElement, ITableLayoutHints
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

    /// <summary>Optional single full-width rule drawn under the header row (and repeated when the header
    /// repeats on a page break). Unlike a header-cell border, this draws ONE horizontal line across the
    /// whole table — no per-cell box, no vertical separators. Null = none. Use for clean financial-report
    /// headers (rule under the column titles).</summary>
    public BorderLine? HeaderRule { get; init; }

    /// <summary>Optional hairline drawn at the bottom edge of each data row, spanning the whole table
    /// width — the classic "lined ledger" look. Applies to normal and grouped rows. Null = none.</summary>
    public BorderLine? RowSeparator { get; init; }

    /// <summary>Optional per-row style selector for "typed rows" (financial statements: section / account /
    /// subtotal / total, each styled differently). When set, each row's value is evaluated, looked up in
    /// <see cref="RowStyleMap"/>, and that style is used instead of <see cref="RowStyle"/>/<see cref="AlternateRowStyle"/>.
    /// A row whose key is missing from the map (or whose mapped style isn't defined) falls back to the normal
    /// row style. Null = uniform rows (current behavior).</summary>
    public IDataBinding<TRow, object?>? RowStyleSelector { get; init; }

    /// <summary>Maps a <see cref="RowStyleSelector"/> key (e.g. a row's "kind") to a style. Ignored when the
    /// selector is null.</summary>
    public IReadOnlyDictionary<string, StyleRef>? RowStyleMap { get; init; }

    /// <summary>When true, a styled row's background is painted as ONE edge-to-edge rectangle spanning the
    /// whole table width (clean total/section bands), instead of per-cell fills that can leave seams.
    /// Applies to every data row that has a non-transparent background. Default false (per-cell, current
    /// behavior).</summary>
    public bool FullRowBackground { get; init; }

    /// <summary>When true, the table draws normally but does NOT advance the band's shared vertical cursor,
    /// so a following table starts at the SAME vertical position — i.e. side by side (use distinct
    /// <c>Bounds.X</c>/<c>Bounds.Width</c> per table). The band's auto-height still grows to contain it.
    /// Use for two-column layouts like a T-account balance sheet. Default false. Note: a suppressed table
    /// is expected to fit on one page (it does not drive pagination).</summary>
    public bool SuppressAdvance { get; init; }

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
    TextAlignment Align = TextAlignment.Left,
    // Optional per-column style: merged OVER the resolved row style for that cell (e.g. a mono font /
    // muted color for a code or amount column, regardless of the row's typed style). Null = inherit row.
    StyleRef? Style = null);
