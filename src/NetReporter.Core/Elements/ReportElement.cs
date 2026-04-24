using NetReporter.Core.Expressions;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Elements;

public abstract record ReportElement
{
    public required Rect Bounds { get; init; }
    public StyleRef Style { get; init; } = StyleRef.Default;
    public IExpression<bool>? Visible { get; init; }

    /// <summary>
    /// Ruta de origen del elemento en el template fuente (ej: "bands.1.elements.2").
    /// Lo asigna quien construye el IR — si es null, el elemento no es trazable al template.
    /// Lo consume el Designer para mapear clicks a edits del YAML.
    /// </summary>
    public string? SourcePath { get; init; }
}
