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
    public List<ElementYaml>? Elements { get; set; }
}

public sealed class ElementYaml
{
    public string? Type { get; set; }
    public BoundsYaml? Bounds { get; set; }
    public string? Style { get; set; }

    // text
    public string? Content { get; set; }

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

    // table groups
    public string? GroupBy { get; set; }
    public GroupHeaderYaml? GroupHeader { get; set; }
    public GroupFooterYaml? GroupFooter { get; set; }

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
    public string? Aggregate { get; set; }   // "sum" | "count" | "avg"
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
