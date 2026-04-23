namespace NetReporter.Core.DataBinding;

/// <summary>
/// Binding tipado sin reflection. El JIT inlinea la expresión agresivamente.
/// </summary>
public interface IDataBinding<TRow, TValue>
{
    TValue Evaluate(TRow row);
}

public sealed class FuncBinding<TRow, TValue> : IDataBinding<TRow, TValue>
{
    private readonly Func<TRow, TValue> _selector;
    public FuncBinding(Func<TRow, TValue> selector) => _selector = selector;
    public TValue Evaluate(TRow row) => _selector(row);
}

public static class Bind
{
    public static IDataBinding<TRow, TValue> From<TRow, TValue>(Func<TRow, TValue> selector) =>
        new FuncBinding<TRow, TValue>(selector);
}
