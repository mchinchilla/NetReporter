using System.Globalization;
using System.Text;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using SkiaSharp;
using NrColor = NetReporter.Core.Primitives.Color;

namespace NetReporter.Svg;

/// <summary>
/// Escribe una <see cref="RenderPage"/> como SVG directamente, sin pasar por el canvas SVG de Skia.
/// <para>
/// Ese canvas reconstruye la tabla glifo→Unicode de toda la fuente en cada texto (coste proporcional
/// al número de glifos, bajo un lock global de FreeType/CoreText) y recodifica cada imagen en PNG en
/// cada página: un documento de 18 páginas tardaba 7× (Linux) a 37× (macOS) más que su PDF y rendía
/// menos cuantos más hilos lo renderizaban. Aquí cada texto cuesta una consulta al cmap, sumar los
/// avances de <see cref="GlyphAdvances"/> (la misma tabla con la que midió el LayoutEngine) y
/// formatear números; las imágenes se incrustan con sus bytes originales, en base64 una sola vez
/// por documento, o se referencian por URL (<see cref="SvgWriterContext.ImageHref"/>).
/// </para>
/// La geometría (alineación, línea base, recorte, encaje de imágenes, radios) es la de
/// <see cref="PageGeometry"/>, compartida con el pintor del PDF.
/// </summary>
internal static class SvgPageWriter
{
    private const string Svg = "http://www.w3.org/2000/svg";

    /// <summary>Grosor con el que se dibuja un trazo de grosor 0 (Skia lo pinta como "hairline").</summary>
    private const float Hairline = 0.5f;

    public static string Write(RenderPage page, float width, float height, SvgWriterContext ctx)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(ctx);

        var sb = ctx.Buffer;
        sb.Clear();
        sb.Append("<svg xmlns=\"").Append(Svg).Append("\" width=\"");
        Num(sb, width);
        sb.Append("\" height=\"");
        Num(sb, height);
        sb.Append("\" viewBox=\"0 0 ");
        Num(sb, width);
        sb.Append(' ');
        Num(sb, height);
        sb.Append("\">\n");

        var clipCount = 0;
        foreach (var cmd in page.Commands)
        {
            switch (cmd)
            {
                case DrawTextCommand text:
                    WriteText(sb, text, ctx, ref clipCount);
                    break;
                case DrawLineCommand line:
                    WriteLine(sb, line);
                    break;
                case DrawRectangleCommand rect:
                    WriteRectangle(sb, rect);
                    break;
                case DrawImageCommand img:
                    WriteImage(sb, img, ctx);
                    break;
                case DrawPathCommand path:
                    WritePath(sb, path);
                    break;
            }
        }

