using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using NetReporter.EscPos;

namespace NetReporter.Viewer;

/// <summary>
/// ESC/POS in the viewer: download the ticket bytes (<c>GET …/export/escpos</c>), print straight to a
/// thermal printer (<c>POST …/print</c>) and, optionally, let each browser configure its own printer
/// (<c>POST …/printer-status</c> + a settings dialog in the component). Set it on <see cref="ReportViewerOptions.EscPos"/>.
/// </summary>
public sealed class ViewerEscPosOptions
{
    /// <summary>Printer profile and renderer behavior (dots per line, cut, drawer, dithering…). Client settings, when allowed, override it per request.</summary>
    public EscPosRendererOptions Renderer { get; set; } = EscPosRendererOptions.ForRoll80();

    /// <summary>Offer the <c>.escpos</c> download (<c>export/escpos</c>). On by default.</summary>
    public bool AllowDownload { get; set; } = true;

    /// <summary>
    /// Resolves the printer for the current request (user, branch, till…) given the <see cref="HttpContext"/>
    /// and the report id. Return <c>null</c> when that user has no printer (the endpoint answers 404 with
    /// <c>{"error":"no-printer"}</c>). Used when the browser sent no printer of its own.
    /// </summary>
    public Func<HttpContext, string, CancellationToken, ValueTask<IEscPosTransport?>>? Printer { get; set; }

    /// <summary>
    /// Before printing, query the printer status (<c>DLE EOT</c>) when the transport implements
    /// <see cref="IEscPosStatusSource"/> and answer <c>409</c> with the cause (<c>paper-end</c>, <c>cover-open</c>,
    /// <c>offline</c>, <c>error</c>) instead of sending a ticket that will never come out. On by default.
    /// </summary>
    public bool CheckStatusBeforePrint { get; set; } = true;

    /// <summary>
    /// Let each browser configure its own printer (host, port, paper, cut, drawer, dithering) in the
    /// component's settings dialog; the settings live in that browser's <c>localStorage</c> and travel with
    /// every print/status request. <b>Off by default</b>: it makes the server open a TCP connection to an
    /// address chosen by the client, so <see cref="IsPrinterAllowed"/> decides which targets are acceptable
    /// (by default only literal private, loopback and link-local IP addresses).
    /// </summary>
    public bool AllowClientSettings { get; set; }

    /// <summary>
    /// Accepts or rejects a printer address sent by the browser (<c>host</c>, <c>port</c>). Default:
    /// <see cref="DefaultIsPrinterAllowed"/> (private/loopback/link-local IP literals; host names rejected).
    /// Rejected printers answer <c>403 {"error":"printer-not-allowed"}</c>.
    /// </summary>
    public Func<string, int, bool> IsPrinterAllowed { get; set; } = DefaultIsPrinterAllowed;

    /// <summary>Timeouts (and <c>NoDelay</c>) applied to client-configured printers; host and port come from the browser.</summary>
    public EscPosTcpPrinterOptions ClientPrinterDefaults { get; set; } = new()
    {
        Host = "client",
        ConnectTimeout = TimeSpan.FromSeconds(3),
        SendTimeout = TimeSpan.FromSeconds(15),
        StatusTimeout = TimeSpan.FromSeconds(2),
    };

    /// <summary>Creates the transport for a client-configured printer. Default: <see cref="EscPosTcpPrinter"/>; tests and apps may substitute.</summary>
    public Func<EscPosTcpPrinterOptions, IEscPosTransport> TransportFactory { get; set; } = o => new EscPosTcpPrinter(o);

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

    /// <summary>Only literal IP addresses in private (RFC 1918 / fc00::/7), loopback or link-local ranges, on a valid port.</summary>
    public static bool DefaultIsPrinterAllowed(string host, int port)
    {
        if (port is < 1 or > 65535 || !IPAddress.TryParse(host, out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }
        return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal;
    }
}
