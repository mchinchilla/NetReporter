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

        if (opts.FullDocument)
        {
            if (opts.ShowZoomControls) WriteZoomBar(sb, opts);
            WriteDocumentEnd(sb);
        }

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
        if (opts.ShowZoomControls) WriteZoomCss(sb);
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
    }

    // === Zoom controls ===

    private static void WriteZoomCss(StringBuilder sb)
    {
        sb.AppendLine(".nr-zoom-bar { position: fixed; bottom: 16px; right: 16px; display: flex; align-items: center; gap: 2px; background: #fff; border: 1px solid #e5e7eb; border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,0.12), 0 2px 4px rgba(0,0,0,0.06); padding: 4px; font-family: -apple-system, BlinkMacSystemFont, \"Segoe UI\", Arial, sans-serif; font-size: 13px; z-index: 1000; user-select: none; }");
        sb.AppendLine(".nr-zoom-btn { width: 28px; height: 28px; display: inline-flex; align-items: center; justify-content: center; background: transparent; border: 0; border-radius: 4px; color: #4b5563; cursor: pointer; font-size: 16px; line-height: 1; padding: 0; }");
        sb.AppendLine(".nr-zoom-btn:hover { background: #f3f4f6; color: #111827; }");
        sb.AppendLine(".nr-zoom-btn:active { background: #e5e7eb; }");
        sb.AppendLine(".nr-zoom-btn:disabled { color: #d1d5db; cursor: not-allowed; }");
        sb.AppendLine(".nr-zoom-btn:disabled:hover { background: transparent; }");
        sb.AppendLine(".nr-zoom-label { min-width: 48px; text-align: center; color: #1f2937; font-variant-numeric: tabular-nums; }");
        sb.AppendLine(".nr-zoom-sep { width: 1px; height: 18px; background: #e5e7eb; margin: 0 2px; }");
        sb.AppendLine("@media print { .nr-zoom-bar { display: none !important; } }");
    }

    private static void WriteZoomBar(StringBuilder sb, HtmlRenderOptions opts)
    {
        var initialZoom = opts.InitialZoom <= 0 ? 1.0 : opts.InitialZoom;
        sb.AppendLine("<div class=\"nr-zoom-bar\" role=\"toolbar\" aria-label=\"Zoom\">");
        sb.AppendLine("  <button type=\"button\" class=\"nr-zoom-btn\" data-nr-zoom=\"out\" title=\"Reducir (Ctrl+-)\" aria-label=\"Reducir\">&minus;</button>");
        sb.AppendLine("  <span class=\"nr-zoom-label\" data-nr-zoom-label>100%</span>");
        sb.AppendLine("  <button type=\"button\" class=\"nr-zoom-btn\" data-nr-zoom=\"in\" title=\"Ampliar (Ctrl++)\" aria-label=\"Ampliar\">+</button>");
        sb.AppendLine("  <span class=\"nr-zoom-sep\"></span>");
        sb.AppendLine("  <button type=\"button\" class=\"nr-zoom-btn\" data-nr-zoom=\"fit\" title=\"Ajustar al ancho\" aria-label=\"Ajustar al ancho\">&#x2194;</button>");
        sb.AppendLine("  <button type=\"button\" class=\"nr-zoom-btn\" data-nr-zoom=\"reset\" title=\"Restablecer (Ctrl+0)\" aria-label=\"Restablecer\">&#x00B7;</button>");
        sb.AppendLine("</div>");
        sb.Append("<script>(function(){var INITIAL=").Append(initialZoom.ToString("0.###", Inv)).AppendLine(";");
        sb.AppendLine("var STEPS=[0.5,0.6,0.7,0.8,0.9,1,1.1,1.25,1.5,1.75,2,2.5,3];");
        sb.AppendLine("var KEY='nr-zoom:'+location.pathname+':'+document.title;");
        sb.AppendLine("var pages=document.querySelectorAll('.nr-page');");
        sb.AppendLine("var label=document.querySelector('[data-nr-zoom-label]');");
        sb.AppendLine("var bar=document.querySelector('.nr-zoom-bar');");
        sb.AppendLine("if(!pages.length||!bar)return;");
        sb.AppendLine("var natural=pages[0].offsetWidth;");
        sb.AppendLine("var stored=parseFloat(localStorage.getItem(KEY));");
        sb.AppendLine("var z=isFinite(stored)&&stored>0?stored:INITIAL;");
        sb.AppendLine("function clamp(v){return Math.max(0.25,Math.min(4,v));}");
        sb.AppendLine("function apply(){z=clamp(z);for(var i=0;i<pages.length;i++){pages[i].style.zoom=z;}if(label)label.textContent=Math.round(z*100)+'%';try{localStorage.setItem(KEY,z);}catch(e){}}");
        sb.AppendLine("function step(d){var idx=0,best=Infinity;for(var i=0;i<STEPS.length;i++){var x=Math.abs(STEPS[i]-z);if(x<best){best=x;idx=i;}}idx=Math.max(0,Math.min(STEPS.length-1,idx+d));z=STEPS[idx];apply();}");
        sb.AppendLine("function fit(){if(!natural)return;var avail=window.innerWidth-48;z=clamp(avail/natural);apply();}");
        sb.AppendLine("bar.addEventListener('click',function(e){var btn=e.target.closest('[data-nr-zoom]');if(!btn)return;var a=btn.getAttribute('data-nr-zoom');if(a==='in')step(1);else if(a==='out')step(-1);else if(a==='reset'){z=1;apply();}else if(a==='fit')fit();});");
        sb.AppendLine("document.addEventListener('keydown',function(e){if(!(e.ctrlKey||e.metaKey))return;if(e.key==='='||e.key==='+'){e.preventDefault();step(1);}else if(e.key==='-'){e.preventDefault();step(-1);}else if(e.key==='0'){e.preventDefault();z=1;apply();}});");
        sb.AppendLine("apply();})();</script>");
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
                case DrawImageCommand i:     WriteImage(sb, i); break;
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
        if (cmd.CornerRadius > 0 && cmd.Corners != RectCorners.None)
        {
            // Orden CSS: superior-izq, superior-der, inferior-der, inferior-izq (igual que Skia).
            var r = Px(cmd.CornerRadius);
            string Corner(RectCorners corner) => cmd.Corners.HasFlag(corner) ? $"{r}px" : "0";
            sb.Append("border-radius:")
              .Append(Corner(RectCorners.TopLeft)).Append(' ')
              .Append(Corner(RectCorners.TopRight)).Append(' ')
              .Append(Corner(RectCorners.BottomRight)).Append(' ')
              .Append(Corner(RectCorners.BottomLeft)).Append(';');
        }
        sb.AppendLine("\"></div>");
    }

    private static void WriteImage(StringBuilder sb, DrawImageCommand cmd)
    {
        if (cmd.Data is null || cmd.Data.Length == 0) return;

        var base64 = Convert.ToBase64String(cmd.Data);
        var fitStyle = cmd.Fit == NetReporter.Core.Elements.ImageFit.Fill
            ? "fill"        // distorsiona para llenar (object-fit: fill)
            : "contain";    // preserva aspect ratio

        sb.Append("<img class=\"nr-el\" alt=\"\" style=\"");
        WriteRect(sb, cmd.Bounds);
        sb.Append("object-fit:").Append(fitStyle).Append(';');
        sb.Append("\" src=\"data:");
        sb.Append(WebUtility.HtmlEncode(cmd.MimeType));
        sb.Append(";base64,");
        sb.Append(base64);
        sb.AppendLine("\">");
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
