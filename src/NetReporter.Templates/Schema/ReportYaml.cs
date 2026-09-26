namespace NetReporter.Templates.Schema;

/// <summary>Modelo de deserialización YAML — no es el IR, se mapea a ReportDefinition en el loader.</summary>
public sealed class ReportYaml
{
    public string? Name { get; set; }
    public string? Title { get; set; }
    public string? FileName { get; set; }   // plantilla con {{ $.path }}
    public PageYaml? Page { get; set; }
    public string? Culture { get; set; }
    public string? Theme { get; set; }
    public Dictionary<string, StyleYaml>? Styles { get; set; }
    public List<BandYaml>? Bands { get; set; }
}

public sealed class PageYaml
{
    public string? Size { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public string? Orientation { get; set; }
    public MarginsYaml? Margins { get; set; }
}

public sealed class MarginsYaml
{
    public string? All { get; set; }
    public string? Horizontal { get; set; }
    public string? Vertical { get; set; }
    public string? Left { get; set; }
    public string? Top { get; set; }
    public string? Right { get; set; }
    public string? Bottom { get; set; }
}

public sealed class StyleYaml
{
    public string? BasedOn { get; set; }
    public string? FontFamily { get; set; }
    public double? FontSize { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public string? Foreground { get; set; }
    public string? Background { get; set; }
    public string? Align { get; set; }
    public string? VAlign { get; set; }
    public MarginsYaml? Padding { get; set; }
    public BorderYaml? Border { get; set; }
    public string? Format { get; set; }
}

public sealed class BorderYaml
{
    public BorderLineYaml? Top { get; set; }
    public BorderLineYaml? Bottom { get; set; }
    public BorderLineYaml? Left { get; set; }
    public BorderLineYaml? Right { get; set; }
    public BorderLineYaml? All { get; set; }
    public BorderLineYaml? HorizontalEdges { get; set; }
}

public sealed class BorderLineYaml
{
    public double Thickness { get; set; } = 0.5;
    public string? Color { get; set; }
}

public sealed class BandYaml
{
    public string? Kind { get; set; }
    public double Height { get; set; }
    public bool? AutoHeight { get; set; }
    public bool? KeepTogether { get; set; }

    /// <summary>Condición DSL; si es falsa la banda no se emite (p. ej. <c>"count($.notas[*]) > 0"</c>).</summary>
    public string? Visible { get; set; }

    /// <summary>PageHeader/PageFooter: imprimir en la primera / última página (default true).</summary>
    public bool? PrintOnFirstPage { get; set; }
    public bool? PrintOnLastPage { get; set; }

    public List<ElementYaml>? Elements { get; set; }
}

public sealed class ElementYaml
{
    public string? Type { get; set; }
    public BoundsYaml? Bounds { get; set; }

    /// <summary>Nombre de estilo, o template dinámico: <c>"{{ $.saldo &lt; 0 ? 'Negativo' : 'Normal' }}"</c>.</summary>
    public string? Style { get; set; }

    /// <summary>Condición DSL; si es falsa el elemento no se emite (p. ej. <c>"$.descuento > 0"</c>).</summary>
    public string? Visible { get; set; }

    // text
    public string? Content { get; set; }
    public bool? WordWrap { get; set; }
    public bool? AutoHeight { get; set; }

    // table
    public string? Rows { get; set; }
    public double? HeaderHeight { get; set; }
    public double? RowHeight { get; set; }
    public string? HeaderStyle { get; set; }
    public string? RowStyle { get; set; }
    public string? AlternateRowStyle { get; set; }
    public string? HeaderMode { get; set; }
    public List<TableColumnYaml>? Columns { get; set; }

    /// <summary>Single full-width rule under the header row (no per-cell box / vertical lines).</summary>
    public BorderLineYaml? HeaderRule { get; set; }

    /// <summary>Full-width hairline at the bottom of each data row (lined-ledger look).</summary>
    public BorderLineYaml? RowSeparator { get; set; }

    /// <summary>JSON path whose value selects a per-row style (typed rows). Looked up in <see cref="RowStyleMap"/>.</summary>
    public string? RowStyleBinding { get; set; }

    /// <summary>Maps a <see cref="RowStyleBinding"/> value to a named style (e.g. {"total": "TotalRow"}).</summary>
    public Dictionary<string, string>? RowStyleMap { get; set; }

    /// <summary>Paint each styled row's background as one edge-to-edge band (no per-cell seams).</summary>
    public bool? FullRowBackground { get; set; }

    /// <summary>Draw the table but don't advance the vertical cursor (side-by-side tables).</summary>
    public bool? SuppressAdvance { get; set; }

    /// <summary>Border drawn around the whole table, with optional corner radius (a "card").</summary>
    public BorderLineYaml? OuterBorder { get; set; }

