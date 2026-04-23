using System.Text;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SkiaSharp;
using NrColor = NetReporter.Core.Primitives.Color;
using NrFontWeight = NetReporter.Core.Styles.FontWeight;
using NrFontStyleKind = NetReporter.Core.Styles.FontStyleKind;
using NrTextAlignment = NetReporter.Core.Styles.TextAlignment;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Pdf;

/// <summary>
/// PDF renderer que consume RenderList. QuestPDF se usa solo como contenedor de documento/página;
/// todo el dibujo va por SkiaSharp → SVG, y QuestPDF lo inserta con container.Svg(svg).
/// Esta es la válvula de escape equivalente al Canvas API (deprecado desde QuestPDF 2024.3.0).
/// </summary>
public sealed class PdfRenderer
{
    public byte[] Render(RlRenderList renderList)
    {
        var pageWidth = (float)renderList.Page.Size.Width;
        var pageHeight = (float)renderList.Page.Size.Height;

        var document = Document.Create(container =>
        {
            foreach (var page in renderList.Pages)
            {
                var svg = RenderPageToSvg(page, pageWidth, pageHeight);

                container.Page(pd =>
                {
                    pd.Size(pageWidth, pageHeight, Unit.Point);
                    pd.Margin(0);  // el layout ya incluye márgenes en coords absolutas
                    pd.Content().Svg(svg);
                });
            }
        });

        return document.GeneratePdf();
    }

    private static string RenderPageToSvg(RenderPage page, float width, float height)
    {
        using var ms = new MemoryStream();
        var bounds = new SKRect(0, 0, width, height);

        using (var canvas = SKSvgCanvas.Create(bounds, ms))
        {
            DrawPage(canvas, page);
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

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
            }
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

        // Medir para aplicar alineación manual
        var textWidth = font.MeasureText(cmd.Text);
        var metrics = font.Metrics;

        float x = cmd.Style.TextAlign switch
        {
            NrTextAlignment.Center => (float)(cmd.Bounds.X + (cmd.Bounds.Width - textWidth) / 2),
            NrTextAlignment.Right  => (float)(cmd.Bounds.Right - textWidth),
            _                      => (float)cmd.Bounds.X
        };

        // Baseline: interpretamos Bounds como top-left, bajamos al baseline.
        float y = (float)cmd.Bounds.Y + (-metrics.Ascent);

        // Clip al rectángulo declarado: el texto que exceda los bounds queda recortado visualmente.
        // Con algo de margen vertical (0.5pt) para evitar recortar descenders en el borde inferior.
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

        if (cmd.Fill is { } fill && fill.A > 0)
        {
            using var fillPaint = new SKPaint
            {
                Color = ToSk(fill),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRect(rect, fillPaint);
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
            canvas.DrawRect(rect, borderPaint);
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
