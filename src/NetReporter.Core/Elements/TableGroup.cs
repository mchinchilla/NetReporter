using System.Globalization;
using NetReporter.Core.DataBinding;
using NetReporter.Core.Expressions;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Elements;

/// <summary>Tipo de agregación a aplicar sobre el binding de una columna.</summary>
public enum AggregateKind
{
    Sum,
    Count,
    Avg,
    Min,
    Max
}

/// <summary>Un nivel de agrupación de una tabla: clave + encabezado/pie opcionales.</summary>
public sealed record TableGroupLevel<TRow>
{
    public required IDataBinding<TRow, object?> By { get; init; }
    public GroupHeader? Header { get; init; }
    public GroupFooter? Footer { get; init; }
}

/// <summary>Utilidades de agrupación y agregación compartidas por el layout y por el renderer XLSX.</summary>
public static class TableGrouping
{
    /// <summary>
    /// Parte las filas en grupos de filas CONSECUTIVAS con la misma clave (no reordena: los datos
    /// deben venir ordenados por la clave, igual que un reporte con cortes de control).
    /// </summary>
    public static List<(object? Key, List<TRow> Rows)> Partition<TRow>(
        IEnumerable<TRow> rows, IDataBinding<TRow, object?> by)
    {
        var result = new List<(object? Key, List<TRow> Rows)>();
        foreach (var row in rows)
        {
            var key = by.Evaluate(row);
            if (result.Count > 0 && KeysEqual(result[^1].Key, key))
                result[^1].Rows.Add(row);
            else
                result.Add((key, new List<TRow> { row }));
        }
        return result;
    }

    public static bool KeysEqual(object? a, object? b) =>
        a is null ? b is null : b is not null && a.Equals(b);

    /// <summary>Aplica Sum/Count/Avg/Min/Max sobre una secuencia de valores (conversión segura a double).</summary>
    public static double Aggregate(AggregateKind kind, IEnumerable<object?> values)
    {
        if (kind == AggregateKind.Count)
            return values.Count(v => v is not null);

        var nums = values.Select(ToDouble).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (nums.Count == 0) return 0;
        return kind switch
        {
            AggregateKind.Sum => nums.Sum(),
            AggregateKind.Avg => nums.Average(),
            AggregateKind.Min => nums.Min(),
            AggregateKind.Max => nums.Max(),
            _ => 0
        };
    }

    public static double? ToDouble(object? v) => v switch
    {
        null      => null,
        double d  => d,
        float f   => f,
        int i     => i,
        long l    => l,
        decimal m => (double)m,
        string s  => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null,
        _ => null
    };
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
///   <item><c>Aggregate</c> presente: aplica Sum/Count/Avg/Min/Max al binding de la columna paralela.</item>
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