    /// <summary>Corner radius (points) for <see cref="OuterBorder"/>.</summary>
    public double? CornerRadius { get; set; }

    /// <summary>
    /// Esquinas que redondea <see cref="CornerRadius"/>. Acepta <c>all</c> (por defecto),
    /// <c>none</c>, los atajos <c>top</c> / <c>bottom</c> / <c>left</c> / <c>right</c>, o una
    /// lista separada por comas de <c>topLeft, topRight, bottomRight, bottomLeft</c>.
    /// Sirve para apilar bandas que se lean como una sola tarjeta.
    /// </summary>
    public string? RoundedCorners { get; set; }

    // table groups
    public string? GroupBy { get; set; }
    public GroupHeaderYaml? GroupHeader { get; set; }
    public GroupFooterYaml? GroupFooter { get; set; }

    /// <summary>Agrupación multinivel (índice 0 = nivel externo). Tiene prioridad sobre groupBy.</summary>
    public List<GroupLevelYaml>? Groups { get; set; }

    /// <summary>Fila de total general al final de la tabla (celdas como un groupFooter).</summary>
    public GroupFooterYaml? Summary { get; set; }

    // line
    public string? Orientation { get; set; }
    public double? Thickness { get; set; }
    public string? Color { get; set; }

    // rectangle
    public string? Fill { get; set; }
    public BorderLineYaml? BorderLine { get; set; }

    // image: source es path local o data URI; fit: contain | fill
    public string? Source { get; set; }
    public string? Fit { get; set; }

    // barcode: value (template), format: qr | code128 | code39 | ean13
    public string? Value { get; set; }
    public string? Format { get; set; }
    public string? BarcodeForeground { get; set; }
    public string? BarcodeBackground { get; set; }

    // chart: rows (JSON Path) + category + series; fill/borderLine = fondo/borde del chart
    /// <summary>bar | horizontalBar | line | area | pie | donut.</summary>
    public string? ChartType { get; set; }
    public string? Category { get; set; }
    public List<ChartSeriesYaml>? Series { get; set; }
    public string? Title { get; set; }
    /// <summary>none | top | bottom | right (ausente = automático).</summary>
    public string? Legend { get; set; }
    public bool? ShowValues { get; set; }
    public bool? ShowPercent { get; set; }
    public bool? ShowGrid { get; set; }
    public bool? ShowMarkers { get; set; }
    public bool? Stacked { get; set; }
    public string? ValueFormat { get; set; }
    public double? AxisMin { get; set; }
    public double? AxisMax { get; set; }
    public List<string>? Palette { get; set; }
    public double? InnerRadius { get; set; }
    public double? LineWidth { get; set; }
}

/// <summary>Formato del clipboard del Designer (copiar/pegar elementos entre bandas o templates).</summary>
public sealed class ClipboardYaml
{
    /// <summary>Marca de formato/versión: siempre 1.</summary>
    public int NetReporterClipboard { get; set; }
    public List<ClipboardItemYaml>? Items { get; set; }
}

public sealed class ClipboardItemYaml
{
    /// <summary>Banda de origen (se usa al pegar sin banda destino).</summary>
    public int Band { get; set; }
    public ElementYaml? Element { get; set; }
}

public sealed class ChartSeriesYaml
{
    public string? Name { get; set; }
    /// <summary>Binding por fila: JSON Path (<c>$.total</c>) o expresión (<c>= $.total * 1.15</c>).</summary>
    public string? Value { get; set; }
    public string? Color { get; set; }
}

public sealed class GroupLevelYaml
{
    /// <summary>Clave del nivel: JSON Path o expresión <c>= …</c> evaluada por fila.</summary>
    public string? By { get; set; }
    public GroupHeaderYaml? Header { get; set; }
    public GroupFooterYaml? Footer { get; set; }
}

public sealed class GroupHeaderYaml
{
    public double Height { get; set; } = 18;
    public string? Content { get; set; }
    public string? Style { get; set; }
}

public sealed class GroupFooterYaml
{
    public double Height { get; set; } = 16;
    public string? Style { get; set; }
    public List<GroupFooterCellYaml?>? Cells { get; set; }
}

public sealed class GroupFooterCellYaml
{
    public string? Content { get; set; }
    public string? Aggregate { get; set; }   // "sum" | "count" | "avg" | "min" | "max"
    public string? Format { get; set; }
    public string? Align { get; set; }
}

public sealed class BoundsYaml
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed class TableColumnYaml
{
    public string? Header { get; set; }
    public string? Binding { get; set; }
    public double Width { get; set; }
    public string? Format { get; set; }
    public string? Align { get; set; }

    /// <summary>Optional per-column style, merged over the row style (e.g. a mono font for code/amount).</summary>
    public string? Style { get; set; }
}
