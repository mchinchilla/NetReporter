using System.Text.Json;
using NetReporter.Core.Expressions;

namespace NetReporter.Templates.Expressions;

/// <summary>
/// Ámbito de evaluación de una expresión: el contexto de layout (página, grupo, cultura), el
/// elemento JSON "actual" al que apunta <c>$</c> (la fila en una tabla, o la raíz fuera de ella) y la
/// raíz del documento de datos, a la que apunta <c>$$</c>.
/// </summary>
public readonly record struct DslScope(IEvaluationContext Context, JsonElement Current, JsonElement Root);

/// <summary>
/// Expresión DSL compilada. Se parsea UNA vez (texto → árbol → delegates encadenados) y luego
/// se evalúa cuantas veces haga falta sin volver a tocar el texto ni usar reflection — la evaluación
/// por fila es solo una cadena de llamadas a delegates.
///
/// <para>Sintaxis:</para>
/// <list type="bullet">
///   <item>Literales: <c>12</c>, <c>3.5</c>, <c>'texto'</c> o <c>"texto"</c>, <c>true</c>, <c>false</c>, <c>null</c></item>
///   <item>Paths: <c>$.cliente.nombre</c>, <c>$.lineas[0].total</c>, <c>$.lineas[*].total</c> (lista),
///         <c>$$.empresa</c> (siempre la raíz, útil dentro de tablas)</item>
///   <item>Contexto: <c>pageNumber</c>, <c>totalPages</c>, <c>rowIndex</c>, <c>#group</c>, <c>#count</c></item>
///   <item>Operadores: <c>+ - * / %</c>, <c>== != &lt; &lt;= &gt; &gt;=</c>, <c>&amp;&amp; || !</c>
///         (o <c>and or not</c>), <c>??</c>, <c>cond ? a : b</c></item>
///   <item>Funciones: ver <see cref="DslFunctions"/> (<c>if</c>, <c>upper</c>, <c>round</c>, <c>sum</c>, <c>format</c>, …)</item>
/// </list>
/// </summary>
public sealed class DslExpression
{
    private readonly Func<DslScope, object?> _eval;

    public string Source { get; }

    private DslExpression(string source, Func<DslScope, object?> eval)
    {
        Source = source;
        _eval = eval;
    }

    /// <summary>Parsea y compila. Lanza <see cref="DslSyntaxException"/> con la posición del error.</summary>
    public static DslExpression Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source))
            throw new DslSyntaxException("Expresión vacía", source, 0);
        var parser = new DslParser(source, DslLexer.Tokenize(source));
        return new DslExpression(source, parser.ParseAll());
    }

    public object? Evaluate(in DslScope scope) => _eval(scope);

    public object? Evaluate(IEvaluationContext context, JsonElement current, JsonElement root) =>
        _eval(new DslScope(context, current, root));

    public bool EvaluateBool(in DslScope scope) => DslValues.IsTruthy(_eval(scope));

    public string EvaluateText(in DslScope scope, string? format = null) =>
        DslValues.ToText(_eval(scope), scope.Context.Culture, format);

    /// <summary>
    /// Separa el sufijo de formato de un placeholder: en <c>$.total * 1.15 : N2</c> devuelve
    /// (<c>$.total * 1.15</c>, <c>N2</c>). El <c>:</c> de un ternario (<c>a ? b : c</c>) no cuenta, ni los
    /// que estén dentro de strings o paréntesis. El formato se toma crudo (puede contener <c>#,##0.00</c>).
    /// </summary>
    public static (string Expression, string? Format) SplitFormat(string text)
    {
        var depth = 0;
        var pendingTernaries = 0;
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is not null)
            {
                if (c == '\\') { i++; continue; }
                if (c == quote) quote = null;
                continue;
            }
            switch (c)
            {
                case '\'' or '"': quote = c; break;
                case '(': depth++; break;
                case ')': depth--; break;
                case '[': depth++; break;
                case ']': depth--; break;
                case '?':
                    if (i + 1 < text.Length && text[i + 1] == '?') { i++; break; }
                    if (depth == 0) pendingTernaries++;
                    break;
                case ':' when depth == 0:
                    if (pendingTernaries > 0) { pendingTernaries--; break; }
                    var fmt = text[(i + 1)..].Trim();
                    return (text[..i].Trim(), fmt.Length == 0 ? null : fmt);
            }
        }
        return (text.Trim(), null);
    }
}

