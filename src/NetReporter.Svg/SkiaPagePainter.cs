using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using SkiaSharp;
using NrColor = NetReporter.Core.Primitives.Color;
using NrTextAlignment = NetReporter.Core.Styles.TextAlignment;

namespace NetReporter.Svg;

/// <summary>
/// Pinta los comandos de una <see cref="RenderPage"/> sobre cualquier <see cref="SKCanvas"/>.
/// Lo comparten el <see cref="SvgRenderer"/> (SKSvgCanvas) y el PdfRenderer (SKDocument), así
/// que ambos formatos dibujan exactamente lo mismo. Cada texto se recorta a su Bounds con
/// ClipRect para que el overflow no escape la caja.
/// <para>
/// Las fuentes, imágenes y pinturas salen de un <see cref="SkiaPaintCache"/>: los renderers crean
/// uno por documento para que no se instancie un SKFont/SKPaint por texto ni se decodifique la
/// misma imagen en cada página.
/// </para>
/// </summary>
public static class SkiaPagePainter
{
    /// <summary>Pinta una página aislada con un <see cref="SkiaPaintCache"/> propio.</summary>
    /// <param name="canvas">Lienzo destino (SVG, página de un SKDocument PDF, bitmap…).</param>
    /// <param name="page">Página ya maquetada por el LayoutEngine.</param>
    /// <param name="mapTypeface">
    /// Sustituye la tipografía justo antes de dibujar. El PdfRenderer la usa para pintar con
    /// fuentes recortadas a los glifos usados; las medidas se siguen tomando de la original.
    /// </param>
    public static void Draw(SKCanvas canvas, RenderPage page, Func<SKTypeface, SKTypeface>? mapTypeface = null)
    {
        using var cache = new SkiaPaintCache();
        Draw(canvas, page, cache, mapTypeface);
    }

    /// <param name="canvas">Lienzo destino (SVG, página de un SKDocument PDF, bitmap…).</param>
    /// <param name="page">Página ya maquetada por el LayoutEngine.</param>
    /// <param name="cache">
    /// Fuentes, imágenes y pinturas compartidas entre las páginas de un documento. Compartir el
    /// mismo cache entre páginas es lo que permite al PDF embeber una imagen repetida una sola vez.
    /// </param>
    /// <param name="mapTypeface">
    /// Sustituye la tipografía justo antes de dibujar. El PdfRenderer la usa para pintar con
    /// fuentes recortadas a los glifos usados; las medidas se siguen tomando de la original.
    /// </param>
    public static void Draw(SKCanvas canvas, RenderPage page, SkiaPaintCache cache, Func<SKTypeface, SKTypeface>? mapTypeface = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(cache);

        foreach (var cmd in page.Commands)
        {
            switch (cmd)
            {
                case DrawTextCommand text:
                    DrawText(canvas, text, cache, mapTypeface);
                    break;
                case DrawLineCommand line:
                    DrawLine(canvas, line, cache);
                    break;
                case DrawRectangleCommand rect:
                    DrawRectangle(canvas, rect, cache);
                    break;
                case DrawImageCommand img:
                    DrawImage(canvas, img, cache);
                    break;
                case DrawPathCommand path:
                    DrawPath(canvas, path, cache);
                    break;
            }
        }
    }

    private static void DrawImage(SKCanvas canvas, DrawImageCommand cmd, SkiaPaintCache cache)
    {
        if (cmd.Data is null || cmd.Data.Length == 0) return;
        var image = cache.Image(cmd.Data);
        if (image is null) return;     // datos inválidos

        var dest = new SKRect(
            (float)cmd.Bounds.X,
            (float)cmd.Bounds.Y,
            (float)cmd.Bounds.Right,
            (float)cmd.Bounds.Bottom);

        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

        if (cmd.Fit == NetReporter.Core.Elements.ImageFit.Fill)
        {
            canvas.DrawImage(image, dest, sampling);
        }
        else
        {
            // Contain: preserva aspect ratio, centrando dentro del rect.
            var imgRatio = (double)image.Width / image.Height;
            var dstRatio = cmd.Bounds.Width / cmd.Bounds.Height;
            SKRect target;
            if (imgRatio > dstRatio)
            {
                // Limitado por ancho
                var h = cmd.Bounds.Width / imgRatio;
                var top = cmd.Bounds.Y + (cmd.Bounds.Height - h) / 2;
                target = new SKRect((float)cmd.Bounds.X, (float)top,
                                    (float)cmd.Bounds.Right, (float)(top + h));
            }
            else
            {
                var w = cmd.Bounds.Height * imgRatio;
                var left = cmd.Bounds.X + (cmd.Bounds.Width - w) / 2;
                target = new SKRect((float)left, (float)cmd.Bounds.Y,
                                    (float)(left + w), (float)cmd.Bounds.Bottom);
            }
            canvas.DrawImage(image, target, sampling);
        }
    }

