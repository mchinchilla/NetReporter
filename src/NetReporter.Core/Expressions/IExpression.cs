namespace NetReporter.Core.Expressions;

public interface IEvaluationContext
{
    int PageNumber { get; }
    int TotalPages { get; }
    int RowIndex { get; }
    object? CurrentRow { get; }
    object? GetParameter(string name);
    object? GetAggregate(string name);
}

public interface IExpression<TValue>
{
    TValue Evaluate(IEvaluationContext context);
}
