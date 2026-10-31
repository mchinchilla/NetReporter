using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using SkiaSharp;
using NrColor = NetReporter.Core.Primitives.Color;


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

        // Fill estira a la caja; Contain preserva la proporción centrando (PageGeometry, compartida con el SVG).
        var target = PageGeometry.ImageTarget(cmd, image.Width, image.Height);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        canvas.DrawImage(image, target, sampling);
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

        // Alineación, línea base y recorte: PageGeometry, la misma que escribe el SVG.
        float x = PageGeometry.TextStartX(cmd, textWidth);
        float y = PageGeometry.TextBaseline(cmd, metrics);

        var paint = cache.Fill(ToSk(cmd.Style.Foreground));
        canvas.Save();
        canvas.ClipRect(PageGeometry.TextClip(cmd));
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
        // seleccionada) se cae al rectángulo recto de siempre. Los radios ya vienen ajustados a
        // la caja por PageGeometry, los mismos que escribe el SVG.
        Span<float> radii = stackalloc float[4];
        using var rounded = PageGeometry.RoundRectRadii(rect, r, cmd.Corners, radii)
            ? BuildRoundRect(rect, radii)
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
    /// Rectángulo con los radios de <see cref="PageGeometry.RoundRectRadii"/>: superior-izq,
    /// superior-der, inferior-der, inferior-izq (el orden que espera Skia).
    /// </summary>
    private static SKRoundRect BuildRoundRect(SKRect rect, ReadOnlySpan<float> radii)
    {
        var rounded = new SKRoundRect();
        rounded.SetRectRadii(rect,
        [
            new SKPoint(radii[0], radii[0]),
            new SKPoint(radii[1], radii[1]),
            new SKPoint(radii[2], radii[2]),
            new SKPoint(radii[3], radii[3])
        ]);
        return rounded;
    }

    private static SKColor ToSk(NrColor c) => new(c.R, c.G, c.B, c.A);
}
