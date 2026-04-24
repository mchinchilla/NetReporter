using NetReporter.Svg;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Pdf;

/// <summary>
/// Renderer PDF. QuestPDF se usa solo como contenedor de documento/página; el dibujo delega
/// en <see cref="SvgRenderer"/> (SkiaSharp → SVG) e inyecta el SVG con container.Svg(...).
/// Esta es la válvula de escape equivalente al Canvas API (deprecado en QuestPDF 2024.3.0+).
/// </summary>
public sealed class PdfRenderer
{
    private readonly SvgRenderer _svg = new();

    public byte[] Render(RlRenderList renderList)
    {
        ArgumentNullException.ThrowIfNull(renderList);
        var pageWidth = (float)renderList.Page.Size.Width;
        var pageHeight = (float)renderList.Page.Size.Height;

        var svgPages = _svg.Render(renderList);

        var document = Document.Create(container =>
        {
            for (int i = 0; i < renderList.Pages.Count; i++)
            {
                var svg = svgPages[i];
                container.Page(pd =>
                {
                    pd.Size(pageWidth, pageHeight, Unit.Point);
                    pd.Margin(0);
                    pd.Content().Svg(svg);
                });
            }
        });

        return document.GeneratePdf();
    }
}
