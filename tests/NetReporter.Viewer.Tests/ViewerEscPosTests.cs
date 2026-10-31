using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using NetReporter.Core.Bands;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Expressions;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;
using NetReporter.EscPos;
using NetReporter.Viewer;

namespace NetReporter.Viewer.Tests;

/// <summary>ESC/POS en el visor: descarga del ticket y <c>POST …/print</c> con estado, errores y "sin impresora".</summary>
public sealed class ViewerEscPosTests : IAsyncLifetime
{
    private sealed class FakePrinter : IEscPosTransport, IEscPosStatusSource
    {
        public EscPosPrinterStatus Status = EscPosPrinterStatus.FromBytes(0x16, 0x12, 0x12);
        public Exception? Throw;
        public int StatusQueries;
        public readonly List<byte[]> Sent = new();

        public Task<EscPosPrinterStatus> QueryStatusAsync(CancellationToken cancellationToken = default)
        {
            StatusQueries++;
            return Task.FromResult(Status);
        }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (Throw is not null) throw Throw;
            Sent.Add(data.ToArray());
            return Task.CompletedTask;
        }
    }

    private static readonly ReportDefinition Ticket = new()
    {
        Name = "Ticket",
        Page = PageSetup.Roll80,
        Styles = new StyleSheetBuilder().Add("Default", s => s.FontFamily("Helvetica").FontSize(9)).Build(),
        Bands = new Band[]
        {
            new DetailBand { Height = 40, Elements = new ReportElement[]
            {
                new RectangleElement { Bounds = new Rect(0, 0, 100, 30), Fill = Color.Black },
            } },
        },
    };

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly FakePrinter _printer = new();
    private readonly ViewerEscPosOptions _escPos = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        _app = builder.Build();

        _escPos.Printer = (_, _, _) => ValueTask.FromResult<IEscPosTransport?>(_printer);
        _app.MapNetReporterViewer("/tickets", (_, _) => ValueTask.FromResult<ReportDefinition?>(Ticket), o =>
        {
            o.EscPos = _escPos;
            o.IncludeErrorDetails = true;
        });
        _app.MapNetReporterViewer("/download-only", (_, _) => ValueTask.FromResult<ReportDefinition?>(Ticket),
            o => o.EscPos = new ViewerEscPosOptions());
        _app.MapNetReporterViewer("/plain", (_, _) => ValueTask.FromResult<ReportDefinition?>(Ticket));

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<JsonElement> Open(string prefix)
    {
        using var res = await _client.GetAsync($"{prefix}/ticket/open");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() : null;

    [Fact]
    public async Task Open_AdvertisesDownloadAndPrinterOnlyWhenConfigured()
    {
        var full = await Open("/tickets");
        Assert.EndsWith("/export/escpos", Str(full.GetProperty("exports"), "escpos"));
        Assert.EndsWith("/print", Str(full, "printer"));

        var downloadOnly = await Open("/download-only");
        Assert.EndsWith("/export/escpos", Str(downloadOnly.GetProperty("exports"), "escpos"));
        Assert.Null(Str(downloadOnly, "printer"));

        var plain = await Open("/plain");
        Assert.Null(Str(plain.GetProperty("exports"), "escpos"));
        Assert.Null(Str(plain, "printer"));
    }

    [Fact]
    public async Task Export_Escpos_DownloadsTheTicketBytes()
    {
        var doc = await Open("/tickets");
        using var res = await _client.GetAsync(Str(doc.GetProperty("exports"), "escpos"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/octet-stream", res.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith(".escpos", res.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.Equal([0x1B, 0x40], bytes[..2]);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public async Task Export_Escpos_Is404_WhenNotEnabled()
    {
        var doc = await Open("/plain");
        var url = Str(doc.GetProperty("exports"), "pdf")!.Replace("/export/pdf", "/export/escpos");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Print_ChecksStatus_RendersAndSends()
    {
        _printer.Sent.Clear();
        _printer.Status = EscPosPrinterStatus.FromBytes(0x16, 0x12, 0x12);
        _printer.Throw = null;
        var queriesBefore = _printer.StatusQueries;
        var doc = await Open("/tickets");

        using var res = await _client.PostAsync(Str(doc, "printer"), content: null);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal(queriesBefore + 1, _printer.StatusQueries);
        var sent = Assert.Single(_printer.Sent);
        Assert.Equal([0x1B, 0x40], sent[..2]);
        // Lo mismo que la descarga: mismo RenderList, mismas opciones.
        using var download = await _client.GetAsync(Str(doc.GetProperty("exports"), "escpos"));
        Assert.Equal(await download.Content.ReadAsByteArrayAsync(), sent);
    }

    [Theory]
    [InlineData(0x16, 0x12, 0x72, "paper-end")]
    [InlineData(0x16, 0x16, 0x12, "cover-open")]
    [InlineData(0x1E, 0x12, 0x12, "offline")]
    public async Task Print_Is409WithTheCause_WhenThePrinterIsNotReady(byte printer, byte offline, byte paper, string code)
    {
        _printer.Sent.Clear();
        _printer.Status = EscPosPrinterStatus.FromBytes(printer, offline, paper);
        var doc = await Open("/tickets");

        using var res = await _client.PostAsync(Str(doc, "printer"), content: null);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(code, body.GetProperty("error").GetString());
        Assert.Empty(_printer.Sent);
    }

    [Fact]
    public async Task Print_SkipsTheStatusCheck_WhenDisabled()
    {
        _printer.Sent.Clear();
        _printer.Status = EscPosPrinterStatus.FromBytes(0x16, 0x12, 0x72);   // sin papel
        _escPos.CheckStatusBeforePrint = false;
        try
        {
            var doc = await Open("/tickets");
            using var res = await _client.PostAsync(Str(doc, "printer"), content: null);
            Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
            Assert.Single(_printer.Sent);
        }
        finally
        {
            _escPos.CheckStatusBeforePrint = true;
            _printer.Status = EscPosPrinterStatus.FromBytes(0x16, 0x12, 0x12);
        }
    }

    [Fact]
    public async Task Print_Is502_WhenThePrinterCannotBeReached()
    {
        _printer.Throw = new EscPosPrinterException("No se pudo conectar con la impresora 10.0.0.9:9100 (ConnectionRefused).");
        try
        {
            var doc = await Open("/tickets");
            using var res = await _client.PostAsync(Str(doc, "printer"), content: null);
            Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("unreachable", body.GetProperty("error").GetString());
            Assert.Contains("10.0.0.9:9100", body.GetProperty("detail").GetString());   // IncludeErrorDetails
        }
        finally { _printer.Throw = null; }
    }

    [Fact]
    public async Task Print_Is404NoPrinter_WhenNoPrinterIsConfigured()
    {
        var doc = await Open("/download-only");
        var url = Str(doc.GetProperty("exports"), "escpos")!.Replace("/export/escpos", "/print");

        using var res = await _client.PostAsync(url, content: null);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("no-printer", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Print_Is404_ForAnUnknownOrExpiredDocument()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/tickets/_doc/bogus/print", content: null)).StatusCode);
    }
}