/// <summary>Parser de descenso recursivo que produce directamente los delegates de evaluación.</summary>
internal sealed class DslParser
{
    private readonly string _source;
    private readonly List<Token> _tokens;
    private int _pos;

    public DslParser(string source, List<Token> tokens)
    {
        _source = source;
        _tokens = tokens;
    }

    private Token Current => _tokens[_pos];

    private DslSyntaxException Error(string message, Token? at = null) =>
        new(message, _source, (at ?? Current).Position);

    public Func<DslScope, object?> ParseAll()
    {
        var expr = ParseTernary();
        if (Current.Kind != TokenKind.End)
            throw Error($"Token inesperado '{Current.Text}'");
        return expr;
    }

    private bool IsOperator(string op) => Current.Kind == TokenKind.Operator && Current.Text == op;

    private bool IsWord(string word) =>
        Current.Kind == TokenKind.Identifier && string.Equals(Current.Text, word, StringComparison.OrdinalIgnoreCase);

    private Func<DslScope, object?> ParseTernary()
    {
        var cond = ParseCoalesce();
        if (!IsOperator("?")) return cond;
        _pos++;
        var whenTrue = ParseTernary();
        if (!IsOperator(":")) throw Error("Se esperaba ':' en el operador ternario");
        _pos++;
        var whenFalse = ParseTernary();
        return s => DslValues.IsTruthy(cond(s)) ? whenTrue(s) : whenFalse(s);
    }

    private Func<DslScope, object?> ParseCoalesce()
    {
        var left = ParseOr();
        while (IsOperator("??"))
        {
            _pos++;
            var l = left;
            var right = ParseOr();
            left = s => l(s) ?? right(s);
        }
        return left;
    }

    private Func<DslScope, object?> ParseOr()
    {
        var left = ParseAnd();
        while (IsOperator("||") || IsWord("or"))
        {
            _pos++;
            var l = left;
            var right = ParseAnd();
            left = s => DslValues.IsTruthy(l(s)) || DslValues.IsTruthy(right(s));
        }
        return left;
    }

    private Func<DslScope, object?> ParseAnd()
    {
        var left = ParseEquality();
        while (IsOperator("&&") || IsWord("and"))
        {
            _pos++;
            var l = left;
            var right = ParseEquality();
            left = s => DslValues.IsTruthy(l(s)) && DslValues.IsTruthy(right(s));
        }
        return left;
    }

    private Func<DslScope, object?> ParseEquality()
    {
        var left = ParseComparison();
        while (IsOperator("==") || IsOperator("!="))
        {
            var negate = Current.Text == "!=";
            _pos++;
            var l = left;
            var right = ParseComparison();
            left = negate
                ? s => !DslValues.AreEqual(l(s), right(s))
                : s => DslValues.AreEqual(l(s), right(s));
        }
        return left;
    }

    private Func<DslScope, object?> ParseComparison()
    {
        var left = ParseAdditive();
        while (IsOperator("<") || IsOperator("<=") || IsOperator(">") || IsOperator(">="))
        {
            var op = Current.Text;
            _pos++;
            var l = left;
            var right = ParseAdditive();
            left = op switch
            {
                "<"  => s => Compare(l(s), right(s), c => c < 0),
                "<=" => s => Compare(l(s), right(s), c => c <= 0),
                ">"  => s => Compare(l(s), right(s), c => c > 0),
                _    => s => Compare(l(s), right(s), c => c >= 0)
            };
        }
        return left;
    }

    // Comparar contra null es siempre falso (como SQL): "$.saldo > 0" no explota si falta el campo.
    private static object Compare(object? a, object? b, Func<int, bool> test) =>
        a is not null && b is not null && test(DslValues.Compare(a, b));

