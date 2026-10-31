using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetReporter.Core.Definition;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Templates;

namespace NetReporter.Viewer.Tests;

public sealed class ViewerEndpointTests : IAsyncLifetime
{
    private static readonly TemplateReport Template =
        YamlReportLoader.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "report.yaml")));

    private static readonly byte[] Logo = TestImages.Png(12, 6);
    private static readonly ReportDefinition ReportWithLogo = YamlReportLoader.Parse($$"""
        name: ConLogo
        page: { size: Letter }
        styles:
          Default: { fontFamily: Helvetica, fontSize: 10 }
        bands:
          - kind: PageHeader
            height: 40
            elements:
              - type: image
                bounds: { x: 0, y: 0, width: 120, height: 30 }
                source: "data:image/png;base64,{{Convert.ToBase64String(Logo)}}"
          - kind: Detail
            height: 20
            elements:
              - type: text
                bounds: { x: 0, y: 0, width: 200, height: 12 }
                content: "hola"
        """).Bind("{}");

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private int _resolveCalls;

    private static ReportDefinition BuildReport(int rows)
    {
        var json = new StringBuilder("""{"titulo":"Clientes","generadoEn":"hoy","clientes":[""");
        for (int i = 0; i < rows; i++)
        {
            if (i > 0) json.Append(',');
            json.Append($$"""{"codigo":"C{{i:000}}","nombre":"Cliente {{i}}","email":"c{{i}}@x.hn","credito":{{i * 10}}}""");
        }
        json.Append("]}");
        return Template.Bind(json.ToString());
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Test", null);
        _app = builder.Build();
        _app.UseAuthentication();

        _app.MapNetReporterViewer("/reports", (req, _) =>
        {
            Interlocked.Increment(ref _resolveCalls);
            return ValueTask.FromResult(req.ReportId switch
            {
                "clientes" => BuildReport(int.Parse(req["rows"] ?? "5")),
                "broken" => throw new InvalidOperationException("secret detail"),
                _ => (ReportDefinition?)null,
            });
        });
        _app.MapNetReporterViewer("/pdf-only", (_, _) => ValueTask.FromResult<ReportDefinition?>(BuildReport(3)),
            o =>
            {
                o.Formats = ViewerFormats.Pdf;
                o.IncludeErrorDetails = true;
                o.Language = "en";
                o.HeadHtml = "<meta name=\"x-test\" content=\"head\">";
            });
        _app.MapNetReporterViewer("/img", (_, _) => ValueTask.FromResult<ReportDefinition?>(ReportWithLogo));

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<JsonElement> Open(string url, string? user = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (user is not null) req.Headers.Add("X-User", user);
        using var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(text.StartsWith('{'), text[..Math.Min(400, text.Length)]);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public async Task ViewerPage_HostsComponentWithEncodedSrc()
    {
        var html = await _client.GetStringAsync("/reports/clientes?rows=3&x=%22%3E%3Cscript%3E");

        Assert.Contains("<netreporter-viewer", html);
        Assert.Contains("src=\"/reports/clientes/open?rows=3&amp;x=%22%3E%3Cscript%3E\"", html);
        Assert.Contains("/reports/_viewer/viewer.js?v=", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public async Task Open_ReturnsMetadataAndPagesAreSvg()
    {
        var doc = await Open("/reports/clientes/open?rows=120");

        var pageCount = doc.GetProperty("pageCount").GetInt32();
        Assert.True(pageCount > 1, $"expected several pages, got {pageCount}");
        Assert.Equal("Reporte de clientes", doc.GetProperty("title").GetString());
        Assert.Equal(612, doc.GetProperty("pageWidth").GetDouble());
        Assert.Equal(792, doc.GetProperty("pageHeight").GetDouble());

        var pageUrl = doc.GetProperty("pageUrl").GetString()!;
        using var page = await _client.GetAsync(pageUrl + pageCount);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("image/svg+xml", page.Content.Headers.ContentType!.MediaType);
        Assert.Contains("nosniff", page.Headers.GetValues("X-Content-Type-Options"));
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\"", await page.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(pageUrl + "0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(pageUrl + (pageCount + 1))).StatusCode);
    }

    [Fact]
    public async Task PagesAndExports_ReuseTheLayoutOfOpen()
    {
        var doc = await Open("/reports/clientes/open?rows=10");
        var calls = _resolveCalls;

        await _client.GetStringAsync(doc.GetProperty("pageUrl").GetString() + "1");
        var pdf = await _client.GetAsync(doc.GetProperty("exports").GetProperty("pdf").GetString());

        Assert.Equal(calls, _resolveCalls);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", pdf.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("Reporte de clientes.pdf", pdf.Content.Headers.ContentDisposition.FileNameStar);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Theory]
    [InlineData("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("html", "text/html")]
    public async Task Export_OtherFormats(string format, string mediaType)
    {
        var doc = await Open("/reports/clientes/open");
        using var res = await _client.GetAsync(doc.GetProperty("exports").GetProperty(format).GetString());

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(mediaType, res.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Export_InlineForPrinting()
    {
        var doc = await Open("/reports/clientes/open");
        using var res = await _client.GetAsync(doc.GetProperty("exports").GetProperty("pdf").GetString() + "?inline=1");

        Assert.Equal("inline", res.Content.Headers.ContentDisposition!.DispositionType);
    }

    [Fact]
    public async Task Formats_LimitExportsAndEndpoints()
    {
        var doc = await Open("/pdf-only/any/open");
        var exports = doc.GetProperty("exports");

        Assert.True(exports.TryGetProperty("pdf", out _));
        Assert.False(exports.TryGetProperty("xlsx", out _));
        var xlsxUrl = exports.GetProperty("pdf").GetString()!.Replace("/pdf", "/xlsx");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(xlsxUrl)).StatusCode);
    }

    [Fact]
    public async Task UnknownReport_Returns404Json()
    {
        using var res = await _client.GetAsync("/reports/nope/open");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Contains("not-found", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RenderError_HidesDetailsByDefault()
    {
        using var res = await _client.GetAsync("/reports/broken/open");

        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("render-failed", body);
        Assert.DoesNotContain("secret detail", body);
    }

    [Fact]
    public async Task UnknownToken_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/reports/_doc/bogus/pages/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/reports/_doc/bogus/export/pdf")).StatusCode);
    }

    [Fact]
    public async Task Document_IsOnlyVisibleToTheUserWhoOpenedIt()
    {
        var doc = await Open("/reports/clientes/open", user: "ana");
        var pageUrl = doc.GetProperty("pageUrl").GetString() + "1";

        async Task<HttpStatusCode> Get(string? user)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, pageUrl);
            if (user is not null) req.Headers.Add("X-User", user);
            using var res = await _client.SendAsync(req);
            return res.StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await Get("ana"));
        Assert.Equal(HttpStatusCode.NotFound, await Get("luis"));
        Assert.Equal(HttpStatusCode.NotFound, await Get(null));
    }

    [Fact]
    public async Task Script_IsServedWithETag()
    {
        using var res = await _client.GetAsync("/reports/_viewer/viewer.js");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("customElements.define('netreporter-viewer'", await res.Content.ReadAsStringAsync());
        Assert.Equal("no-cache", res.Headers.CacheControl!.ToString());   // plain URL: revalidate on every load
        using var versioned = await _client.GetAsync("/reports/_viewer/viewer.js?v=" + ViewerEndpointExtensions.ScriptVersion);
        Assert.Contains("immutable", versioned.Headers.CacheControl!.ToString());

        using var again = new HttpRequestMessage(HttpMethod.Get, "/reports/_viewer/viewer.js");
        again.Headers.TryAddWithoutValidation("If-None-Match", res.Headers.ETag!.Tag);
        using var cached = await _client.SendAsync(again);
        Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);
    }

    [Fact]
    public async Task Images_AreServedByUrl_AndPagesReferenceThemInsteadOfEmbedding()
    {
        var png = Logo;
        var doc = await Open("/img/x/open", user: "ana");
        using var pageReq = new HttpRequestMessage(HttpMethod.Get, doc.GetProperty("pageUrl").GetString() + "1");
        pageReq.Headers.Add("X-User", "ana");
        var svg = await (await _client.SendAsync(pageReq)).Content.ReadAsStringAsync();

        var imagesUrl = doc.GetProperty("pageUrl").GetString()!.Replace("/pages/", "/images/");
        Assert.Contains($"href=\"{imagesUrl}0\"", svg);
        Assert.DoesNotContain("data:image", svg);

        async Task<HttpResponseMessage> Get(string url, string? user)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (user is not null) req.Headers.Add("X-User", user);
            return await _client.SendAsync(req);
        }

        using var image = await Get(imagesUrl + "0", "ana");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(png, await image.Content.ReadAsByteArrayAsync());
        Assert.Contains("nosniff", image.Headers.GetValues("X-Content-Type-Options"));
        Assert.Equal(HttpStatusCode.NotFound, (await Get(imagesUrl + "1", "ana")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(imagesUrl + "0", "luis")).StatusCode);
    }

    [Theory]
    [InlineData(null, "es")]                        // nothing asked → first supported language
    [InlineData("en-US,en;q=0.9", "en")]
    [InlineData("es-HN,es;q=0.9,en;q=0.8", "es")]
    [InlineData("pt-BR,pt;q=0.9", "es")]            // unsupported → fallback
    [InlineData("fr;q=0.5,en;q=0.9", "en")]         // by quality, not order
    public async Task ViewerPage_NegotiatesTheLanguageFromAcceptLanguage(string? acceptLanguage, string expected)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/reports/clientes");
        if (acceptLanguage is not null) req.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        using var res = await _client.SendAsync(req);
        var html = await res.Content.ReadAsStringAsync();

        Assert.Contains($"<html lang=\"{expected}\">", html);
        Assert.Contains($"lang=\"{expected}\" set-title", html);
        Assert.Equal(expected, Assert.Single(res.Content.Headers.ContentLanguage));
        Assert.Contains("Accept-Language", res.Headers.Vary);
    }

    [Fact]
    public async Task ViewerPage_FixedLanguageAndHeadHtml()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/pdf-only/any");
        req.Headers.TryAddWithoutValidation("Accept-Language", "es");
        using var res = await _client.SendAsync(req);
        var html = await res.Content.ReadAsStringAsync();

        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("<meta name=\"x-test\" content=\"head\">", html);
        Assert.DoesNotContain("Accept-Language", res.Headers.Vary);
    }

    [Fact]
    public void NegotiateLanguage_HonoursCustomSupportedLanguages()
    {
        var options = new ReportViewerOptions();
        options.SupportedLanguages.Clear();
        options.SupportedLanguages.Add("en");
        options.SupportedLanguages.Add("pt-BR");

        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.Headers.AcceptLanguage = "pt-BR,pt;q=0.8";
        Assert.Equal("pt-br", ViewerEndpointExtensions.NegotiateLanguage(ctx, options));

        ctx.Request.Headers.AcceptLanguage = "pt-PT";
        Assert.Equal("en", ViewerEndpointExtensions.NegotiateLanguage(ctx, options));   // pt-PT ≠ pt-BR, no bare "pt"
    }

    [Fact]
    public void FileBaseName_StripsInvalidCharacters()
    {
        var report = BuildReport(1) with { FileName = "factura: 001/2026?" };
        Assert.Equal("factura_ 001_2026_", ViewerEndpointExtensions.FileBaseName(report));
    }

    /// <summary>Authenticates as the user named in the X-User header, if any.</summary>
    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers["X-User"].ToString();
            if (user.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user)], "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}

public sealed class ViewerDocumentStoreTests
{
    private static ViewerDocument Doc()
    {
        var yaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "report.yaml"));
        var report = YamlReportLoader.Parse(yaml).Bind("""{"titulo":"t","generadoEn":"g","clientes":[]}""");
        var layout = new NetReporter.Core.Layout.LayoutEngine(NetReporter.Svg.SkiaTextMeasurer.Instance).Layout(report);
        return new ViewerDocument("r", report, layout, owner: null);
    }

    [Fact]
    public void Add_OverTheLimit_EvictsTheLeastRecentlyUsedAndKeepsTheNewOne()
    {
        using var store = new ViewerDocumentStore(new ReportViewerOptions { MaxCachedDocuments = 2 });
        var a = store.Add(Doc());
        var b = store.Add(Doc());
        Assert.NotNull(store.Get(a, null));     // a is now more recent than b
        var c = store.Add(Doc());

        Assert.NotNull(store.Get(c, null));
        Assert.NotNull(store.Get(a, null));
        Assert.Null(store.Get(b, null));
    }

    [Fact]
    public void RenderSvgPage_StillWorksAfterTheDocumentIsDisposed()
    {
        var doc = Doc();
        var before = doc.RenderSvgPage(0, CancellationToken.None);
        doc.Dispose();
        Assert.Equal(before, doc.RenderSvgPage(0, CancellationToken.None));
    }

    [Fact]
    public void RenderSvgPage_SharedContextProducesTheSameSvgAsSvgRenderer()
    {
        using var doc = Doc();
        var expected = new NetReporter.Svg.SvgRenderer().Render(doc.RenderList);
        for (int i = 0; i < doc.PageCount; i++)
            Assert.Equal(expected[i], doc.RenderSvgPage(i, CancellationToken.None));
    }

    [Fact]
    public void RenderSvgPage_Cancelled_Throws()
    {
        using var doc = Doc();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => doc.RenderSvgPage(0, cts.Token));
    }

    [Fact]
    public void Images_AreIndexedOncePerDistinctArray_AndReferencedByUrlOnceItIsKnown()
    {
        var logo = TestImages.Png(8, 8);
        var other = TestImages.Png(4, 4);
        var template = Doc();
        var pages = new RenderPage[]
        {
            new(1, [new DrawImageCommand(new Rect(0, 0, 50, 50), logo, "image/png"), new DrawImageCommand(new Rect(0, 60, 50, 50), other, "image/png")]),
            new(2, [new DrawImageCommand(new Rect(0, 0, 50, 50), logo, "image/png")]),
        };
        using var doc = new ViewerDocument("r", template.Report, template.RenderList with { Pages = pages }, owner: null);

        Assert.Equal(2, doc.ImageCount);
        Assert.Same(logo, doc.Image(0)!.Value.Data);
        Assert.Same(other, doc.Image(1)!.Value.Data);
        Assert.Null(doc.Image(2));
        Assert.Null(doc.Image(-1));

        Assert.Contains("href=\"data:image/png;base64,", doc.RenderSvgPage(0, CancellationToken.None));

        doc.SetImagesUrl("/r/_doc/t/images");
        var page1 = doc.RenderSvgPage(0, CancellationToken.None);
        Assert.Contains("href=\"/r/_doc/t/images/0\"", page1);
        Assert.Contains("href=\"/r/_doc/t/images/1\"", page1);
        Assert.Contains("href=\"/r/_doc/t/images/0\"", doc.RenderSvgPage(1, CancellationToken.None));
        Assert.DoesNotContain("data:", page1);
    }
}

internal static class TestImages
{
    public static byte[] Png(int width, int height)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Opaque);
        bitmap.Erase(SkiaSharp.SKColors.CornflowerBlue);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
