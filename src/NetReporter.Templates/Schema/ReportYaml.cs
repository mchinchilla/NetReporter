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

    // line
    public string? Orientation { get; set; }
    public double? Thickness { get; set; }
    public string? Color { get; set; }

    // rectangle
    public string? Fill { get; set; }
    public BorderLineYaml? BorderLine { get; set; }
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
}