    private static void DrawText(SKCanvas canvas, DrawTextCommand cmd, SkiaPaintCache cache, Func<SKTypeface, SKTypeface>? mapTypeface)
    {
        var size = (float)cmd.Style.FontSize;
        var primary = SkiaFonts.Resolve(cmd.Style);
        var primaryFont = cache.Font(primary, size);
        var runs = SkiaFonts.SplitRuns(cmd.Text, primary, primaryFont);

        // Glifos y anchos con la tipografía original (la misma que usó el medidor); la mapeada solo
        // dibuja. Los anchos salen de la tabla de avances en vez de MeasureText, y los glifos se
        // dibujan ya posicionados: así Skia no consulta su strike cache (un lock global) ni para
        // medir ni para colocar el texto, que era lo que serializaba a los hilos que pintan en paralelo.
        var glyphs = new ushort[runs.Count][];
        Span<float> widths = runs.Count <= 8 ? stackalloc float[8] : new float[runs.Count];
        widths = widths[..runs.Count];
        float textWidth = 0;
        for (var i = 0; i < runs.Count; i++)
        {
            var advances = SkiaFonts.Advances(runs[i].Typeface, size);
            glyphs[i] = advances.Glyphs(runs[i].Text);
            widths[i] = advances.Width(glyphs[i]);
            textWidth += widths[i];
        }

        var metrics = cache.Metrics(primary, size);

        float x = cmd.Style.TextAlign switch
        {
            NrTextAlignment.Center => (float)(cmd.Bounds.X + (cmd.Bounds.Width - textWidth) / 2),
            NrTextAlignment.Right  => (float)(cmd.Bounds.Right - textWidth),
            _                      => (float)cmd.Bounds.X
        };

        // Vertical placement of the (single) line within the cell box: baseline = top + topOffset + ascent.
        // Center / Bottom push it down by the leftover height; Top (default) keeps the old behaviour.
        var lineHeight = metrics.Descent - metrics.Ascent;
        float topOffset = cmd.Style.VerticalAlign switch
        {
            NetReporter.Core.Styles.VerticalAlignment.Center => (float)Math.Max(0, (cmd.Bounds.Height - lineHeight) / 2),
            NetReporter.Core.Styles.VerticalAlignment.Bottom => (float)Math.Max(0, cmd.Bounds.Height - lineHeight),
            _                                                => 0f
        };
        float y = (float)cmd.Bounds.Y + topOffset + (-metrics.Ascent);

        // El recorte evita que el texto desborde a lo ancho. En vertical se deja un margen de ~0.35em:
        // los acentos de las mayúsculas (Á, Í, Ó, Ñ…) sobresalen del ascent de la fuente y, con un
        // recorte exacto a la caja, se perdían ("MARÍA" se veía como "MARIA").
        var verticalSlack = (float)(cmd.Style.FontSize * 0.35);
        var clipRect = new SKRect(
            (float)cmd.Bounds.X,
            (float)cmd.Bounds.Y - verticalSlack,
            (float)cmd.Bounds.Right,
            (float)cmd.Bounds.Bottom + verticalSlack);

        var paint = cache.Fill(ToSk(cmd.Style.Foreground));
        canvas.Save();
        canvas.ClipRect(clipRect);
        for (var i = 0; i < runs.Count; i++)
        {
            if (glyphs[i].Length > 0)
            {
                var typeface = mapTypeface?.Invoke(runs[i].Typeface) ?? runs[i].Typeface;
                // Bounds holgados, relativos al origen del tramo: solo sirven al quickReject de Skia, y
                // calcular los exactos volvería a pasar por el strike cache.
                var bounds = new SKRect(-size, metrics.Ascent - size, widths[i] + size, metrics.Descent + size);
                var run = cache.BlobBuilder.AllocatePositionedRun(cache.Font(typeface, size), glyphs[i].Length, bounds);
                glyphs[i].CopyTo(run.Glyphs);
                SkiaFonts.Advances(runs[i].Typeface, size).Position(glyphs[i], run.Positions);
                using var blob = cache.BlobBuilder.Build();
                if (blob is not null) canvas.DrawText(blob, x, y, paint);
            }
            x += widths[i];
        }
        canvas.Restore();
    }

