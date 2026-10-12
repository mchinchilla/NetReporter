using NetReporter.Svg;
using SkiaSharp;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Pdf;

/// <summary>
/// Renderer PDF. Escribe el documento con el backend PDF de Skia (<see cref="SKDocument"/>) y
/// dibuja cada página con <see cref="SkiaPagePainter"/>, el mismo pintor del SvgRenderer: vectorial,
/// con texto seleccionable y exactamente las fuentes con las que midió el LayoutEngine.
/// Las fuentes se embeben recortadas a los glifos usados (ver <see cref="PdfRendererOptions.SubsetFonts"/>).
/// </summary>
public sealed class PdfRenderer
{
    private readonly PdfRendererOptions _options;

    public PdfRenderer() : this(new PdfRendererOptions()) { }

    public PdfRenderer(PdfRendererOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public byte[] Render(RlRenderList renderList)
    {
        ArgumentNullException.ThrowIfNull(renderList);
        var pageWidth = (float)renderList.Page.Size.Width;
        var pageHeight = (float)renderList.Page.Size.Height;

        using var fonts = _options.SubsetFonts ? FontSubsetPlan.Build(renderList) : FontSubsetPlan.Empty();

        // Partir de Default es obligatorio: trae EncodingQuality = 101 (imágenes sin pérdida) y
        // RasterDpi = 72. Un `new SKDocumentPdfMetadata()` los deja en 0 y Skia recomprime las
        // imágenes como JPEG de calidad mínima.
        var metadata = SKDocumentPdfMetadata.Default;
        metadata.Creator = "NetReporter";
        metadata.Producer = "NetReporter";
        metadata.Creation = metadata.Modified = DateTime.Now;

        using var output = new MemoryStream();
        using (var document = SKDocument.CreatePdf(output, metadata)
                              ?? throw new InvalidOperationException("SkiaSharp no pudo crear el documento PDF."))
        {
            foreach (var page in renderList.Pages)
            {
                var canvas = document.BeginPage(pageWidth, pageHeight);
                SkiaPagePainter.Draw(canvas, page, fonts.Map);
                document.EndPage();
            }
            document.Close();
        }

        return output.ToArray();
    }
}
