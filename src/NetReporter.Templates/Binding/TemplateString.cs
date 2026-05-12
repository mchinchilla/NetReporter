using System.Text;
using System.Text.Json;
using NetReporter.Core.Expressions;

namespace NetReporter.Templates.Binding;

/// <summary>
/// Parsea y compila plantillas tipo "Hola {{ $.nombre }}, página {{ pageNumber }}".
/// Soporta:
///   {{ pageNumber }}   — número de página actual
///   {{ totalPages }}   — total de páginas
///   {{ rowIndex }}     — índice de fila (en contexto de tabla)
///   {{ $.path }}       — JSON Path sobre el data root capturado
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

        return expr switch
        {
            "pageNumber" => PageNumberPart.Instance,
            "totalPages" => TotalPagesPart.Instance,
            "rowIndex"   => RowIndexPart.Instance,
            "#group"     => GroupKeyPart.Instance,
            "#count"     => GroupCountPart.Instance,
            _ when expr.StartsWith('$') => JsonPathPart.Create(expr),
            _ => throw new FormatException(
                $"Expresión de template no reconocida: '{{{{ {expr} }}}}'. " +
                "Soportado: pageNumber, totalPages, rowIndex, #group, #count, $.path, $.path:format")
        };
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
            var colon = expr.IndexOf(':');
            if (colon < 0) return new JsonPathPart(expr.Trim(), null);
            var fmt = expr[(colon + 1)..].Trim();
            return new JsonPathPart(expr[..colon].Trim(), fmt.Length == 0 ? null : fmt);
        }

        public override string Evaluate(IEvaluationContext ctx, JsonElement contextRoot)
        {
            var el = JsonPath.Select(contextRoot, _path);
            if (_format is not null && JsonPath.ToBoxedValue(el) is IFormattable f)
                return f.ToString(_format, System.Globalization.CultureInfo.CurrentCulture);
            return JsonPath.ToDisplayString(el);
        }
    }
}
