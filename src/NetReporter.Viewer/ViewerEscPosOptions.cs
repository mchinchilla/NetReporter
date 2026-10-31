using Microsoft.AspNetCore.Http;
using NetReporter.EscPos;

namespace NetReporter.Viewer;

/// <summary>
/// ESC/POS in the viewer: download the ticket bytes (<c>GET …/export/escpos</c>) and print straight to a
/// thermal printer (<c>POST …/print</c>). Set it on <see cref="ReportViewerOptions.EscPos"/>.
/// </summary>
public sealed class ViewerEscPosOptions
{
    /// <summary>Printer profile and renderer behavior (dots per line, cut, drawer, dithering…).</summary>
    public EscPosRendererOptions Renderer { get; set; } = EscPosRendererOptions.ForRoll80();

    /// <summary>Offer the <c>.escpos</c> download (<c>export/escpos</c>). On by default.</summary>
    public bool AllowDownload { get; set; } = true;

    /// <summary>
    /// Resolves the printer for the current request (user, branch, till…) given the <see cref="HttpContext"/>
    /// and the report id. Return <c>null</c> when that user has no printer (the endpoint answers 404 with
    /// <c>{"error":"no-printer"}</c>). Without this delegate <c>POST …/print</c> is not offered at all.
    /// </summary>
    public Func<HttpContext, string, CancellationToken, ValueTask<IEscPosTransport?>>? Printer { get; set; }

    /// <summary>
    /// Before printing, query the printer status (<c>DLE EOT</c>) when the transport implements
    /// <see cref="IEscPosStatusSource"/> and answer <c>409</c> with the cause (<c>paper-end</c>, <c>cover-open</c>,
    /// <c>offline</c>, <c>error</c>) instead of sending a ticket that will never come out. On by default.
    /// </summary>
    public bool CheckStatusBeforePrint { get; set; } = true;

    /// <summary>One TCP printer for every request (single-till setups).</summary>
    public ViewerEscPosOptions UseTcpPrinter(EscPosTcpPrinterOptions printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        var transport = new EscPosTcpPrinter(printer);
        Printer = (_, _, _) => ValueTask.FromResult<IEscPosTransport?>(transport);
        return this;
    }

    /// <summary>One TCP printer for every request, by host and port.</summary>
    public ViewerEscPosOptions UseTcpPrinter(string host, int port = 9100) =>
        UseTcpPrinter(new EscPosTcpPrinterOptions { Host = host, Port = port });
}
