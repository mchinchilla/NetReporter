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
/// </summary>
public static class SkiaPagePainter
{
    /// <param name="canvas">Lienzo destino (SVG, página de un SKDocument PDF, bitmap…).</param>
    /// <param name="page">Página ya maquetada por el LayoutEngine.</param>
    /// <param name="mapTypeface">
    /// Sustituye la tipografía justo antes de dibujar. El PdfRenderer la usa para pintar con
    /// fuentes recortadas a los glifos usados; las medidas se siguen tomando de la original.
    /// </param>
    public static void Draw(SKCanvas canvas, RenderPage page, Func<SKTypeface, SKTypeface>? mapTypeface = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(page);

        foreach (var cmd in page.Commands)
        {
            switch (cmd)
            {
                case DrawTextCommand text:
                    DrawText(canvas, text, mapTypeface);
                    break;
                case DrawLineCommand line:
                    DrawLine(canvas, line);
                    break;
                case DrawRectangleCommand rect:
                    DrawRectangle(canvas, rect);
                    break;
                case DrawImageCommand img:
                    DrawImage(canvas, img);
                    break;
                case DrawPathCommand path:
                    DrawPath(canvas, path);
                    break;
            }
        }
    }

    private static void DrawImage(SKCanvas canvas, DrawImageCommand cmd)
    {
        if (cmd.Data is null || cmd.Data.Length == 0) return;
        using var image = SKImage.FromEncodedData(cmd.Data);
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

    private static void DrawText(SKCanvas canvas, DrawTextCommand cmd, Func<SKTypeface, SKTypeface>? mapTypeface)
    {
        using var paint = new SKPaint
        {
            Color = ToSk(cmd.Style.Foreground),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        var size = (float)cmd.Style.FontSize;
        var primary = SkiaFonts.Resolve(cmd.Style);
        var runs = SkiaFonts.SplitRuns(cmd.Text, primary);

        // Anchos con la tipografía original (la misma que usó el medidor); la mapeada solo dibuja.
        var widths = new float[runs.Count];
        float textWidth = 0;
        for (var i = 0; i < runs.Count; i++)
        {
            using var measureFont = SkiaFonts.CreateFont(runs[i].Typeface, size);
            widths[i] = measureFont.MeasureText(runs[i].Text);
            textWidth += widths[i];
        }

        using var primaryFont = SkiaFonts.CreateFont(primary, size);
        var metrics = primaryFont.Metrics;

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

        canvas.Save();
        canvas.ClipRect(clipRect);
        for (var i = 0; i < runs.Count; i++)
        {
            var typeface = mapTypeface?.Invoke(runs[i].Typeface) ?? runs[i].Typeface;
            using var font = SkiaFonts.CreateFont(typeface, size);
            canvas.DrawText(runs[i].Text, x, y, SKTextAlign.Left, font, paint);
            x += widths[i];
        }
        canvas.Restore();
    }

    private static void DrawLine(SKCanvas canvas, DrawLineCommand cmd)
    {
        using var paint = new SKPaint
        {
            Color = ToSk(cmd.Color),
            StrokeWidth = (float)cmd.Thickness,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        canvas.DrawLine(
            (float)cmd.From.X, (float)cmd.From.Y,
            (float)cmd.To.X,   (float)cmd.To.Y,
            paint);
    }

    private static void DrawRectangle(SKCanvas canvas, DrawRectangleCommand cmd)
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
            using var fillPaint = new SKPaint
            {
                Color = ToSk(fill),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            if (rounded is not null) canvas.DrawRoundRect(rounded, fillPaint);
            else canvas.DrawRect(rect, fillPaint);
        }

        if (cmd.Border is { } border)
        {
            using var borderPaint = new SKPaint
            {
                Color = ToSk(border.Color),
                StrokeWidth = (float)border.Thickness,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };
            if (rounded is not null) canvas.DrawRoundRect(rounded, borderPaint);
            else canvas.DrawRect(rect, borderPaint);
        }
    }

    private static void DrawPath(SKCanvas canvas, DrawPathCommand cmd)
    {
        if (cmd.Points.Count < 2) return;

        using var builder = new SKPathBuilder();
        builder.MoveTo((float)cmd.Points[0].X, (float)cmd.Points[0].Y);
        for (var i = 1; i < cmd.Points.Count; i++)
            builder.LineTo((float)cmd.Points[i].X, (float)cmd.Points[i].Y);
        if (cmd.Closed) builder.Close();
        using var path = builder.Detach();

        if (cmd.Closed && cmd.Fill is { } fill && fill.A > 0)
        {
            using var fillPaint = new SKPaint { Color = ToSk(fill), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawPath(path, fillPaint);
        }

        if (cmd.Stroke is { } stroke && stroke.Thickness > 0)
        {
            using var strokePaint = new SKPaint
            {
                Color = ToSk(stroke.Color),
                StrokeWidth = (float)stroke.Thickness,
                Style = SKPaintStyle.Stroke,
                StrokeJoin = SKStrokeJoin.Round,
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true
            };
            canvas.DrawPath(path, strokePaint);
        }
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