    private Func<DslScope, object?> ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (IsOperator("+") || IsOperator("-"))
        {
            var op = Current.Text;
            _pos++;
            var l = left;
            var right = ParseMultiplicative();
            left = op == "+"
                ? s => Add(l(s), right(s), s.Context)
                : s => Arithmetic(l(s), right(s), (x, y) => x - y);
        }
        return left;
    }

    private static object? Add(object? a, object? b, IEvaluationContext ctx)
    {
        // "+" suma números; si alguno es texto no numérico, concatena.
        if (a is string || b is string)
        {
            if (DslValues.ToNumber(a) is { } na && DslValues.ToNumber(b) is { } nb && a is not string && b is not string)
                return na + nb;
            return DslValues.ToText(a, ctx.Culture) + DslValues.ToText(b, ctx.Culture);
        }
        if (a is DateTime da && DslValues.ToNumber(b) is { } days) return da.AddDays((double)days);
        return Arithmetic(a, b, (x, y) => x + y);
    }

    private static object? Arithmetic(object? a, object? b, Func<decimal, decimal, decimal> op)
    {
        var na = DslValues.ToNumber(a);
        var nb = DslValues.ToNumber(b);
        if (na is null || nb is null) return null;
        try { return op(na.Value, nb.Value); }
        catch (DivideByZeroException) { return null; }
        catch (OverflowException) { return null; }
    }

    private Func<DslScope, object?> ParseMultiplicative()
    {
        var left = ParseUnary();
        while (IsOperator("*") || IsOperator("/") || IsOperator("%"))
        {
            var op = Current.Text;
            _pos++;
            var l = left;
            var right = ParseUnary();
            left = op switch
            {
                "*" => s => Arithmetic(l(s), right(s), (x, y) => x * y),
                "/" => s => Arithmetic(l(s), right(s), (x, y) => x / y),
                _   => s => Arithmetic(l(s), right(s), (x, y) => x % y)
            };
        }
        return left;
    }

    private Func<DslScope, object?> ParseUnary()
    {
        if (IsOperator("!") || IsWord("not"))
        {
            _pos++;
            var operand = ParseUnary();
            return s => !DslValues.IsTruthy(operand(s));
        }
        if (IsOperator("-"))
        {
            _pos++;
            var operand = ParseUnary();
            return s => DslValues.ToNumber(operand(s)) is { } n ? -n : null;
        }
        if (IsOperator("+"))
        {
            _pos++;
            return ParseUnary();
        }
        return ParsePrimary();
    }

    private Func<DslScope, object?> ParsePrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.Number:
            {
                _pos++;
                object value = token.Number;
                return _ => value;
            }
            case TokenKind.String:
            {
                _pos++;
                var value = token.Text;
                return _ => value;
            }
            case TokenKind.Path:
                _pos++;
                return DslPath.Compile(token.Text, _source, token.Position);
            case TokenKind.Hash:
                _pos++;
                return token.Text switch
                {
                    "#group" => s => DslValues.Normalize(s.Context.GroupKey),
                    "#count" => s => (decimal)s.Context.GroupRowCount,
                    "#level" => s => (decimal)Math.Max(0, s.Context.GroupKeys.Count - 1),
                    _ => throw Error($"Variable desconocida '{token.Text}'. Usa #group, #count o #level", token)
                };
            case TokenKind.LParen:
            {
                _pos++;
                var inner = ParseTernary();
                if (Current.Kind != TokenKind.RParen) throw Error("Se esperaba ')'");
                _pos++;
                return inner;
            }
            case TokenKind.Identifier:
                return ParseIdentifier();
            case TokenKind.End:
                throw Error("Expresión incompleta");
            default:
                throw Error($"Token inesperado '{token.Text}'");
        }
    }

    private Func<DslScope, object?> ParseIdentifier()
    {
        var token = Current;
        _pos++;
        var name = token.Text;

        if (Current.Kind == TokenKind.LParen)
        {
            _pos++;
            var args = new List<Func<DslScope, object?>>();
            if (Current.Kind != TokenKind.RParen)
            {
                while (true)
                {
                    args.Add(ParseTernary());
                    if (Current.Kind == TokenKind.Comma) { _pos++; continue; }
                    break;
                }
            }
            if (Current.Kind != TokenKind.RParen) throw Error($"Se esperaba ')' al cerrar {name}(...)");
            _pos++;
            return DslFunctions.Bind(name, args, msg => Error(msg, token));
        }

        return name switch
        {
            "true"  or "True"  => _ => true,
            "false" or "False" => _ => false,
            "null"  or "Null"  => _ => null,
            "pageNumber" => s => (decimal)s.Context.PageNumber,
            "totalPages" => s => (decimal)s.Context.TotalPages,
            "rowIndex"   => s => (decimal)s.Context.RowIndex,
            "rowNumber"  => s => (decimal)s.Context.RowIndex + 1,
            _ => throw Error(
                $"Identificador desconocido '{name}'. Usa pageNumber, totalPages, rowIndex, rowNumber, " +
                "#group, #count, un path ($.campo) o una función (if, upper, round, sum, format, …)", token)
        };
    }
}

