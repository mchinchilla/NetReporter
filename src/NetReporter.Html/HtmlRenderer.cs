using System.Globalization;
using System.Net;
using System.Text;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Html;

/// <summary>
/// Consume un <see cref="RlRenderList"/> y emite HTML paginado. Cada página va en una
/// <c>&lt;section class="nr-page"&gt;</c> con dimensiones en puntos (1pt = 1px).
/// CSS <c>@page</c> y <c>page-break-after</c> controlan el print.
/// </summary>
public sealed class HtmlRenderer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string Render(RlRenderList renderList, HtmlRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(renderList);
        var opts = options ?? new HtmlRenderOptions();

        var sb = new StringBuilder(4096);
        if (opts.FullDocument) WriteDocumentStart(sb, renderList.Page, opts);

        foreach (var page in renderList.Pages)
            WritePage(sb, page, renderList.Page);

        if (opts.FullDocument) WriteDocumentEnd(sb);

        return sb.ToString();
    }

    // === Document ===

    private static void WriteDocumentStart(StringBuilder sb, PageSetup page, HtmlRenderOptions opts)
    {
        var w = Px(page.Size.Width);
        var h = Px(page.Size.Height);

        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"es\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.Append("<title>").Append(WebUtility.HtmlEncode(opts.Title)).AppendLine("</title>");
        sb.AppendLine("<style>");
        sb.AppendLine($"@page {{ size: {w}pt {h}pt; margin: 0; }}");
        sb.AppendLine("html, body { margin: 0; padding: 0; }");
        sb.AppendLine($".nr-page {{ position: relative; width: {w}px; height: {h}px; overflow: hidden; box-sizing: border-box; background: white; }}");
        sb.AppendLine(".nr-el { position: absolute; box-sizing: border-box; }");
        sb.AppendLine(".nr-text { overflow: hidden; white-space: nowrap; }");
        if (opts.ShowScreenChrome)
        {
            sb.AppendLine("@media screen { body { background: #e2e8f0; padding: 24px 0; } ");
            sb.AppendLine("  .nr-page { margin: 0 auto 24px; box-shadow: 0 1px 3px rgba(0,0,0,0.1), 0 1px 2px rgba(0,0,0,0.06); } }");
        }
        sb.AppendLine("@media print {");
        sb.AppendLine("  body { background: white; }");
        sb.AppendLine("  .nr-page { margin: 0; box-shadow: none; page-break-after: always; }");
        sb.AppendLine("  .nr-page:last-child { page-break-after: auto; }");
        sb.AppendLine("}");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
    }

    private static void WriteDocumentEnd(StringBuilder sb)
    {
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
    }

    private static void WritePage(StringBuilder sb, RenderPage page, PageSetup setup)
    {
        sb.Append("<section class=\"nr-page\" data-page=\"").Append(page.PageNumber).AppendLine("\">");
        foreach (var cmd in page.Commands)
        {
            switch (cmd)
            {
                case DrawTextCommand t:      WriteText(sb, t); break;
                case DrawLineCommand l:      WriteLine(sb, l); break;
                case DrawRectangleCommand r: WriteRectangle(sb, r); break;
            }
        }
        sb.AppendLine("</section>");
    }

    // === Commands ===

    private static void WriteText(StringBuilder sb, DrawTextCommand cmd)
    {
        var s = cmd.Style;
        sb.Append("<span class=\"nr-el nr-text\" style=\"");
        WriteRect(sb, cmd.Bounds);
        sb.Append("font-family:").Append(CssFontFamily(s.FontFamily)).Append(';');
        sb.Append("font-size:").Append(Px(s.FontSize)).Append("px;");
        if (s.Weight == FontWeight.Bold) sb.Append("font-weight:700;");
        if (s.Italic == FontStyleKind.Italic) sb.Append("font-style:italic;");
        sb.Append("color:").Append(Hex(s.Foreground)).Append(';');
        if (s.Background.A > 0)
            sb.Append("background:").Append(Rgba(s.Background)).Append(';');
        sb.Append("text-align:").Append(CssTextAlign(s.TextAlign)).Append(';');
        // Vertical alignment aproximada con line-height = altura del rect.
        sb.Append("line-height:").Append(Px(cmd.Bounds.Height)).Append("px;");
        // Padding del estilo dentro del span (no del rect — el rect ya fue shrinked por el engine).
        sb.Append("\">");
        sb.Append(WebUtility.HtmlEncode(cmd.Text));
        sb.AppendLine("</span>");
    }

    private static void WriteLine(StringBuilder sb, DrawLineCommand cmd)
    {
        // El engine genera líneas horizontales (from.y == to.y) o verticales.
        var isHorizontal = Math.Abs(cmd.From.Y - cmd.To.Y) < 0.01;
        var thickness = Math.Max(cmd.Thickness, 0.5);

        sb.Append("<div class=\"nr-el\" style=\"");
        if (isHorizontal)
        {
            var left = Math.Min(cmd.From.X, cmd.To.X);
            var width = Math.Abs(cmd.To.X - cmd.From.X);
            sb.Append("left:").Append(Px(left)).Append("px;");
            sb.Append("top:").Append(Px(cmd.From.Y - thickness / 2)).Append("px;");
            sb.Append("width:").Append(Px(width)).Append("px;");
            sb.Append("height:").Append(Px(thickness)).Append("px;");
        }
        else
        {
            var top = Math.Min(cmd.From.Y, cmd.To.Y);
            var height = Math.Abs(cmd.To.Y - cmd.From.Y);
            sb.Append("left:").Append(Px(cmd.From.X - thickness / 2)).Append("px;");
            sb.Append("top:").Append(Px(top)).Append("px;");
            sb.Append("width:").Append(Px(thickness)).Append("px;");
            sb.Append("height:").Append(Px(height)).Append("px;");
        }
        sb.Append("background:").Append(Hex(cmd.Color)).Append(';');
        sb.AppendLine("\"></div>");
    }

    private static void WriteRectangle(StringBuilder sb, DrawRectangleCommand cmd)
    {
        sb.Append("<div class=\"nr-el\" style=\"");
        WriteRect(sb, cmd.Bounds);
        if (cmd.Fill is { } fill && fill.A > 0)
            sb.Append("background:").Append(Hex(fill)).Append(';');
        if (cmd.Border is { } border)
            sb.Append("border:").Append(Px(border.Thickness)).Append("px solid ").Append(Hex(border.Color)).Append(';');
        sb.AppendLine("\"></div>");
    }

    // === Helpers ===

    private static void WriteRect(StringBuilder sb, Rect r)
    {
        sb.Append("left:").Append(Px(r.X)).Append("px;");
        sb.Append("top:").Append(Px(r.Y)).Append("px;");
        sb.Append("width:").Append(Px(r.Width)).Append("px;");
        sb.Append("height:").Append(Px(r.Height)).Append("px;");
    }

    private static string Px(double v) => v.ToString("0.##", Inv);

    private static string Hex(Color c) => c.A == 255 ? c.ToHexString() : Rgba(c);

    private static string Rgba(Color c) =>
        c.A == 255
            ? $"rgb({c.R},{c.G},{c.B})"
            : string.Create(Inv, $"rgba({c.R},{c.G},{c.B},{c.A / 255.0:0.###})");

    private static string CssTextAlign(Core.Styles.TextAlignment a) => a switch
    {
        Core.Styles.TextAlignment.Center  => "center",
        Core.Styles.TextAlignment.Right   => "right",
        Core.Styles.TextAlignment.Justify => "justify",
        _ => "left"
    };

    private static string CssFontFamily(string family)
    {
        // Escapar con comillas si el nombre tiene espacio. Agrego fallback genérico.
        var needsQuote = family.IndexOfAny(new[] { ' ', ',' }) >= 0;
        var quoted = needsQuote ? $"\"{family}\"" : family;
        return $"{quoted}, Arial, sans-serif";
    }
}
