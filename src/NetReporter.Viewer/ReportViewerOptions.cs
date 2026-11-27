using Microsoft.AspNetCore.Http;
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

    /// <summary>The report's first table as CSV (<see cref="NetReporter.Core.Export.CsvRenderer"/>); only offered when the report has one.</summary>
    Csv = 8,
    All = Pdf | Html | Xlsx | Csv,
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
    /// Per-request UI language of the full-page viewer (e.g. the signed-in user's preference). A null or empty result
    /// falls back to <see cref="Language"/> and then to <c>Accept-Language</c> negotiation.
    /// </summary>
    public Func<HttpContext, string?>? LanguageResolver { get; set; }

    /// <summary>
    /// Per-request HTML added to the full-page viewer's <c>&lt;head&gt;</c> after <see cref="HeadHtml"/> (e.g. a
    /// <c>&lt;link&gt;</c> to the user's theme). Emitted verbatim like <see cref="HeadHtml"/>: return only markup the
    /// application controls, never user input.
    /// </summary>
    public Func<HttpContext, string?>? HeadHtmlResolver { get; set; }

    /// <summary>
    /// Key of the user a document belongs to. Defaults to <c>User.Identity.Name</c> (null when anonymous); supply a
    /// stable id when display names may repeat. The key is combined with the viewer's base URL, so with a route
    /// parameter in the prefix (<c>/c/{tenant}/viewer</c>) a document opened under one value isn't served under another.
    /// </summary>
    public Func<HttpContext, string?>? DocumentOwner { get; set; }

    /// <summary>
    /// Called after an export is rendered and before it is sent (downloads and the PDF used to print). Use it for an
    /// audit record. If it throws, the viewer answers 500 and sends nothing. Not called for ESC/POS printing (<c>POST …/print</c>).
    /// </summary>
    public Func<ViewerExportContext, CancellationToken, ValueTask>? OnExport { get; set; }

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

    /// <summary>Options for the CSV download (the report's first table).</summary>
    public NetReporter.Core.Export.CsvRenderOptions? CsvOptions { get; set; }

    /// <summary>
    /// ESC/POS for thermal printers: <c>null</c> (default) offers nothing; an instance enables the
    /// <c>.escpos</c> download and, with <see cref="ViewerEscPosOptions.Printer"/>, <c>POST …/print</c>.
    /// </summary>
    public ViewerEscPosOptions? EscPos { get; set; }
}

/// <summary>An export about to be sent, for <see cref="ReportViewerOptions.OnExport"/>.</summary>
public sealed class ViewerExportContext
{
    internal ViewerExportContext(HttpContext httpContext, string reportId, string format, long bytes, bool inline, object? state)
    {
        HttpContext = httpContext;
        ReportId = reportId;
        Format = format;
        Bytes = bytes;
        Inline = inline;
        State = state;
    }

    /// <summary>The export request: user, services.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>The report id the document was opened with.</summary>
    public string ReportId { get; }

    /// <summary><c>pdf</c>, <c>html</c>, <c>xlsx</c> or <c>escpos</c>.</summary>
    public string Format { get; }

    /// <summary>Size of the file being sent.</summary>
    public long Bytes { get; }

    /// <summary>True when shown in the browser (<c>?inline=1</c>, which the viewer uses to print) rather than downloaded.</summary>
    public bool Inline { get; }

    /// <summary>What the resolver attached at open (<see cref="ReportRequest.State"/>).</summary>
    public object? State { get; }
}