/// <summary>Path JSON precompilado: los segmentos se tokenizan una vez, no en cada evaluación.</summary>
internal static class DslPath
{
    private abstract record Segment;
    private sealed record Property(string Name) : Segment;
    private sealed record Index(int Value) : Segment;
    private sealed record Wildcard : Segment;

    public static Func<DslScope, object?> Compile(string path, string source, int position)
    {
        var fromRoot = path.StartsWith("$$", StringComparison.Ordinal);
        var segments = Parse(path[(fromRoot ? 2 : 1)..], source, position);

        return s =>
        {
            var start = fromRoot ? s.Root : s.Current;
            if (start.ValueKind == JsonValueKind.Undefined) return null;
            return Walk(start, segments, 0);
        };
    }

    private static object? Walk(JsonElement current, List<Segment> segments, int index)
    {
        for (var i = index; i < segments.Count; i++)
        {
            switch (segments[i])
            {
                case Property p:
                    if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(p.Name, out var next))
                        return null;
                    current = next;
                    break;
                case Index ix:
                    if (current.ValueKind != JsonValueKind.Array) return null;
                    var len = current.GetArrayLength();
                    var at = ix.Value < 0 ? len + ix.Value : ix.Value;
                    if (at < 0 || at >= len) return null;
                    current = current[at];
                    break;
                case Wildcard:
                    if (current.ValueKind != JsonValueKind.Array) return null;
                    var results = new List<object?>();
                    foreach (var item in current.EnumerateArray())
                    {
                        var v = Walk(item, segments, i + 1);
                        if (v is IReadOnlyList<object?> nested && i + 1 < segments.Count) results.AddRange(nested);
                        else results.Add(v);
                    }
                    return results;
            }
        }
        return DslValues.FromJson(current);
    }

    private static List<Segment> Parse(string rest, string source, int position)
    {
        var result = new List<Segment>();
        var i = 0;
        while (i < rest.Length)
        {
            if (rest[i] == '.')
            {
                i++;
                var start = i;
                while (i < rest.Length && rest[i] != '.' && rest[i] != '[') i++;
                if (i == start) throw new DslSyntaxException("Nombre de propiedad vacío en el path", source, position);
                result.Add(new Property(rest[start..i]));
            }
            else if (rest[i] == '[')
            {
                var close = rest.IndexOf(']', i);
                var inner = rest[(i + 1)..close].Trim();
                i = close + 1;
                if (inner == "*") result.Add(new Wildcard());
                else if (int.TryParse(inner, out var n)) result.Add(new Index(n));
                else if (inner.Length >= 2 && (inner[0] == '\'' || inner[0] == '"')) result.Add(new Property(inner[1..^1]));
                else throw new DslSyntaxException($"Índice inválido '[{inner}]' en el path", source, position);
            }
            else
            {
                throw new DslSyntaxException($"Path inválido cerca de '{rest[i..]}'", source, position);
            }
        }
        return result;
    }
}
