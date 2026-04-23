using System.Text.Json;
using NetReporter.Core.DataBinding;

namespace NetReporter.Templates.Binding;

/// <summary>
/// Binding que evalúa un JSON Path sobre un <see cref="JsonElement"/> (una fila de tabla).
/// Devuelve el valor boxed para que el formateador de tabla pueda aplicar formatos numéricos/fecha.
/// </summary>
public sealed class JsonPathBinding : IDataBinding<JsonElement, object?>
{
    private readonly string _path;

    public JsonPathBinding(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _path = path;
    }

    public object? Evaluate(JsonElement row) =>
        JsonPath.ToBoxedValue(JsonPath.Select(row, _path));
}
