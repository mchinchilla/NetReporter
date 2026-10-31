using NetReporter.Core.RenderList;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Svg;

/// <summary>
/// Renderer SVG: una cadena SVG por página, escrita por <see cref="SvgPageWriter"/> con la misma
/// geometría y las mismas fuentes que el PDF (<see cref="PageGeometry"/>, <see cref="SkiaFonts"/>,
/// <see cref="GlyphAdvances"/>), pero sin pasar por el canvas SVG de Skia, cuyo coste por texto crecía
/// con el tamaño de la fuente y serializaba a los hilos. Cada glifo lleva su posición, así que el
/// navegador respeta la maquetación aunque sustituya la fuente.
/// </summary>
public sealed class SvgRenderer
{
    /// <summary>Renderiza cada página a SVG. Devuelve una lista paralela a <see cref="RlRenderList.Pages"/>.</summary>
    public IReadOnlyList<string> Render(RlRenderList renderList) => Render(renderList, CancellationToken.None);

    /// <summary>
    /// Igual que <see cref="Render(RlRenderList)"/>, abortando con
    /// <see cref="OperationCanceledException"/> cuando se cancela <paramref name="cancellationToken"/>
    /// (se revisa por página).
    /// </summary>
    public IReadOnlyList<string> Render(RlRenderList renderList, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(renderList);
        var pageWidth = (float)renderList.Page.Size.Width;
        var pageHeight = (float)renderList.Page.Size.Height;

        // Un contexto para todo el documento: fuentes, métricas y el base64 de cada imagen se calculan una vez.
        using var context = new SvgWriterContext();
        var result = new string[renderList.Pages.Count];
        for (int i = 0; i < renderList.Pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[i] = SvgPageWriter.Write(renderList.Pages[i], pageWidth, pageHeight, context);
        }

        return result;
    }

    /// <summary>Renderiza una página aislada. Para varias páginas del mismo documento, <see cref="Render(RlRenderList)"/> reutiliza fuentes e imágenes.</summary>
    public string RenderPage(RenderPage page, float width, float height)
    {
        using var context = new SvgWriterContext();
        return SvgPageWriter.Write(page, width, height, context);
    }

    /// <summary>Renderiza una página con un contexto provisto por el llamador (uno por documento; no thread-safe).</summary>
    internal static string RenderPage(RenderPage page, float width, float height, SvgWriterContext context) =>
        SvgPageWriter.Write(page, width, height, context);
}
