using NetReporter.Core.Layout;
using NetReporter.Pdf;
using NetReporter.Xlsx;

namespace NetReporter.Viewer.Desktop;

/// <summary>How a <see cref="DesktopReportDocument"/> lays the report out and exports it.</summary>
public sealed class DesktopReportOptions
{
    /// <summary>Text measurer for the layout. Defaults to <c>SkiaTextMeasurer.Instance</c> (what the painter draws with).</summary>
    public ITextMeasurer? TextMeasurer { get; set; }

    /// <summary>Barcode generator for the layout. Defaults to <c>ZXingBarcodeGenerator.Instance</c>.</summary>
    public IBarcodeMatrixGenerator? BarcodeGenerator { get; set; }

    /// <summary>Options for the PDF export.</summary>
    public PdfRendererOptions PdfOptions { get; set; } = new();

    /// <summary>Options for the XLSX export (null = renderer defaults).</summary>
    public XlsxRenderOptions? XlsxOptions { get; set; }
}
