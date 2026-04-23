using System.Text.Json;

namespace NetReporter.Templates.Binding;

/// <summary>
/// Resolver mínimo de JSON Path. Soporta:
///   $                     → root
///   $.prop                → acceso a propiedad
///   $.prop.nested         → anidado
///   $.array[0]            → índice
///   $.array[*]            → enumerar (solo via <see cref="SelectMany"/>)
/// Sin wildcards recursivos, sin filtros, sin expresiones.
/// </summary>
public static class JsonPath
{
    /// <summary>Resuelve un path a un solo JsonElement. Devuelve null si no existe.</summary>
    public static JsonElement? Select(JsonElement root, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path == "$" || string.IsNullOrEmpty(path)) return root;

        var segments = Tokenize(path);
        var current = (JsonElement?)root;

        foreach (var seg in segments)
        {
            if (current is null) return null;
            current = Step(current.Value, seg);
        }

        return current;
    }

    /// <summary>Enumera elementos cuando el path termina en [*]. Si no, wrap del elemento único.</summary>
    public static IReadOnlyList<JsonElement> SelectMany(JsonElement root, string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // Si el path termina en [*], quita ese segmento y enumera el resultado.
        if (path.EndsWith("[*]", StringComparison.Ordinal))
        {
            var basePath = path[..^3];
            var arr = Select(root, basePath);
            if (arr is null || arr.Value.ValueKind != JsonValueKind.Array)
                return Array.Empty<JsonElement>();

            var list = new List<JsonElement>();
            foreach (var item in arr.Value.EnumerateArray())
                list.Add(item);
            return list;
        }

        // Paths sin [*] que apuntan directamente a un array.
        var direct = Select(root, path);
        if (direct is null) return Array.Empty<JsonElement>();
        if (direct.Value.ValueKind == JsonValueKind.Array)
        {
            var list = new List<JsonElement>();
            foreach (var item in direct.Value.EnumerateArray())
                list.Add(item);
            return list;
        }
        return new[] { direct.Value };
    }

    /// <summary>Renderiza un JsonElement a string apto para mostrar.</summary>
    public static string ToDisplayString(JsonElement? value)
    {
        if (value is null) return string.Empty;
        var v = value.Value;
        return v.ValueKind switch
        {
            JsonValueKind.String    => v.GetString() ?? string.Empty,
            JsonValueKind.Number    => v.GetRawText(),
            JsonValueKind.True      => "true",
            JsonValueKind.False     => "false",
            JsonValueKind.Null      => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            _                       => v.GetRawText()
        };
    }

    /// <summary>Intenta extraer un valor .NET tipificado para pasar a IFormattable.</summary>
    public static object? ToBoxedValue(JsonElement? value)
    {
        if (value is null) return null;
        var v = value.Value;
        return v.ValueKind switch
        {
            JsonValueKind.String    => v.GetString(),
            JsonValueKind.True      => true,
            JsonValueKind.False     => false,
            JsonValueKind.Null      => null,
            JsonValueKind.Undefined => null,
            JsonValueKind.Number    => v.TryGetInt64(out var l) ? l
                                     : v.TryGetDecimal(out var d) ? d
                                     : v.GetDouble(),
            _                       => v.GetRawText()
        };
    }

    // === Parser ===

    private sealed record Segment(string? Property, int? Index);

    private static List<Segment> Tokenize(string path)
    {
        var result = new List<Segment>();
        var span = path.AsSpan();

        // '$' inicial es opcional pero debe estar si el path es absoluto.
        var i = 0;
        if (i < span.Length && span[i] == '$') i++;

        while (i < span.Length)
        {
            var c = span[i];

            if (c == '.')
            {
                i++;
                var start = i;
                while (i < span.Length && span[i] != '.' && span[i] != '[') i++;
                var prop = span[start..i].ToString();
                if (prop.Length == 0)
                    throw new ArgumentException($"JSON path inválido cerca de '{path[i..]}'.");
                result.Add(new Segment(prop, null));
            }
            else if (c == '[')
            {
                i++;
                var start = i;
                while (i < span.Length && span[i] != ']') i++;
                if (i >= span.Length)
                    throw new ArgumentException($"JSON path sin cerrar ']': '{path}'.");
                var inner = span[start..i].ToString();
                i++; // skip ']'
                if (inner == "*")
                    throw new ArgumentException(
                        "Wildcard [*] solo se soporta al final del path. Usa JsonPath.SelectMany.");
                if (!int.TryParse(inner, out var idx))
                    throw new ArgumentException($"Índice no numérico en path: '{inner}'.");
                result.Add(new Segment(null, idx));
            }
            else
            {
                throw new ArgumentException($"Carácter inesperado '{c}' en path '{path}'.");
            }
        }

        return result;
    }

    private static JsonElement? Step(JsonElement current, Segment seg)
    {
        if (seg.Property is not null)
        {
            if (current.ValueKind != JsonValueKind.Object) return null;
            return current.TryGetProperty(seg.Property, out var p) ? p : null;
        }
        if (seg.Index is int idx)
        {
            if (current.ValueKind != JsonValueKind.Array) return null;
            if (idx < 0 || idx >= current.GetArrayLength()) return null;
            return current[idx];
        }
        return null;
    }
}
