using System.Text;
using NetReporter.Core.RenderList;
using SkiaSharp;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Svg;

/// <summary>
/// Renderer SVG que consume <see cref="RlRenderList"/> y emite una cadena SVG por página
/// usando SkiaSharp (<see cref="SKSvgCanvas"/>). El dibujo lo hace <see cref="SkiaPagePainter"/>,
/// el mismo que usa el PdfRenderer.
/// </summary>
public sealed class SvgRenderer
{
    /// <summary>Renderiza cada página a SVG. Devuelve una lista paralela a <see cref="RlRenderList.Pages"/>.</summary>
    public IReadOnlyList<string> Render(RlRenderList renderList)
    {
        ArgumentNullException.ThrowIfNull(renderList);
        var pageWidth = (float)renderList.Page.Size.Width;
        var pageHeight = (float)renderList.Page.Size.Height;

        // Un cache para todo el documento: las fuentes e imágenes se crean una vez, no por página.
        using var cache = new SkiaPaintCache();
        var result = new string[renderList.Pages.Count];
        for (int i = 0; i < renderList.Pages.Count; i++)
            result[i] = RenderPage(renderList.Pages[i], pageWidth, pageHeight, cache);

        return result;
    }

    public string RenderPage(RenderPage page, float width, float height)
    {
        using var cache = new SkiaPaintCache();
        return RenderPage(page, width, height, cache);
    }

    private static string RenderPage(RenderPage page, float width, float height, SkiaPaintCache cache)
    {
        ArgumentNullException.ThrowIfNull(page);

        using var ms = new MemoryStream();
        var bounds = new SKRect(0, 0, width, height);

        using (var canvas = SKSvgCanvas.Create(bounds, ms))
        {
            SkiaPagePainter.Draw(canvas, page, cache);
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