        sb.Append("</svg>\n");
        return sb.ToString();
    }

    // === Texto ===

    private static void WriteText(StringBuilder sb, DrawTextCommand cmd, SvgWriterContext ctx, ref int clipCount)
    {
        if (cmd.Text.Length == 0) return;

        var size = (float)cmd.Style.FontSize;
        var primary = SkiaFonts.Resolve(cmd.Style);
        var primaryFont = ctx.Paints.Font(primary, size);
        var metrics = ctx.Paints.Metrics(primary, size);
        var baseline = PageGeometry.TextBaseline(cmd, metrics);

        // Caso común: la fuente principal tiene todos los caracteres → un solo tramo, sin listas.
        // ContainsGlyphs y GetGlyphs consultan el cmap de la fuente, no el strike cache (el lock global).
        if (primaryFont.ContainsGlyphs(cmd.Text))
        {
            var advances = SkiaFonts.Advances(primary, size);
            var count = CodePoints(cmd.Text);
            Span<ushort> glyphs = count <= 256 ? stackalloc ushort[count] : new ushort[count];
            primaryFont.GetGlyphs(cmd.Text.AsSpan(), glyphs);
            var textWidth = advances.Width(glyphs);
            var x = PageGeometry.TextStartX(cmd, textWidth);

            var clipId = BeginClip(sb, cmd, textWidth, baseline, metrics, ref clipCount);
            WriteRun(sb, cmd, ctx, primary, size, advances, glyphs, x, baseline);
            EndClip(sb, clipId);
            return;
        }

        // Con caracteres que la fuente no tiene, los tramos de respaldo salen de SkiaFonts, como en el PDF.
        var runs = SkiaFonts.SplitRuns(cmd.Text, primary, primaryFont);
        var runGlyphs = new ushort[runs.Count][];
        float total = 0;
        for (var i = 0; i < runs.Count; i++)
        {
            runGlyphs[i] = SkiaFonts.Advances(runs[i].Typeface, size).Glyphs(runs[i].Text);
            total += SkiaFonts.Advances(runs[i].Typeface, size).Width(runGlyphs[i]);
        }

        var startX = PageGeometry.TextStartX(cmd, total);
        var clip = BeginClip(sb, cmd, total, baseline, metrics, ref clipCount);
        for (var i = 0; i < runs.Count; i++)
        {
            var advances = SkiaFonts.Advances(runs[i].Typeface, size);
            var runCmd = ReferenceEquals(runs[i].Text, cmd.Text) ? cmd : cmd with { Text = runs[i].Text };
            WriteRun(sb, runCmd, ctx, runs[i].Typeface, size, advances, runGlyphs[i], startX, baseline);
            startX += advances.Width(runGlyphs[i]);
        }
        EndClip(sb, clip);
    }

    /// <summary>
    /// Un <c>&lt;text&gt;</c> con la x de cada glifo: el navegador coloca cada carácter donde lo
    /// colocó Skia aunque sustituya la fuente. <c>xml:space="preserve"</c> evita que colapse los
    /// espacios, que dejarían de corresponder uno a uno con las posiciones.
    /// </summary>
    private static void WriteRun(StringBuilder sb, DrawTextCommand cmd, SvgWriterContext ctx, SKTypeface typeface,
                                 float size, GlyphAdvances advances, ReadOnlySpan<ushort> glyphs, float x, float baseline)
    {
        if (glyphs.Length == 0) return;
        var font = ctx.Font(cmd.Style.FontFamily, typeface);

        sb.Append("<text xml:space=\"preserve\" font-family=\"").Append(font.FamilyAttribute).Append("\" font-size=\"");
        Num(sb, size);
        sb.Append('"');
        if (font.Weight != (int)SKFontStyleWeight.Normal) sb.Append(" font-weight=\"").Append(font.Weight).Append('"');
        if (font.Italic) sb.Append(" font-style=\"italic\"");
        Fill(sb, cmd.Style.Foreground);
        sb.Append(" x=\"");
        for (var i = 0; i < glyphs.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            Num(sb, x);
            x += advances.Of(glyphs[i]);
        }
        sb.Append("\" y=\"");
        Num(sb, baseline);
        sb.Append("\">");
        Escape(sb, cmd.Text);
        sb.Append("</text>\n");
    }

    /// <summary>
    /// El pintor del PDF recorta todo texto a <see cref="PageGeometry.TextClip"/>. Aquí solo se emite
    /// el recorte cuando el texto saldría de él (desborda a lo ancho, o la caja es más baja que la
    /// línea): el resultado es el mismo y la mayoría de los textos no cargan con un clipPath.
    /// </summary>
    private static int BeginClip(StringBuilder sb, DrawTextCommand cmd, float textWidth, float baseline,
                                 in SKFontMetrics metrics, ref int clipCount)
    {
        var clip = PageGeometry.TextClip(cmd);
        var startX = PageGeometry.TextStartX(cmd, textWidth);
        var overflows = startX < clip.Left - 0.01f || startX + textWidth > clip.Right + 0.01f
                        || baseline + metrics.Ascent < clip.Top - 0.01f
                        || baseline + metrics.Descent > clip.Bottom + 0.01f;
        if (!overflows) return 0;

        var id = ++clipCount;
        sb.Append("<clipPath id=\"c").Append(id).Append("\"><rect x=\"");
        Num(sb, clip.Left);
        sb.Append("\" y=\"");
        Num(sb, clip.Top);
        sb.Append("\" width=\"");
        Num(sb, clip.Width);
        sb.Append("\" height=\"");
        Num(sb, clip.Height);
        sb.Append("\"/></clipPath>\n<g clip-path=\"url(#c").Append(id).Append(")\">\n");
        return id;
    }

    private static void EndClip(StringBuilder sb, int clipId)
    {
        if (clipId > 0) sb.Append("</g>\n");
    }

    /// <summary>Puntos de código del texto: un glifo (y una x) por cada uno; un par sustituto cuenta uno.</summary>
    private static int CodePoints(string text)
    {
        var count = 0;
        foreach (var c in text)
            if (!char.IsLowSurrogate(c)) count++;
        return count;
    }

    // === Formas ===

    private static void WriteLine(StringBuilder sb, DrawLineCommand cmd)
    {
        sb.Append("<line x1=\"");
        Num(sb, (float)cmd.From.X);
        sb.Append("\" y1=\"");
        Num(sb, (float)cmd.From.Y);
        sb.Append("\" x2=\"");
        Num(sb, (float)cmd.To.X);
        sb.Append("\" y2=\"");
        Num(sb, (float)cmd.To.Y);
        sb.Append('"');
        Stroke(sb, cmd.Color, (float)cmd.Thickness);
        sb.Append("/>\n");
    }

    private static void WriteRectangle(StringBuilder sb, DrawRectangleCommand cmd)
    {
        var hasFill = cmd.Fill is { A: > 0 };
        var hasBorder = cmd.Border is not null;
        if (!hasFill && !hasBorder) return;

        var rect = new SKRect((float)cmd.Bounds.X, (float)cmd.Bounds.Y, (float)cmd.Bounds.Right, (float)cmd.Bounds.Bottom);
        Span<float> radii = stackalloc float[4];
        var rounded = PageGeometry.RoundRectRadii(rect, (float)cmd.CornerRadius, cmd.Corners, radii);
        var uniform = rounded && radii[0] == radii[1] && radii[1] == radii[2] && radii[2] == radii[3];

        if (!rounded || uniform)
        {
            sb.Append("<rect x=\"");
            Num(sb, rect.Left);
            sb.Append("\" y=\"");
            Num(sb, rect.Top);
            sb.Append("\" width=\"");
            Num(sb, rect.Width);
            sb.Append("\" height=\"");
            Num(sb, rect.Height);
            sb.Append('"');
            if (uniform)
            {
                sb.Append(" rx=\"");
                Num(sb, radii[0]);
                sb.Append('"');
            }
        }
        else
        {
            // Esquinas distintas: el mismo contorno que SKRoundRect, con un arco por esquina redondeada.
            sb.Append("<path d=\"");
            RoundRectPath(sb, rect, radii);
            sb.Append('"');
        }

        if (hasFill) Fill(sb, cmd.Fill!.Value); else sb.Append(" fill=\"none\"");
        if (hasBorder) Stroke(sb, cmd.Border!.Value.Color, (float)cmd.Border.Value.Thickness);
        sb.Append("/>\n");
    }

    private static void RoundRectPath(StringBuilder sb, SKRect r, ReadOnlySpan<float> radii)
    {
        float tl = radii[0], tr = radii[1], br = radii[2], bl = radii[3];
        sb.Append('M'); Num(sb, r.Left + tl); sb.Append(' '); Num(sb, r.Top);
        sb.Append('H'); Num(sb, r.Right - tr);
        if (tr > 0) Arc(sb, tr, r.Right, r.Top + tr);
        sb.Append('V'); Num(sb, r.Bottom - br);
        if (br > 0) Arc(sb, br, r.Right - br, r.Bottom);
        sb.Append('H'); Num(sb, r.Left + bl);
        if (bl > 0) Arc(sb, bl, r.Left, r.Bottom - bl);
        sb.Append('V'); Num(sb, r.Top + tl);
        if (tl > 0) Arc(sb, tl, r.Left + tl, r.Top);
        sb.Append('Z');

        static void Arc(StringBuilder sb, float radius, float x, float y)
        {
            sb.Append('A'); Num(sb, radius); sb.Append(' '); Num(sb, radius);
            sb.Append(" 0 0 1 "); Num(sb, x); sb.Append(' '); Num(sb, y);
        }
    }

    private static void WritePath(StringBuilder sb, DrawPathCommand cmd)
    {
        if (cmd.Points.Count < 2) return;
        var hasFill = cmd.Closed && cmd.Fill is { A: > 0 };
        var hasStroke = cmd.Stroke is { Thickness: > 0 };
        if (!hasFill && !hasStroke) return;

        sb.Append("<path d=\"M");
        Num(sb, (float)cmd.Points[0].X);
        sb.Append(' ');
        Num(sb, (float)cmd.Points[0].Y);
        for (var i = 1; i < cmd.Points.Count; i++)
        {
            sb.Append('L');
            Num(sb, (float)cmd.Points[i].X);
            sb.Append(' ');
            Num(sb, (float)cmd.Points[i].Y);
        }
        if (cmd.Closed) sb.Append('Z');
        sb.Append('"');
        if (hasFill) Fill(sb, cmd.Fill!.Value); else sb.Append(" fill=\"none\"");
        if (hasStroke)
        {
            Stroke(sb, cmd.Stroke!.Value.Color, (float)cmd.Stroke.Value.Thickness);
            sb.Append(" stroke-linejoin=\"round\" stroke-linecap=\"round\"");
        }
        sb.Append("/>\n");
    }

    // === Imágenes ===

    private static void WriteImage(StringBuilder sb, DrawImageCommand cmd, SvgWriterContext ctx)
    {
        if (cmd.Data is null || cmd.Data.Length == 0) return;
        var image = ctx.Image(cmd.Data, cmd.MimeType);
        if (image is null) return;   // datos inválidos: el PDF tampoco la dibuja

        var target = PageGeometry.ImageTarget(cmd, image.Width, image.Height);
        sb.Append("<image x=\"");
        Num(sb, target.Left);
        sb.Append("\" y=\"");
        Num(sb, target.Top);
        sb.Append("\" width=\"");
        Num(sb, target.Width);
        sb.Append("\" height=\"");
        Num(sb, target.Height);
        sb.Append("\" preserveAspectRatio=\"none\" href=\"");
        var href = ctx.ImageHref?.Invoke(cmd.Data, image.Mime);
        if (href is not null) EscapeAttribute(sb, href);
        else sb.Append(image.DataUri(cmd.Data));
        sb.Append("\"/>\n");
    }

    // === Atributos ===

    private static void Fill(StringBuilder sb, NrColor color)
    {
        sb.Append(" fill=\"");
        Hex(sb, color);
        sb.Append('"');
        Opacity(sb, " fill-opacity=\"", color.A);
    }

    private static void Stroke(StringBuilder sb, NrColor color, float width)
    {
        sb.Append(" stroke=\"");
        Hex(sb, color);
        sb.Append("\" stroke-width=\"");
        Num(sb, width > 0 ? width : Hairline);
        sb.Append('"');
        Opacity(sb, " stroke-opacity=\"", color.A);
    }

    private static void Opacity(StringBuilder sb, string attribute, byte alpha)
    {
        if (alpha == 255) return;
        sb.Append(attribute);
        Num(sb, alpha / 255f);
        sb.Append('"');
    }

    private static void Hex(StringBuilder sb, NrColor c)
    {
        const string digits = "0123456789ABCDEF";
        sb.Append('#')
          .Append(digits[c.R >> 4]).Append(digits[c.R & 15])
          .Append(digits[c.G >> 4]).Append(digits[c.G & 15])
          .Append(digits[c.B >> 4]).Append(digits[c.B & 15]);
    }

    /// <summary>Número con hasta 3 decimales (milésimas de punto), sin notación científica ni cultura.</summary>
    internal static void Num(StringBuilder sb, float value)
    {
        Span<char> buffer = stackalloc char[32];
        if (float.IsFinite(value) && value.TryFormat(buffer, out var written, "0.###", CultureInfo.InvariantCulture))
            sb.Append(buffer[..written]);
        else
            sb.Append('0');
    }

    /// <summary>Contenido de texto: escapa lo que XML exige y sustituye los caracteres de control por espacios (la x de cada glifo debe seguir correspondiendo).</summary>
    private static void Escape(StringBuilder sb, string text)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case < ' ': sb.Append(' '); break;
                case '￾' or '￿': sb.Append('�'); break;
                default: sb.Append(c); break;
            }
        }
    }

    private static void EscapeAttribute(StringBuilder sb, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '"': sb.Append("&quot;"); break;
                case < ' ': break;
                default: sb.Append(c); break;
            }
        }
    }
}
