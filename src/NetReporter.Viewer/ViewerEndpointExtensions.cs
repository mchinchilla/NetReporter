using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using NetReporter.Barcodes;
using NetReporter.Core.Definition;
using NetReporter.Core.Layout;
using NetReporter.Html;
using NetReporter.Pdf;
using NetReporter.Svg;
using NetReporter.Xlsx;

namespace NetReporter.Viewer;

/// <summary>
/// Maps the report viewer:
/// <list type="bullet">
/// <item><c>GET {prefix}/{reportId}?…</c> — full-page viewer.</item>
/// <item><c>GET {prefix}/{reportId}/open?…</c> — lays the report out once and returns its metadata (JSON).</item>
/// <item><c>GET {prefix}/_doc/{token}/pages/{n}</c> — page <c>n</c> (1-based) as SVG.</item>
/// <item><c>GET {prefix}/_doc/{token}/export/{pdf|html|xlsx}</c> — download (<c>?inline=1</c> to display).</item>
/// <item><c>GET {prefix}/_viewer/viewer.js</c> — the <c>&lt;netreporter-viewer&gt;</c> web component, to embed the viewer in the app's own pages.</item>
/// </list>
/// The returned group accepts the usual conventions (<c>.RequireAuthorization()</c>, rate limiting…).
/// </summary>
public static class ViewerEndpointExtensions
{
    private const string AssetResource = "NetReporter.Viewer.viewer.js";
    private static readonly Lazy<(byte[] Bytes, string ETag)> ViewerScript = new(LoadScript);

    /// <summary>Maps the viewer using the <see cref="IReportSource"/> registered in DI.</summary>
    public static RouteGroupBuilder MapNetReporterViewer(
        this IEndpointRouteBuilder endpoints, string prefix, Action<ReportViewerOptions>? configure = null)
        => Map(endpoints, prefix, source: null, configure);

    /// <summary>Maps the viewer resolving reports with <paramref name="resolve"/>.</summary>
    public static RouteGroupBuilder MapNetReporterViewer(
        this IEndpointRouteBuilder endpoints,
        string prefix,
        Func<ReportRequest, CancellationToken, ValueTask<ReportDefinition?>> resolve,
        Action<ReportViewerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        return Map(endpoints, prefix, new DelegateReportSource(resolve), configure);
    }

    private static RouteGroupBuilder Map(
        IEndpointRouteBuilder endpoints, string prefix, IReportSource? source, Action<ReportViewerOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        prefix = "/" + prefix.Trim('/');

        var options = new ReportViewerOptions();
        configure?.Invoke(options);
        var viewer = new Viewer(prefix, options, source, new ViewerDocumentStore(options));

        var group = endpoints.MapGroup(prefix);
        group.MapGet("/_viewer/viewer.js", ServeScript);
        group.MapGet("/_doc/{token}/pages/{page:int}", viewer.ServePage);
        group.MapGet("/_doc/{token}/export/{format}", viewer.ServeExport);
        group.MapGet("/{reportId}/open", viewer.Open);
        group.MapGet("/{reportId}", viewer.ServeViewerPage);
        return group;
    }

    private static Task ServeScript(HttpContext ctx)
    {
        var (bytes, etag) = ViewerScript.Value;
        ctx.Response.Headers.ETag = etag;
        ctx.Response.Headers.CacheControl = "public, max-age=86400";
        if (ctx.Request.Headers.IfNoneMatch == etag)
        {
            ctx.Response.StatusCode = StatusCodes.Status304NotModified;
            return Task.CompletedTask;
        }
        ctx.Response.ContentType = "text/javascript; charset=utf-8";
        return ctx.Response.Body.WriteAsync(bytes).AsTask();
    }

