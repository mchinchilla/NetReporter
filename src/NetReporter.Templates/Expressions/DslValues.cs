using System.Globalization;
using System.Text.Json;

namespace NetReporter.Templates.Expressions;

/// <summary>
/// Modelo de valores del DSL. Los valores en tiempo de ejecución son siempre uno de:
/// <c>null</c>, <see cref="bool"/>, <see cref="decimal"/>, <see cref="string"/>, <see cref="DateTime"/>,
/// <see cref="IReadOnlyList{T}"/> de valores (resultado de un path con <c>[*]</c> o de un array JSON),
/// o <see cref="JsonElement"/> para objetos JSON. Todas las conversiones pasan por aquí.
/// </summary>
public static class DslValues
{
    /// <summary>Convierte un <see cref="JsonElement"/> al modelo de valores del DSL.</summary>
    public static object? FromJson(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String    => el.GetString(),
        JsonValueKind.Number    => el.TryGetDecimal(out var d) ? d : (decimal)el.GetDouble(),
        JsonValueKind.True      => true,
        JsonValueKind.False     => false,
        JsonValueKind.Null      => null,
        JsonValueKind.Undefined => null,
        JsonValueKind.Array     => el.EnumerateArray().Select(FromJson).ToList(),
        _                       => el
    };

    /// <summary>Normaliza un valor .NET arbitrario (p. ej. una clave de grupo) al modelo del DSL.</summary>
    public static object? Normalize(object? v) => v switch
    {
        null => null,
        JsonElement je => FromJson(je),
        decimal or string or bool or DateTime => v,
        int i => (decimal)i,
        long l => (decimal)l,
        double d => ToDecimalSafe(d),
        float f => ToDecimalSafe(f),
        short s => (decimal)s,
        byte b => (decimal)b,
        DateTimeOffset dto => dto.DateTime,
        IEnumerable<object?> seq => seq.Select(Normalize).ToList(),
        _ => v.ToString()
    };

    private static decimal ToDecimalSafe(double d) =>
        double.IsNaN(d) || double.IsInfinity(d) ? 0m
        : d > (double)decimal.MaxValue ? decimal.MaxValue
        : d < (double)decimal.MinValue ? decimal.MinValue
        : (decimal)d;

    /// <summary>Verdad "a la JavaScript": null/false/0/""/lista vacía son falsos.</summary>
    public static bool IsTruthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        decimal d => d != 0,
        string s => s.Length > 0,
        IReadOnlyList<object?> list => list.Count > 0,
        JsonElement je => je.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False),
        _ => true
    };

    /// <summary>Intenta interpretar el valor como número (strings numéricos incluidos).</summary>
    public static decimal? ToNumber(object? v) => v switch
    {
        decimal d => d,
        bool b => b ? 1 : 0,
        string s when decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowExponent,
                                       CultureInfo.InvariantCulture, out var d) => d,
        _ => null
    };

    /// <summary>Intenta interpretar el valor como fecha (ISO 8601 o formato invariante).</summary>
    public static DateTime? ToDate(object? v) => v switch
    {
        DateTime dt => dt,
        string s when DateTime.TryParse(s, CultureInfo.InvariantCulture,
                                        DateTimeStyles.RoundtripKind, out var dt) => dt,
        _ => null
    };

    /// <summary>
    /// Representación de texto de un valor. Con <paramref name="format"/> aplica un format string .NET;
    /// un string que parece número o fecha se formatea como tal (p. ej. <c>"2025-01-31"</c> con <c>dd/MM/yyyy</c>).
    /// </summary>
    public static string ToText(object? v, CultureInfo culture, string? format = null)
    {
        if (format is not null)
        {
            switch (v)
            {
                case decimal d: return d.ToString(format, culture);
                case DateTime dt: return dt.ToString(format, culture);
                case string s when ToNumber(s) is { } n: return n.ToString(format, culture);
                case string s when ToDate(s) is { } date: return date.ToString(format, culture);
            }
        }

        return v switch
        {
            null => string.Empty,
            string s => s,
            bool b => b ? "true" : "false",
            decimal d => d.ToString(culture),
            DateTime dt => dt.ToString(culture),
            IReadOnlyList<object?> list => string.Join(", ", list.Select(x => ToText(x, culture))),
            JsonElement je => je.GetRawText(),
            _ => Convert.ToString(v, culture) ?? string.Empty
        };
    }

    /// <summary>Igualdad tolerante: números con strings numéricos, fechas con strings de fecha.</summary>
    public static bool AreEqual(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is decimal || b is decimal)
        {
            var na = ToNumber(a);
            var nb = ToNumber(b);
            if (na is not null && nb is not null) return na == nb;
        }
        if (a is DateTime || b is DateTime)
        {
            var da = ToDate(a);
            var db = ToDate(b);
            if (da is not null && db is not null) return da == db;
        }
        if (a is bool ba && b is bool bb) return ba == bb;
        return string.Equals(ToText(a, CultureInfo.InvariantCulture), ToText(b, CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    /// <summary>Comparación de orden: numérica, por fecha o por texto ordinal, en ese orden de preferencia.</summary>
    public static int Compare(object? a, object? b)
    {
        if (a is null || b is null) return a is null ? (b is null ? 0 : -1) : 1;
        if (ToNumber(a) is { } na && ToNumber(b) is { } nb) return na.CompareTo(nb);
        if (ToDate(a) is { } da && ToDate(b) is { } db) return da.CompareTo(db);
        return string.CompareOrdinal(ToText(a, CultureInfo.InvariantCulture), ToText(b, CultureInfo.InvariantCulture));
    }

    /// <summary>Aplana argumentos para agregados: listas se expanden un nivel.</summary>
    internal static IEnumerable<object?> Flatten(IEnumerable<object?> args)
    {
        foreach (var a in args)
        {
            if (a is IReadOnlyList<object?> list)
                foreach (var x in list) yield return x;
            else
                yield return a;
        }
    }
}
