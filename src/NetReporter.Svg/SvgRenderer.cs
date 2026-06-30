using System.Text;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;
using SkiaSharp;
using NrColor = NetReporter.Core.Primitives.Color;
using NrFontWeight = NetReporter.Core.Styles.FontWeight;
using NrFontStyleKind = NetReporter.Core.Styles.FontStyleKind;
using NrTextAlignment = NetReporter.Core.Styles.TextAlignment;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Svg;

/// <summary>
/// Renderer SVG que consume <see cref="RlRenderList"/> y emite una cadena SVG por página
/// usando SkiaSharp (<see cref="SKSvgCanvas"/>). Cada texto se recorta a su Bounds
/// con ClipRect para que el overflow no escape la caja.
/// </summary>
public sealed class SvgRenderer
{
    /// <summary>Renderiza cada página a SVG. Devuelve una lista paralela a <see cref="RlRenderList.Pages"/>.</summary>
    public IReadOnlyList<string> Render(RlRenderList renderList)
    {
        ArgumentNullException.ThrowIfNull(renderList);
        var pageWidth = (float)renderList.Page.Size.Width;
        var pageHeight = (float)renderList.Page.Size.Height;

        var result = new string[renderList.Pages.Count];
        for (int i = 0; i < renderList.Pages.Count; i++)
            result[i] = RenderPage(renderList.Pages[i], pageWidth, pageHeight);

        return result;
    }

    public string RenderPage(RenderPage page, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(page);

        using var ms = new MemoryStream();
        var bounds = new SKRect(0, 0, width, height);

        using (var canvas = SKSvgCanvas.Create(bounds, ms))
        {
            DrawPage(canvas, page);
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // === Drawing primitives ===

    private static void DrawPage(SKCanvas canvas, RenderPage page)
    {
        foreach (var cmd in page.Commands)
        {
            switch (cmd)
            {
                case DrawTextCommand text:
                    DrawText(canvas, text);
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

    private static void DrawText(SKCanvas canvas, DrawTextCommand cmd)
    {
        using var paint = new SKPaint
        {
            Color = ToSk(cmd.Style.Foreground),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        using var font = new SKFont
        {
            Size = (float)cmd.Style.FontSize,
            Typeface = ResolveTypeface(cmd.Style)
        };

        var textWidth = font.MeasureText(cmd.Text);
        var metrics = font.Metrics;

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

        var clipRect = new SKRect(
            (float)cmd.Bounds.X,
            (float)cmd.Bounds.Y,
            (float)cmd.Bounds.Right,
            (float)cmd.Bounds.Bottom);

        canvas.Save();
        canvas.ClipRect(clipRect);
        canvas.DrawText(cmd.Text, x, y, SKTextAlign.Left, font, paint);
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

        if (cmd.Fill is { } fill && fill.A > 0)
        {
            using var fillPaint = new SKPaint
            {
                Color = ToSk(fill),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            if (r > 0) canvas.DrawRoundRect(rect, r, r, fillPaint);
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
            if (r > 0) canvas.DrawRoundRect(rect, r, r, borderPaint);
            else canvas.DrawRect(rect, borderPaint);
        }
    }

    private static SKColor ToSk(NrColor c) => new(c.R, c.G, c.B, c.A);

    private static SKTypeface ResolveTypeface(ResolvedStyle style)
    {
        var slant = style.Italic == NrFontStyleKind.Italic
            ? SKFontStyleSlant.Italic
            : SKFontStyleSlant.Upright;
        var weight = style.Weight == NrFontWeight.Bold
            ? SKFontStyleWeight.Bold
            : SKFontStyleWeight.Normal;

        return SKTypeface.FromFamilyName(style.FontFamily, weight, SKFontStyleWidth.Normal, slant)
               ?? SKTypeface.Default;
    }
}