    private static (byte[], string) LoadScript()
    {
        using var stream = typeof(ViewerEndpointExtensions).Assembly.GetManifestResourceStream(AssetResource)
            ?? throw new InvalidOperationException($"Embedded resource '{AssetResource}' not found.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
        return (bytes, $"\"{hash}\"");
    }

    internal static string ScriptVersion => ViewerScript.Value.ETag.Trim('"');

    private sealed class Viewer(string prefix, ReportViewerOptions options, IReportSource? source, ViewerDocumentStore store)
    {
        private static string? CurrentUser(HttpContext ctx)
            => ctx.User.Identity?.IsAuthenticated == true ? ctx.User.Identity.Name : null;

        private static ILogger Logger(HttpContext ctx)
            => ctx.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("NetReporter.Viewer")
               ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        private string BaseUrl(HttpContext ctx) => ctx.Request.PathBase.Add(prefix).Value!;

        public Task ServeViewerPage(HttpContext ctx)
        {
            var reportId = (string)ctx.GetRouteValue("reportId")!;
            var baseUrl = BaseUrl(ctx);
            var src = $"{baseUrl}/{Uri.EscapeDataString(reportId)}/open{ctx.Request.QueryString}";
            var lang = WebUtility.HtmlEncode(options.Language);

            var html = $$"""
                <!doctype html>
                <html lang="{{lang}}">
                <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{{WebUtility.HtmlEncode(reportId)}}</title>
                <style>html,body{margin:0;height:100%;background:#525659}netreporter-viewer{display:block;height:100%}</style>
                <script type="module" src="{{WebUtility.HtmlEncode($"{baseUrl}/_viewer/viewer.js?v={ScriptVersion}")}}"></script>
                </head>
                <body>
                <netreporter-viewer src="{{WebUtility.HtmlEncode(src)}}" lang="{{lang}}" set-title></netreporter-viewer>
                </body>
                </html>
                """;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-store";
            return ctx.Response.WriteAsync(html, Encoding.UTF8);
        }

        public async Task Open(HttpContext ctx)
        {
            var reportId = (string)ctx.GetRouteValue("reportId")!;
            var aborted = ctx.RequestAborted;
            ctx.Response.Headers.CacheControl = "no-store";

            ReportDefinition? report;
            Core.RenderList.RenderList layout;
            try
            {
                var resolver = source ?? ctx.RequestServices.GetRequiredService<IReportSource>();
                report = await resolver.ResolveAsync(new ReportRequest(reportId, ctx), aborted);
                if (report is null)
                {
                    await WriteError(ctx, StatusCodes.Status404NotFound, "not-found", null);
                    return;
                }

                var engine = new LayoutEngine(
                    options.TextMeasurer ?? SkiaTextMeasurer.Instance,
                    options.BarcodeGenerator ?? ZXingBarcodeGenerator.Instance);
                layout = engine.Layout(report, aborted);
            }
            catch (OperationCanceledException) when (aborted.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger(ctx).LogError(ex, "NetReporter viewer could not open report {ReportId}", reportId);
                await WriteError(ctx, StatusCodes.Status500InternalServerError, "render-failed",
                    options.IncludeErrorDetails ? $"{ex.GetType().Name}: {ex.Message}" : null);
                return;
            }

            var doc = new ViewerDocument(reportId, report, layout, CurrentUser(ctx));
            var token = store.Add(doc);
            var docUrl = $"{BaseUrl(ctx)}/_doc/{token}";

            ctx.Response.ContentType = "application/json; charset=utf-8";
            await using var json = new Utf8JsonWriter(ctx.Response.Body);
            json.WriteStartObject();
            json.WriteString("title", report.Title ?? report.Name);
            json.WriteString("fileName", FileBaseName(report));
            json.WriteNumber("pageCount", doc.PageCount);
            json.WriteNumber("pageWidth", layout.Page.Size.Width);
            json.WriteNumber("pageHeight", layout.Page.Size.Height);
            json.WriteString("pageUrl", docUrl + "/pages/");
            json.WriteStartObject("exports");
            if (options.Formats.HasFlag(ViewerFormats.Pdf)) json.WriteString("pdf", docUrl + "/export/pdf");
            if (options.Formats.HasFlag(ViewerFormats.Xlsx)) json.WriteString("xlsx", docUrl + "/export/xlsx");
            if (options.Formats.HasFlag(ViewerFormats.Html)) json.WriteString("html", docUrl + "/export/html");
            json.WriteEndObject();
            json.WriteEndObject();
        }

        public Task ServePage(HttpContext ctx)
        {
            var doc = store.Get((string)ctx.GetRouteValue("token")!, CurrentUser(ctx));
            var pageOk = int.TryParse(ctx.GetRouteValue("page")?.ToString(),
                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var page);
            if (doc is null || !pageOk || page < 1 || page > doc.PageCount)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            var svg = doc.GetSvgPage(page - 1);
            ctx.Response.ContentType = "image/svg+xml; charset=utf-8";
            ctx.Response.Headers.CacheControl = "private, max-age=3600";
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            // The SVG is only ever used as an image; forbid scripts if someone opens it directly.
            ctx.Response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src data:; style-src 'unsafe-inline'";
            return ctx.Response.WriteAsync(svg, Encoding.UTF8);
        }

        public async Task ServeExport(HttpContext ctx)
        {
            var doc = store.Get((string)ctx.GetRouteValue("token")!, CurrentUser(ctx));
            var format = ((string)ctx.GetRouteValue("format")!).ToLowerInvariant();
            var allowed = format switch
            {
                "pdf" => options.Formats.HasFlag(ViewerFormats.Pdf),
                "html" => options.Formats.HasFlag(ViewerFormats.Html),
                "xlsx" => options.Formats.HasFlag(ViewerFormats.Xlsx),
                _ => false,
            };
            if (doc is null || !allowed)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var aborted = ctx.RequestAborted;
            byte[] bytes;
            string contentType;
            try
            {
                (bytes, contentType) = format switch
                {
                    "pdf" => (new PdfRenderer(options.PdfOptions).Render(doc.RenderList, aborted), "application/pdf"),
                    "html" => (Encoding.UTF8.GetBytes(new HtmlRenderer().Render(doc.RenderList,
                                  new HtmlRenderOptions { Title = doc.Report.Title ?? doc.Report.Name }, aborted)),
                              "text/html; charset=utf-8"),
                    _ => (new XlsxRenderer().Render(doc.Report, options.XlsxOptions),
                          "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
                };
            }
            catch (OperationCanceledException) when (aborted.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger(ctx).LogError(ex, "NetReporter viewer could not export report {ReportId} to {Format}", doc.ReportId, format);
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return;
            }

            var inline = ctx.Request.Query["inline"] == "1";
            var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
            disposition.SetHttpFileName($"{FileBaseName(doc.Report)}.{format}");
            ctx.Response.Headers.ContentDisposition = disposition.ToString();
            ctx.Response.Headers.CacheControl = "private, no-cache";
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength = bytes.Length;
            await ctx.Response.Body.WriteAsync(bytes, aborted);
        }

        private static async Task WriteError(HttpContext ctx, int status, string code, string? detail)
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            await using var json = new Utf8JsonWriter(ctx.Response.Body);
            json.WriteStartObject();
            json.WriteString("error", code);
            if (detail is not null) json.WriteString("detail", detail);
            json.WriteEndObject();
        }
    }

    /// <summary>File name without extension: resolved <c>fileName:</c> → title → name, minus invalid characters.</summary>
    internal static string FileBaseName(ReportDefinition report)
    {
        var raw = !string.IsNullOrWhiteSpace(report.FileName) ? report.FileName
                : !string.IsNullOrWhiteSpace(report.Title) ? report.Title
                : report.Name;
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw.Trim())
            sb.Append(Array.IndexOf(invalid, c) >= 0 || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c);
        var name = sb.ToString().Trim('.', ' ');
        return name.Length == 0 ? "report" : name;
    }
}
