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
            _ when expr.StartsWith('$') => new JsonPathPart(expr),
            _ => throw new FormatException(
                $"Expresión de template no reconocida: '{{{{ {expr} }}}}'. " +
                "Soportado: pageNumber, totalPages, rowIndex, $.path")
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

    private sealed class JsonPathPart : TemplatePart
    {
        private readonly string _path;
        public JsonPathPart(string path) => _path = path;
        public override string Evaluate(IEvaluationContext ctx, JsonElement contextRoot) =>
            JsonPath.ToDisplayString(JsonPath.Select(contextRoot, _path));
    }
}