    private static void DrawLine(SKCanvas canvas, DrawLineCommand cmd, SkiaPaintCache cache)
    {
        canvas.DrawLine(
            (float)cmd.From.X, (float)cmd.From.Y,
            (float)cmd.To.X,   (float)cmd.To.Y,
            cache.Stroke(ToSk(cmd.Color), (float)cmd.Thickness));
    }

    private static void DrawRectangle(SKCanvas canvas, DrawRectangleCommand cmd, SkiaPaintCache cache)
    {
        var rect = new SKRect(
            (float)cmd.Bounds.X,
            (float)cmd.Bounds.Y,
            (float)cmd.Bounds.Right,
            (float)cmd.Bounds.Bottom);
        var r = (float)cmd.CornerRadius;

        // Un solo SKRoundRect para relleno y borde: si no hay radio (o ninguna esquina
        // seleccionada) se cae al rectángulo recto de siempre.
        using var rounded = r > 0 && cmd.Corners != RectCorners.None
            ? BuildRoundRect(rect, r, cmd.Corners)
            : null;

        if (cmd.Fill is { } fill && fill.A > 0)
        {
            var fillPaint = cache.Fill(ToSk(fill));
            if (rounded is not null) canvas.DrawRoundRect(rounded, fillPaint);
            else canvas.DrawRect(rect, fillPaint);
        }

        if (cmd.Border is { } border)
        {
            var borderPaint = cache.Stroke(ToSk(border.Color), (float)border.Thickness);
            if (rounded is not null) canvas.DrawRoundRect(rounded, borderPaint);
            else canvas.DrawRect(rect, borderPaint);
        }
    }

    private static void DrawPath(SKCanvas canvas, DrawPathCommand cmd, SkiaPaintCache cache)
    {
        if (cmd.Points.Count < 2) return;

        using var builder = new SKPathBuilder();
        builder.MoveTo((float)cmd.Points[0].X, (float)cmd.Points[0].Y);
        for (var i = 1; i < cmd.Points.Count; i++)
            builder.LineTo((float)cmd.Points[i].X, (float)cmd.Points[i].Y);
        if (cmd.Closed) builder.Close();
        using var path = builder.Detach();

        if (cmd.Closed && cmd.Fill is { } fill && fill.A > 0)
            canvas.DrawPath(path, cache.Fill(ToSk(fill)));

        if (cmd.Stroke is { } stroke && stroke.Thickness > 0)
            canvas.DrawPath(path, cache.Stroke(ToSk(stroke.Color), (float)stroke.Thickness, SKStrokeJoin.Round, SKStrokeCap.Round));
    }

    /// <summary>
    /// Rectángulo con radio aplicado solo a las esquinas pedidas. El orden de los radios que
    /// espera Skia es superior-izq, superior-der, inferior-der, inferior-izq.
    /// </summary>
    private static SKRoundRect BuildRoundRect(SKRect rect, float radius, RectCorners corners)
    {
        SKPoint R(RectCorners corner) =>
            corners.HasFlag(corner) ? new SKPoint(radius, radius) : SKPoint.Empty;

        var rounded = new SKRoundRect();
        rounded.SetRectRadii(rect,
        [
            R(RectCorners.TopLeft),
            R(RectCorners.TopRight),
            R(RectCorners.BottomRight),
            R(RectCorners.BottomLeft)
        ]);
        return rounded;
    }

    private static SKColor ToSk(NrColor c) => new(c.R, c.G, c.B, c.A);
}
