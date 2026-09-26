using System.Globalization;

namespace NetReporter.Templates.Expressions;

/// <summary>
/// Funciones built-in del DSL (nombres sin distinguir mayúsculas). La aridad se valida al compilar,
/// así un template con <c>round()</c> falla al cargar y no a mitad del render.
///
/// <list type="table">
///   <item><term>Lógica</term><description>if(c, a, b) · iif · coalesce(a, b, …) · isNull(x) · isEmpty(x)</description></item>
///   <item><term>Texto</term><description>upper · lower · trim · len · substr(s, i[, n]) · left(s, n) · right(s, n) ·
///     replace(s, a, b) · contains(s, x) · startsWith · endsWith · concat(…) · padLeft(s, n[, c]) · padRight · join(lista[, sep])</description></item>
///   <item><term>Números</term><description>round(x[, d]) · floor · ceil · abs · min(…) · max(…) · number(x)</description></item>
///   <item><term>Agregados</term><description>sum(lista) · avg(lista) · count(lista) · min(lista) · max(lista)</description></item>
///   <item><term>Fechas</term><description>date(x) · now() · today() · year(d) · month(d) · day(d) · addDays(d, n) · addMonths(d, n)</description></item>
///   <item><term>Formato</term><description>format(x, fmt) · text(x)</description></item>
///   <item><term>Grupos</term><description>group(n) — clave del nivel n (0 = externo) en tablas con grupos anidados</description></item>
/// </list>
/// </summary>
public static class DslFunctions
{
    private delegate object? Impl(object?[] args, DslScope scope);

    private sealed record Definition(int MinArgs, int MaxArgs, Impl Body);

    private const int Many = int.MaxValue;

    private static readonly Dictionary<string, Definition> s_functions = new(StringComparer.OrdinalIgnoreCase)
    {
        // === Lógica ===
        ["coalesce"] = new(1, Many, (a, _) => a.FirstOrDefault(x => x is not null && !(x is string s && s.Length == 0))),
        ["isNull"]   = new(1, 1, (a, _) => a[0] is null),
        ["isEmpty"]  = new(1, 1, (a, _) => !DslValues.IsTruthy(a[0]) && a[0] is not false && !(a[0] is decimal d && d == 0)),

        // === Texto ===
        ["upper"]      = new(1, 1, (a, s) => Text(a[0], s).ToUpper(s.Context.Culture)),
        ["lower"]      = new(1, 1, (a, s) => Text(a[0], s).ToLower(s.Context.Culture)),
        ["trim"]       = new(1, 1, (a, s) => Text(a[0], s).Trim()),
        ["len"]        = new(1, 1, (a, s) => (decimal)Length(a[0], s)),
        ["length"]     = new(1, 1, (a, s) => (decimal)Length(a[0], s)),
        ["substr"]     = new(2, 3, (a, s) => Substring(Text(a[0], s), Int(a[1]), a.Length > 2 ? Int(a[2]) : null)),
        ["left"]       = new(2, 2, (a, s) => Substring(Text(a[0], s), 0, Int(a[1]))),
        ["right"]      = new(2, 2, (a, s) => { var t = Text(a[0], s); var n = Math.Clamp(Int(a[1]), 0, t.Length); return t[^n..]; }),
        ["replace"]    = new(3, 3, (a, s) => { var find = Text(a[1], s); return find.Length == 0 ? Text(a[0], s) : Text(a[0], s).Replace(find, Text(a[2], s), StringComparison.Ordinal); }),
        ["contains"]   = new(2, 2, (a, s) => a[0] is IReadOnlyList<object?> list
                                        ? list.Any(x => DslValues.AreEqual(x, a[1]))
                                        : Text(a[0], s).Contains(Text(a[1], s), StringComparison.OrdinalIgnoreCase)),
        ["startsWith"] = new(2, 2, (a, s) => Text(a[0], s).StartsWith(Text(a[1], s), StringComparison.OrdinalIgnoreCase)),
        ["endsWith"]   = new(2, 2, (a, s) => Text(a[0], s).EndsWith(Text(a[1], s), StringComparison.OrdinalIgnoreCase)),
        ["concat"]     = new(1, Many, (a, s) => string.Concat(DslValues.Flatten(a).Select(x => Text(x, s)))),
        ["padLeft"]    = new(2, 3, (a, s) => Text(a[0], s).PadLeft(Int(a[1]), a.Length > 2 ? PadChar(a[2], s) : ' ')),
        ["padRight"]   = new(2, 3, (a, s) => Text(a[0], s).PadRight(Int(a[1]), a.Length > 2 ? PadChar(a[2], s) : ' ')),
        ["join"]       = new(1, 2, (a, s) => string.Join(a.Length > 1 ? Text(a[1], s) : ", ",
                                        DslValues.Flatten(new[] { a[0] }).Select(x => Text(x, s)))),

        // === Números ===
        ["round"]  = new(1, 2, (a, _) => DslValues.ToNumber(a[0]) is { } n
                                    ? Math.Round(n, a.Length > 1 ? Math.Clamp(Int(a[1]), 0, 28) : 0, MidpointRounding.AwayFromZero)
                                    : null),
        ["floor"]  = new(1, 1, (a, _) => DslValues.ToNumber(a[0]) is { } n ? Math.Floor(n) : null),
        ["ceil"]   = new(1, 1, (a, _) => DslValues.ToNumber(a[0]) is { } n ? Math.Ceiling(n) : null),
        ["abs"]    = new(1, 1, (a, _) => DslValues.ToNumber(a[0]) is { } n ? Math.Abs(n) : null),
        ["number"] = new(1, 1, (a, _) => DslValues.ToNumber(a[0])),
        ["min"]    = new(1, Many, (a, _) => Extreme(a, pickMax: false)),
        ["max"]    = new(1, Many, (a, _) => Extreme(a, pickMax: true)),

        // === Agregados ===
        ["sum"]   = new(1, Many, (a, _) => Numbers(a).Aggregate(0m, (acc, x) => acc + x)),
        ["avg"]   = new(1, Many, (a, _) => Numbers(a) is { Count: > 0 } nums ? nums.Sum() / nums.Count : null),
        ["count"] = new(1, Many, (a, _) => (decimal)DslValues.Flatten(a).Count(x => x is not null)),

        // === Fechas ===
        ["date"]      = new(1, 2, (a, s) => a.Length > 1 && a[0] is string str
                                        && DateTime.TryParseExact(str, Text(a[1], s), s.Context.Culture, DateTimeStyles.None, out var exact)
                                            ? exact
                                            : DslValues.ToDate(a[0])),
        ["now"]       = new(0, 0, (_, _) => DateTime.Now),
        ["today"]     = new(0, 0, (_, _) => DateTime.Today),
        ["year"]      = new(1, 1, (a, _) => DslValues.ToDate(a[0]) is { } d ? (decimal)d.Year : null),
        ["month"]     = new(1, 1, (a, _) => DslValues.ToDate(a[0]) is { } d ? (decimal)d.Month : null),
        ["day"]       = new(1, 1, (a, _) => DslValues.ToDate(a[0]) is { } d ? (decimal)d.Day : null),
        ["addDays"]   = new(2, 2, (a, _) => DslValues.ToDate(a[0]) is { } d ? d.AddDays(Int(a[1])) : null),
        ["addMonths"] = new(2, 2, (a, _) => DslValues.ToDate(a[0]) is { } d ? d.AddMonths(Int(a[1])) : null),

        // === Formato ===
        ["format"] = new(2, 2, (a, s) => DslValues.ToText(a[0], s.Context.Culture, Text(a[1], s))),
        ["text"]   = new(1, 1, (a, s) => Text(a[0], s)),

        // === Grupos ===
        ["group"] = new(0, 1, (a, s) =>
        {
            var keys = s.Context.GroupKeys;
            if (a.Length == 0) return DslValues.Normalize(s.Context.GroupKey);
            var level = Int(a[0]);
            return level >= 0 && level < keys.Count ? DslValues.Normalize(keys[level]) : null;
        }),
    };

