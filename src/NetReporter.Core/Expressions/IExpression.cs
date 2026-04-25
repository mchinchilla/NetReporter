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
}

public interface IExpression<TValue>
{
    TValue Evaluate(IEvaluationContext context);
}
