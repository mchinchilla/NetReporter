namespace NetReporter.Core.Expressions;

/// <summary>Helpers para crear expresiones sin ceremony.</summary>
public static class Expr
{
    public static IExpression<T> Literal<T>(T value) => new LiteralExpression<T>(value);

    public static IExpression<T> Of<T>(Func<IEvaluationContext, T> fn) =>
        new FuncExpression<T>(fn);

    public static IExpression<string> Str(string value) => new LiteralExpression<string>(value);

    public static IExpression<string> PageNumber =>
        Of(ctx => ctx.PageNumber.ToString());

    public static IExpression<string> TotalPages =>
        Of(ctx => ctx.TotalPages.ToString());

    public static IExpression<string> PageOfTotal =>
        Of(ctx => $"Página {ctx.PageNumber} de {ctx.TotalPages}");
}

internal sealed class LiteralExpression<T> : IExpression<T>
{
    private readonly T _value;
    public LiteralExpression(T value) => _value = value;
    public T Evaluate(IEvaluationContext context) => _value;
}

internal sealed class FuncExpression<T> : IExpression<T>
{
    private readonly Func<IEvaluationContext, T> _fn;
    public FuncExpression(Func<IEvaluationContext, T> fn) => _fn = fn;
    public T Evaluate(IEvaluationContext context) => _fn(context);
}
