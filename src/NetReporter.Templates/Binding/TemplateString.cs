using System.Text;
using System.Text.Json;
using NetReporter.Core.Expressions;
using NetReporter.Templates.Expressions;

namespace NetReporter.Templates.Binding;

/// <summary>
/// Parsea y compila plantillas tipo "Hola {{ $.nombre }}, página {{ pageNumber }}".
/// Soporta:
///   {{ pageNumber }}   — número de página actual
///   {{ totalPages }}   — total de páginas
///   {{ rowIndex }}     — índice de fila (en contexto de tabla)
///   {{ $.path }}       — JSON Path sobre el data root capturado
///   {{ $.path:N2 }}    — con format string .NET (cultura del reporte)
///   {{ expr }}         — cualquier expresión DSL (ver <see cref="DslExpression"/>), p. ej.
///                        {{ $.cantidad * $.precio : N2 }} o {{ $.saldo == 0 ? 'PAGADO' : 'PENDIENTE' }}
/// Texto fuera de {{...}} se mantiene literal.
/// </summary>
public static class TemplateString
{
    /// <summary>
    /// Resuelve un template string inmediatamente contra <paramref name="dataRoot"/>,
    /// sin depender de contexto de layout. Placeholders como <c>{{ pageNumber }}</c> o
    /// <c>{{ totalPages }}</c> emiten cadena vacía — útil para campos fuera de la fase
    /// de render (filename, title dinámico, etc.).
    /// </summary>
    public static string Resolve(string template, JsonElement dataRoot)
    {
        var expr = Compile(template, dataRoot);
        return expr.Evaluate(StaticContext.Instance);
    }

    private sealed class StaticContext : IEvaluationContext
    {
        public static readonly StaticContext Instance = new();
        public int PageNumber => 0;
        public int TotalPages => 0;
        public int RowIndex => 0;
        public object? CurrentRow => null;
        public object? GetParameter(string name) => null;
        public object? GetAggregate(string name) => null;
    }

    /// <summary>
    /// Compila un template string a <see cref="IExpression{String}"/> resolviendo
    /// los paths JSON contra <paramref name="dataRoot"/> (captured por closure).
    /// </summary>
    public static IExpression<string> Compile(string template, JsonElement dataRoot)
    {
        var parts = Parse(template);
        return Expr.Of(ctx =>
        {
            var sb = new StringBuilder(template.Length);
            foreach (var part in parts)
                sb.Append(part.Evaluate(ctx, dataRoot));
            return sb.ToString();
        });
    }

    /// <summary>
    /// Compila un template string donde los paths JSON se resuelven contra la fila actual
    /// (obtenida de <see cref="IEvaluationContext.CurrentRow"/> casteado a <see cref="JsonElement"/>).
    /// </summary>
    public static IExpression<string> CompileForRow(string template)
    {
        var parts = Parse(template);
        return Expr.Of(ctx =>
        {
            var row = ctx.CurrentRow is JsonElement r ? r : default;
            var sb = new StringBuilder(template.Length);
            foreach (var part in parts)
                sb.Append(part.Evaluate(ctx, row));
            return sb.ToString();
        });
    }

    // === Parser ===

    private static List<TemplatePart> Parse(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var parts = new List<TemplatePart>();
        var i = 0;

        while (i < template.Length)
        {
            var open = template.IndexOf("{{", i, StringComparison.Ordinal);
            if (open < 0)
            {
                parts.Add(new LiteralPart(template[i..]));
                break;
            }

            if (open > i)
                parts.Add(new LiteralPart(template[i..open]));

            var close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0)
                throw new FormatException($"Template sin cerrar '{{{{...}}}}' en: {template}");

            var expr = template[(open + 2)..close].Trim();
            parts.Add(CreatePart(expr));
            i = close + 2;
        }

