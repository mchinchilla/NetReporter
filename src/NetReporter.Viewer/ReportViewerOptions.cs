using NetReporter.Core.Layout;
using NetReporter.Pdf;
using NetReporter.Xlsx;

namespace NetReporter.Viewer;

/// <summary>Download formats offered by the viewer.</summary>
[Flags]
public enum ViewerFormats
{
    None = 0,
    Pdf = 1,
    Html = 2,
    Xlsx = 4,
    All = Pdf | Html | Xlsx,
}

/// <summary>Options for <c>MapNetReporterViewer</c> (see <see cref="ViewerEndpointExtensions"/>).</summary>
public sealed class ReportViewerOptions
{
    /// <summary>
    /// How long a laid-out document stays in memory after its last access (sliding). Page and export
    /// requests for an expired document return 404 and the viewer reopens the report.
    /// </summary>
    public TimeSpan DocumentLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Maximum number of laid-out documents kept in memory; when full, the least recently used are evicted.</summary>
    public int MaxCachedDocuments { get; set; } = 100;

    /// <summary>Download formats shown in the toolbar and served by the export endpoint. Printing needs <see cref="ViewerFormats.Pdf"/>.</summary>
    public ViewerFormats Formats { get; set; } = ViewerFormats.All;

    /// <summary>
    /// UI language of the full-page viewer (<c>"es"</c>, <c>"en"</c>, or one registered with
    /// <c>NetReporterViewer.registerLanguage</c>). Null, the default, negotiates it per request from
    /// <c>Accept-Language</c> among <see cref="SupportedLanguages"/>.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Languages the full-page viewer may be served in, most preferred first; the first one is the fallback
    /// when the browser asks for none of them. <c>es</c> and <c>en</c> ship with the component; add another
    /// here after registering its strings through <see cref="HeadHtml"/>.
    /// </summary>
    public IList<string> SupportedLanguages { get; } = new List<string> { "es", "en" };

    /// <summary>
    /// Raw HTML inserted in the <c>&lt;head&gt;</c> of the full-page viewer, before the component script:
    /// a <c>&lt;script&gt;</c> that defines <c>globalThis.NetReporterViewerI18n = { pt: { … } }</c> to add a
    /// language, a <c>&lt;style&gt;</c> with the <c>--nr-*</c> theme variables, a favicon link… It is emitted
    /// verbatim: never build it from user input.
    /// </summary>
    public string? HeadHtml { get; set; }

    /// <summary>
    /// When true, layout errors are sent to the browser with their message. Keep it false in production:
    /// messages may contain template or data details.
    /// </summary>
    public bool IncludeErrorDetails { get; set; }

    /// <summary>Text measurer for the layout. Defaults to <c>SkiaTextMeasurer.Instance</c> (what the SVG/PDF renderers draw with).</summary>
    public ITextMeasurer? TextMeasurer { get; set; }

    /// <summary>Barcode generator for the layout. Defaults to <c>ZXingBarcodeGenerator.Instance</c>.</summary>
    public IBarcodeMatrixGenerator? BarcodeGenerator { get; set; }

    /// <summary>Options for the PDF download and print.</summary>
    public PdfRendererOptions PdfOptions { get; set; } = new();

    /// <summary>Options for the XLSX download.</summary>
    public XlsxRenderOptions? XlsxOptions { get; set; }
}
