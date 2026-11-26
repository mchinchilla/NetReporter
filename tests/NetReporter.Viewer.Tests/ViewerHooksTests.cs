using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetReporter.Core.Definition;
using NetReporter.Templates;

namespace NetReporter.Viewer.Tests;

/// <summary>
/// Hooks for multi-tenant hosts: route parameters in the prefix, the document owner key, per-report formats, the
/// export hook with the resolver's state, and per-request language and head.
/// </summary>
public sealed class ViewerHooksTests : IAsyncLifetime
{
    private static readonly ReportDefinition Report = YamlReportLoader.Parse("""
        name: Hooks
        title: Reporte
        page: { size: Letter }
        styles:
          Default: { fontFamily: Helvetica, fontSize: 10 }
        bands:
          - kind: Detail
            height: 20
            elements:
              - type: text
                bounds: { x: 0, y: 0, width: 200, height: 12 }
                content: "hola"
        """).Bind("{}");

    private readonly ConcurrentQueue<ViewerExportContext> _exports = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private bool _failExport;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, IdAuthHandler>("Test", null);
        _app = builder.Build();
        _app.UsePathBase("/app");
        _app.UseRouting();
        _app.UseAuthentication();

        _app.MapNetReporterViewer("/c/{tenant}/viewer", (req, _) =>
        {
            if (req.ReportId != "r") return ValueTask.FromResult<ReportDefinition?>(null);
            if (req["formats"] == "pdf") req.Formats = ViewerFormats.Pdf;
            req.State = $"{req.HttpContext.GetRouteValue("tenant")}:{req.ReportId}";
            return ValueTask.FromResult<ReportDefinition?>(Report);
        }, o =>
        {
            o.Formats = ViewerFormats.Pdf | ViewerFormats.Xlsx;
            o.DocumentOwner = ctx => ctx.User.FindFirstValue("uid");
            o.LanguageResolver = ctx => ctx.Request.Headers["X-Lang"].ToString();
            o.HeadHtmlResolver = ctx => ctx.GetRouteValue("tenant") is "acme" ? "<meta name=\"tenant\" content=\"acme\">" : null;
            o.OnExport = (export, _) =>
            {
                if (_failExport) throw new InvalidOperationException("audit down");
                _exports.Enqueue(export);
                return ValueTask.CompletedTask;
            };
        });

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<HttpResponseMessage> Get(string url, string uid = "1", string name = "Ana", string? lang = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Uid", uid);
        req.Headers.Add("X-Name", name);
        if (lang is not null) req.Headers.Add("X-Lang", lang);
        return await _client.SendAsync(req);
    }

    private async Task<JsonElement> Open(string url, string uid = "1", string name = "Ana")
    {
        using var res = await Get(url, uid, name);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task RouteParametersInPrefix_AreExpandedInEveryUrl()
    {
        var html = await (await Get("/app/c/acme/viewer/r")).Content.ReadAsStringAsync();
        Assert.Contains("src=\"/app/c/acme/viewer/r/open\"", html);
        Assert.Contains("/app/c/acme/viewer/_viewer/viewer.js?v=", html);
        Assert.DoesNotContain("{tenant}", html);

        var doc = await Open("/app/c/acme/viewer/r/open");
        Assert.StartsWith("/app/c/acme/viewer/_doc/", doc.GetProperty("pageUrl").GetString());
        Assert.StartsWith("/app/c/acme/viewer/_doc/", doc.GetProperty("exports").GetProperty("pdf").GetString());
        using var page = await Get(doc.GetProperty("pageUrl").GetString() + "1");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
    }

    [Fact]
    public async Task Document_IsNotServedUnderAnotherTenantOrToAnotherOwner()
    {
        var doc = await Open("/app/c/acme/viewer/r/open");
        var page = doc.GetProperty("pageUrl").GetString() + "1";

        // Same token under another tenant's URL.
        using var otherTenant = await Get(page.Replace("/c/acme/", "/c/globex/", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, otherTenant.StatusCode);

        // Same display name, different user id: the owner key is the id.
        using var sameName = await Get(page, uid: "2", name: "Ana");
        Assert.Equal(HttpStatusCode.NotFound, sameName.StatusCode);

        // Same id with another display name is still the owner.
        using var renamed = await Get(page, uid: "1", name: "Ana María");
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
    }

    [Fact]
    public async Task ResolverFormats_NarrowTheMenuAndTheEndpoint()
    {
        var doc = await Open("/app/c/acme/viewer/r/open?formats=pdf");
        var exports = doc.GetProperty("exports");
        Assert.True(exports.TryGetProperty("pdf", out var pdf));
        Assert.False(exports.TryGetProperty("xlsx", out _));

        using var xlsx = await Get(pdf.GetString()!.Replace("/export/pdf", "/export/xlsx", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, xlsx.StatusCode);

        // Without the narrowing the options' formats apply (and never more than them).
        var all = (await Open("/app/c/acme/viewer/r/open")).GetProperty("exports");
        Assert.True(all.TryGetProperty("xlsx", out _));
        Assert.False(all.TryGetProperty("html", out _));
    }

    [Fact]
    public async Task OnExport_ReceivesFormatSizeInlineAndState_AndAFailureSendsNothing()
    {
        var pdf = (await Open("/app/c/acme/viewer/r/open")).GetProperty("exports").GetProperty("pdf").GetString()!;

        using var download = await Get(pdf);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        using var print = await Get(pdf + "?inline=1");
        Assert.Equal(HttpStatusCode.OK, print.StatusCode);

        var seen = _exports.ToArray();
        Assert.Equal(2, seen.Length);
        Assert.All(seen, e =>
        {
            Assert.Equal("pdf", e.Format);
            Assert.Equal("r", e.ReportId);
            Assert.Equal("acme:r", e.State);
            Assert.True(e.Bytes > 0);
        });
        Assert.False(seen[0].Inline);
        Assert.True(seen[1].Inline);
        Assert.Equal(download.Content.Headers.ContentLength, seen[0].Bytes);

        _failExport = true;
        using var failed = await Get(pdf);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Empty(await failed.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task LanguageAndHead_ComeFromTheRequest()
    {
        using var res = await Get("/app/c/acme/viewer/r", lang: "en");
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("<meta name=\"tenant\" content=\"acme\">", html);
        Assert.DoesNotContain("Accept-Language", res.Headers.Vary);

        // An empty answer falls back to Accept-Language negotiation.
        using var req = new HttpRequestMessage(HttpMethod.Get, "/app/c/acme/viewer/r");
        req.Headers.Add("X-Uid", "1");
        req.Headers.AcceptLanguage.ParseAdd("es-HN");
        var negotiated = await (await _client.SendAsync(req)).Content.ReadAsStringAsync();
        Assert.Contains("<html lang=\"es\">", negotiated);
    }

    private sealed class IdAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var uid = Request.Headers["X-Uid"].ToString();
            if (uid.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim("uid", uid), new Claim(ClaimTypes.Name, Request.Headers["X-Name"].ToString())], "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
