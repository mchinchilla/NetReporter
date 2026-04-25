using NetReporter.Core.Expressions;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Elements;

/// <summary>Tipo de agregación a aplicar sobre el binding de una columna.</summary>
public enum AggregateKind
{
    Sum,
    Count,
    Avg
}

/// <summary>
/// Banda que se inserta antes de cada grupo (encabezado por grupo).
/// El contenido es una expresión que se evalúa con un contexto que expone
/// el valor del agrupador via <c>{{ #group }}</c> y el conteo via <c>{{ #count }}</c>.
/// </summary>
public sealed record GroupHeader
{
    public required double Height { get; init; }
    public required IExpression<string> Content { get; init; }
    public StyleRef Style { get; init; } = new("GroupHeader");
}

/// <summary>
/// Banda que se inserta después de las filas de cada grupo (subtotales).
/// Las celdas mapean 1-a-1 con las columnas de la tabla por índice (mismo width/align).
/// Una celda <c>null</c> o sin <c>Content</c> ni <c>Aggregate</c> queda en blanco.
/// </summary>
public sealed record GroupFooter
{
    public required double Height { get; init; }
    public required IReadOnlyList<GroupFooterCell?> Cells { get; init; }
    public StyleRef Style { get; init; } = new("GroupFooter");
}

/// <summary>
/// Celda del group footer. Soporta dos modos exclusivos:
/// <list type="bullet">
///   <item><c>Aggregate</c> presente: aplica Sum/Count/Avg al binding de la columna paralela.</item>
///   <item><c>Content</c> presente: evalúa la expresión con acceso a <c>{{ #group }}</c> y <c>{{ #count }}</c>.</item>
/// </list>
/// </summary>
public sealed record GroupFooterCell
{
    public IExpression<string>? Content { get; init; }
    public AggregateKind? Aggregate { get; init; }
    public string? Format { get; init; }
    public TextAlignment? Align { get; init; }
}
