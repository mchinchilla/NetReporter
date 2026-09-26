using System.Globalization;

namespace NetReporter.Core.Expressions;

public interface IEvaluationContext
{
    int PageNumber { get; }
    int TotalPages { get; }
    int RowIndex { get; }
    object? CurrentRow { get; }
    object? GetParameter(string name);
    object? GetAggregate(string name);

    /// <summary>
    /// Valor del agrupador para el grupo actual. <c>null</c> fuera de un grupo o
    /// en tablas no agrupadas. Default: null.
    /// </summary>
    object? GroupKey => null;

    /// <summary>Conteo de filas en el grupo actual. Default: 0.</summary>
    int GroupRowCount => 0;

    /// <summary>
    /// Claves de todos los niveles de agrupación activos, del más externo (índice 0) al actual.
    /// Vacío fuera de una tabla agrupada. Lo usa la función DSL <c>group(n)</c> en grupos anidados.
    /// </summary>
    IReadOnlyList<object?> GroupKeys => Array.Empty<object?>();

    /// <summary>Cultura del reporte, para formatear números/fechas en expresiones. Default: invariante.</summary>
    CultureInfo Culture => CultureInfo.InvariantCulture;
}

public interface IExpression<TValue>
{
    TValue Evaluate(IEvaluationContext context);
}