    /// <summary>Nombres de todas las funciones disponibles (para mensajes de error y documentación).</summary>
    public static IReadOnlyCollection<string> Names => s_functions.Keys;

    internal static Func<DslScope, object?> Bind(
        string name, List<Func<DslScope, object?>> args, Func<string, DslSyntaxException> error)
    {
        // if/iif son perezosas: solo evalúan la rama elegida (evita divisiones por cero en la otra rama).
        if (name.Equals("if", StringComparison.OrdinalIgnoreCase) || name.Equals("iif", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count is < 2 or > 3) throw error($"{name}() espera 2 o 3 argumentos: {name}(condición, siNo[, siNo])");
            var cond = args[0];
            var whenTrue = args[1];
            var whenFalse = args.Count > 2 ? args[2] : (_ => null);
            return s => DslValues.IsTruthy(cond(s)) ? whenTrue(s) : whenFalse(s);
        }

        if (!s_functions.TryGetValue(name, out var def))
            throw error($"Función desconocida '{name}()'. Disponibles: if, {string.Join(", ", s_functions.Keys)}");

        if (args.Count < def.MinArgs || args.Count > def.MaxArgs)
        {
            var expected = def.MinArgs == def.MaxArgs ? $"{def.MinArgs}"
                : def.MaxArgs == Many ? $"al menos {def.MinArgs}"
                : $"entre {def.MinArgs} y {def.MaxArgs}";
            throw error($"{name}() espera {expected} argumento(s), recibió {args.Count}");
        }

        var compiled = args.ToArray();
        var body = def.Body;
        return s =>
        {
            var values = new object?[compiled.Length];
            for (var i = 0; i < compiled.Length; i++) values[i] = compiled[i](s);
            return body(values, s);
        };
    }

    private static string Text(object? v, DslScope s) => DslValues.ToText(v, s.Context.Culture);

    private static int Int(object? v) =>
        DslValues.ToNumber(v) is { } n ? (int)Math.Clamp(Math.Truncate(n), int.MinValue, int.MaxValue) : 0;

    private static char PadChar(object? v, DslScope s) => Text(v, s) is { Length: > 0 } t ? t[0] : ' ';

    private static int Length(object? v, DslScope s) => v switch
    {
        null => 0,
        IReadOnlyList<object?> list => list.Count,
        _ => Text(v, s).Length
    };

    private static string Substring(string text, int start, int? length)
    {
        start = Math.Clamp(start, 0, text.Length);
        var max = text.Length - start;
        var len = length is { } l ? Math.Clamp(l, 0, max) : max;
        return text.Substring(start, len);
    }

    private static List<decimal> Numbers(object?[] args) =>
        DslValues.Flatten(args).Select(DslValues.ToNumber).Where(n => n is not null).Select(n => n!.Value).ToList();

    private static object? Extreme(object?[] args, bool pickMax)
    {
        object? best = null;
        foreach (var v in DslValues.Flatten(args))
        {
            if (v is null) continue;
            if (best is null) { best = v; continue; }
            var cmp = DslValues.Compare(v, best);
            if (pickMax ? cmp > 0 : cmp < 0) best = v;
        }
        return best;
    }
}
