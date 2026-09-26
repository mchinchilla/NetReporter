using System.Globalization;
using System.Text.Json;
using NetReporter.Core.DataBinding;
using NetReporter.Core.Expressions;
using NetReporter.Templates.Expressions;

namespace NetReporter.Templates.Binding;

/// <summary>
/// Crea bindings de fila a partir del texto del template. Un binding que empieza con <c>=</c> es una
/// expresión DSL evaluada contra la fila (<c>$</c> = fila, <c>$$</c> = raíz de datos); cualquier otro
/// texto es un JSON Path clásico. Ej.: <c>binding: "= $.cantidad * $.precio"</c>.
/// </summary>
public static class BindingFactory
{
    public static IDataBinding<JsonElement, object?> Create(string spec, JsonElement root)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var trimmed = spec.TrimStart();
        return trimmed.StartsWith('=')
            ? new DslBinding(DslExpression.Parse(trimmed[1..]), root)
            : new JsonPathBinding(spec);
    }

    /// <summary>
    /// Compila una condición (<c>visible: "$.descuento > 0"</c>) a <see cref="IExpression{Boolean}"/>.
    /// Acepta la expresión desnuda o envuelta en <c>{{ }}</c>. <c>$</c> apunta a la raíz de datos.
    /// </summary>
    public static IExpression<bool> CompileCondition(string source, JsonElement root)
    {
        ArgumentNullException.ThrowIfNull(source);
        var text = source.Trim();
        if (text.StartsWith("{{", StringComparison.Ordinal) && text.EndsWith("}}", StringComparison.Ordinal))
            text = text[2..^2];
        var expr = DslExpression.Parse(text);
        return Expr.Of(ctx => expr.EvaluateBool(new DslScope(ctx, root, root)));
    }
}

/// <summary>Binding de fila respaldado por una expresión DSL precompilada.</summary>
public sealed class DslBinding : IDataBinding<JsonElement, object?>
{
    private readonly DslExpression _expr;
    private readonly JsonElement _root;

    public DslBinding(DslExpression expr, JsonElement root)
    {
        _expr = expr;
        _root = root;
    }

    public object? Evaluate(JsonElement row)
    {
        var value = _expr.Evaluate(new DslScope(RowContext.Instance, row, _root));
        // Las listas no tienen formato de celda: se aplanan a texto.
        return value is IReadOnlyList<object?> list ? DslValues.ToText(list, CultureInfo.InvariantCulture) : value;
    }

    private sealed class RowContext : IEvaluationContext
    {
        public static readonly RowContext Instance = new();
        public int PageNumber => 0;
        public int TotalPages => 0;
        public int RowIndex => 0;
        public object? CurrentRow => null;
        public object? GetParameter(string name) => null;
        public object? GetAggregate(string name) => null;
    }
}
