using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using NetReporter.Core.Bands;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Layout;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;
using NetReporter.EscPos;
using NetReporter.Viewer;

namespace NetReporter.Viewer.Tests;

/// <summary>Impresora configurada por el navegador (<see cref="ViewerEscPosOptions.AllowClientSettings"/>): filtro de direcciones, ajustes y estado.</summary>
public sealed class ViewerClientPrinterTests : IAsyncLifetime
{
    private sealed class FakePrinter(EscPosTcpPrinterOptions options) : IEscPosTransport, IEscPosStatusSource
    {
        public EscPosTcpPrinterOptions Options { get; } = options;
        public static EscPosPrinterStatus NextStatus = EscPosPrinterStatus.FromBytes(0x16, 0x12, 0x12);
        public static readonly List<FakePrinter> Created = new();
        public readonly List<byte[]> Sent = new();

        public Task<EscPosPrinterStatus> QueryStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(NextStatus);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
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
    private readonly ViewerEscPosOptions _clientOnly = new()
    {
        AllowClientSettings = true,
        TransportFactory = o => { var p = new FakePrinter(o); FakePrinter.Created.Add(p); return p; },
    };

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        _app = builder.Build();

        _app.MapNetReporterViewer("/client", (_, _) => ValueTask.FromResult<ReportDefinition?>(Ticket), o => o.EscPos = _clientOnly);
        _app.MapNetReporterViewer("/server-only", (_, _) => ValueTask.FromResult<ReportDefinition?>(Ticket), o =>
        {
            // Sin AllowClientSettings: lo que mande el navegador se ignora y se usa la impresora del servidor.
            o.EscPos = new ViewerEscPosOptions
            {
                Printer = (_, _, _) => ValueTask.FromResult<IEscPosTransport?>(new FakePrinter(new EscPosTcpPrinterOptions { Host = "server" })),
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

    private async Task<JsonElement> Open(string prefix)
    {
        using var res = await _client.GetAsync($"{prefix}/ticket/open");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() : null;

    private static object Settings(string host, int port = 9100, string paper = "auto") =>
        new { host, port, paper, cut = "partial", feed = 3, drawer = false, drawerPin = 0, dithering = "threshold", threshold = 160 };

    [Fact]
    public async Task Open_AdvertisesClientSettings()
    {
        var doc = await Open("/client");
        Assert.EndsWith("/print", Str(doc, "printer"));
        Assert.EndsWith("/printer-status", Str(doc, "printerStatus"));
        Assert.True(doc.GetProperty("printerSettings").GetBoolean());
        Assert.False(doc.GetProperty("serverPrinter").GetBoolean());

        var serverOnly = await Open("/server-only");
        Assert.True(serverOnly.GetProperty("serverPrinter").GetBoolean());
        Assert.False(serverOnly.TryGetProperty("printerSettings", out _));
    }

    [Fact]
    public async Task Print_UsesTheBrowserPrinter_AndItsPaperSize()
    {
        FakePrinter.Created.Clear();
        var doc = await Open("/client");

        using var res = await _client.PostAsJsonAsync(Str(doc, "printer"), Settings("192.168.1.20", 9100, paper: "58"));

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var printer = Assert.Single(FakePrinter.Created);
        Assert.Equal("192.168.1.20", printer.Options.Host);
        Assert.Equal(9100, printer.Options.Port);
        Assert.Equal(TimeSpan.FromSeconds(3), printer.Options.ConnectTimeout);
        var sent = Assert.Single(printer.Sent);
        var layout = new LayoutEngine().Layout(Ticket);
        Assert.Equal(new EscPosRenderer(EscPosRendererOptions.ForRoll58()).Render(layout), sent);
    }

    [Theory]
    [InlineData("8.8.8.8", 9100)]
    [InlineData("printer.local", 9100)]
    [InlineData("192.168.1.20", 0)]
    public async Task Print_RejectsPrintersOutsideThePolicy(string host, int port)
    {
        FakePrinter.Created.Clear();
        var doc = await Open("/client");

        using var res = await _client.PostAsJsonAsync(Str(doc, "printer"), Settings(host, port));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("printer-not-allowed", JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
        Assert.Empty(FakePrinter.Created);
    }

    [Fact]
    public async Task Print_WithoutABrowserPrinter_Is404_WhenTheServerHasNone()
    {
        var doc = await Open("/client");
        using var res = await _client.PostAsync(Str(doc, "printer"), content: null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("no-printer", JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Print_IgnoresBrowserSettings_WhenNotAllowed()
    {
        FakePrinter.Created.Clear();
        var doc = await Open("/server-only");

        using var res = await _client.PostAsJsonAsync(Str(doc, "printer"), Settings("8.8.8.8"));

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);   // la impresora del servidor, no la del cuerpo
        Assert.Empty(FakePrinter.Created);
    }

    [Fact]
    public async Task PrinterStatus_ReportsTheBrowserPrinterState()
    {
        var doc = await Open("/client");
        FakePrinter.NextStatus = EscPosPrinterStatus.FromBytes(0x16, 0x12, 0x72);   // sin papel
        try
        {
            using var res = await _client.PostAsJsonAsync(Str(doc, "printerStatus"), Settings("10.0.0.5"));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("paper-end", body.GetProperty("code").GetString());
            Assert.False(body.GetProperty("ready").GetBoolean());
            Assert.True(body.GetProperty("paperEnd").GetBoolean());
        }
        finally { FakePrinter.NextStatus = EscPosPrinterStatus.FromBytes(0x16, 0x12, 0x12); }
    }

    [Fact]
    public async Task PrinterStatus_Is404_WhenClientSettingsAreNotAllowed_And400WithoutHost()
    {
        var serverOnly = await Open("/server-only");
        var url = Str(serverOnly, "printer")!.Replace("/print", "/printer-status");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync(url, Settings("10.0.0.5"))).StatusCode);

        var doc = await Open("/client");
        using var res = await _client.PostAsJsonAsync(Str(doc, "printerStatus"), new { port = 9100 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("invalid-printer", JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.9", true)]
    [InlineData("172.32.0.9", false)]
    [InlineData("192.168.99.81", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("2001:db8::1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::ffff:192.168.1.1", true)]
    [InlineData("printer", false)]
    public void DefaultPolicy_AllowsOnlyPrivateIpLiterals(string host, bool allowed)
    {
        Assert.Equal(allowed, ViewerEscPosOptions.DefaultIsPrinterAllowed(host, 9100));
        Assert.False(ViewerEscPosOptions.DefaultIsPrinterAllowed(host, 70000));
    }
}
