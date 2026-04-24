namespace NetReporter.Templates;

/// <summary>
/// Patch aplicable a un elemento del template. Todos los campos son opcionales:
/// solo se aplican los no-null. Permite editar bounds y propiedades específicas por kind.
/// </summary>
public sealed class ElementPatch
{
    // Bounds (aplicables a todos los kinds)
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }

    // Común: style
    public string? Style { get; init; }

    // Específicos por kind
    public string? Content { get; init; }   // text
    public string? Color { get; init; }     // line
    public double? Thickness { get; init; } // line
    public string? Fill { get; init; }      // rectangle

    // table
    public string? Rows { get; init; }
    public string? HeaderMode { get; init; }
    public double? HeaderHeight { get; init; }
    public double? RowHeight { get; init; }
    public string? HeaderStyle { get; init; }
    public string? RowStyle { get; init; }
    public string? AlternateRowStyle { get; init; }
}