        return parts;
    }

    private static TemplatePart CreatePart(string expr)
    {
        if (expr.Length == 0)
            return new LiteralPart(string.Empty);

        // Los placeholders clásicos mantienen su ruta rápida y su salida exacta (p. ej. un número JSON
        // se imprime tal cual viene en el JSON). Todo lo demás se compila como expresión DSL.
        return expr switch
        {
            "pageNumber" => PageNumberPart.Instance,
            "totalPages" => TotalPagesPart.Instance,
            "rowIndex"   => RowIndexPart.Instance,
            "#group"     => GroupKeyPart.Instance,
            "#count"     => GroupCountPart.Instance,
            _ when IsSimplePath(expr) => JsonPathPart.Create(expr),
            _ => DslPart.Create(expr)
        };
    }

    /// <summary><c>$.a.b[0]</c> con sufijo <c>:formato</c> opcional — sin operadores, <c>$$</c> ni <c>[*]</c>.</summary>
    private static bool IsSimplePath(string expr)
    {
        var (path, _) = DslExpression.SplitFormat(expr);
        if (path.Length == 0 || path[0] != '$' || path.StartsWith("$$", StringComparison.Ordinal)) return false;
        if (path.Contains("[*]", StringComparison.Ordinal)) return false;
        foreach (var c in path)
            if (!(char.IsLetterOrDigit(c) || c is '$' or '.' or '_' or '-' or '[' or ']'))
                return false;
        return true;
    }

    // === Parts ===

    private abstract class TemplatePart
    {
        public abstract string Evaluate(IEvaluationContext ctx, JsonElement contextRoot);
    }

    private sealed class LiteralPart : TemplatePart
    {
        private readonly string _text;
        public LiteralPart(string text) => _text = text;
        public override string Evaluate(IEvaluationContext ctx, JsonElement contextRoot) => _text;
    }

    private sealed class PageNumberPart : TemplatePart
    {
        public static readonly PageNumberPart Instance = new();
        public override string Evaluate(IEvaluationContext ctx, JsonElement _) =>
            ctx.PageNumber.ToString();
    }

    private sealed class TotalPagesPart : TemplatePart
    {
        public static readonly TotalPagesPart Instance = new();
        public override string Evaluate(IEvaluationContext ctx, JsonElement _) =>
            ctx.TotalPages.ToString();
    }

    private sealed class RowIndexPart : TemplatePart
    {
        public static readonly RowIndexPart Instance = new();
        public override string Evaluate(IEvaluationContext ctx, JsonElement _) =>
            ctx.RowIndex.ToString();
    }

    private sealed class GroupKeyPart : TemplatePart
    {
        public static readonly GroupKeyPart Instance = new();
        public override string Evaluate(IEvaluationContext ctx, JsonElement _)
        {
            var key = ctx.GroupKey;
            if (key is null) return string.Empty;
            if (key is JsonElement je) return JsonPath.ToDisplayString(je);
            return key.ToString() ?? string.Empty;
        }
    }

    private sealed class GroupCountPart : TemplatePart
    {
        public static readonly GroupCountPart Instance = new();
        public override string Evaluate(IEvaluationContext ctx, JsonElement _) =>
            ctx.GroupRowCount.ToString();
    }

    private sealed class JsonPathPart : TemplatePart
    {
        private readonly string _path;
        private readonly string? _format;

        private JsonPathPart(string path, string? format) { _path = path; _format = format; }

        /// <summary>
        /// Parses a path token with an optional <c>:format</c> suffix (e.g. <c>$.totals.debit:N2</c>).
        /// A colon never occurs inside a JSON path, so the split is unambiguous.
        /// </summary>
        public static JsonPathPart Create(string expr)
        {
            var (path, format) = DslExpression.SplitFormat(expr);
            return new JsonPathPart(path, format);
        }

        public override string Evaluate(IEvaluationContext ctx, JsonElement contextRoot)
        {
            if (contextRoot.ValueKind == JsonValueKind.Undefined) return string.Empty;
            var el = JsonPath.Select(contextRoot, _path);
            if (_format is not null)
                return el is { } value ? DslValues.ToText(DslValues.FromJson(value), ctx.Culture, _format) : string.Empty;
            return JsonPath.ToDisplayString(el);
        }
    }

    /// <summary>Placeholder con una expresión DSL arbitraria y formato opcional.</summary>
    private sealed class DslPart : TemplatePart
    {
        private readonly DslExpression _expr;
        private readonly string? _format;

        private DslPart(DslExpression expr, string? format) { _expr = expr; _format = format; }

        public static DslPart Create(string text)
        {
            var (expression, format) = DslExpression.SplitFormat(text);
            return new DslPart(DslExpression.Parse(expression), format);
        }

        public override string Evaluate(IEvaluationContext ctx, JsonElement contextRoot) =>
            _expr.EvaluateText(new DslScope(ctx, contextRoot, contextRoot), _format);
    }
}
