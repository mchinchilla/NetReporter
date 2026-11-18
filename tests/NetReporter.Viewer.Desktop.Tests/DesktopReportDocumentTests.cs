using System.Text;
using NetReporter.Core.Bands;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Expressions;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;
using NetReporter.Svg;
using SkiaSharp;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Viewer.Desktop.Tests;

// El registro de fuentes es global al proceso: Lato se registra una vez para toda la colección.
[CollectionDefinition(nameof(DesktopCollection), DisableParallelization = true)]
public sealed class DesktopCollection : ICollectionFixture<LatoFixture>;

public sealed class LatoFixture
{
    public LatoFixture() => SkiaFonts.RegisterFont(Path.Combine(AppContext.BaseDirectory, "Fonts", "Lato-Regular.ttf"));
}

[Collection(nameof(DesktopCollection))]
public sealed class DesktopReportDocumentTests
{
    private static StyleSheet Styles() => new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Lato").FontSize(10))
        .Build();

    private static ResolvedStyle TextStyle(double size) =>
        new("Lato", size, FontWeight.Normal, FontStyleKind.Normal, Color.Black, Color.Transparent,
            TextAlignment.Left, VerticalAlignment.Top, Thickness.Zero, null, null);

    private static ReportDefinition Report(PageSetup page, string? title = null, string? fileName = null, params Band[] bands) => new()
    {
        Name = "Test report",
        Title = title,
        FileName = fileName,
        Page = page,
        Styles = Styles(),
        Bands = bands,
    };

    private static DesktopReportDocument Document(params RenderCommand[] commands) =>
        new(Report(PageSetup.Letter), new RlRenderList(PageSetup.Letter, [new RenderPage(1, commands)]));

    private static ReportHeaderBand Header(string text) => new()
    {
        Height = 30,
        Elements = [new TextElement { Bounds = new Rect(0, 0, 300, 20), Content = Expr.Str(text) }],
    };

    private static int Distance(SKColor a, SKColor b) =>
        Math.Max(Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)), Math.Abs(a.Blue - b.Blue));

    private static bool IsDark(SKBitmap bitmap, int x, int y) => bitmap.GetPixel(x, y).Red < 64;

    [Fact]
    public void RenderTile_PaintsAtTheRequestedScale()
    {
        using var doc = Document(new DrawRectangleCommand(new Rect(72, 72, 72, 72), Color.Black, null));
        using var full = doc.RenderTile(0, 2, SKRectI.Create(0, 0, 1224, 1584));

        Assert.Equal(SKColorType.Bgra8888, full.ColorType);
        Assert.True(IsDark(full, 150, 150));
        Assert.True(IsDark(full, 285, 285));
        Assert.False(IsDark(full, 140, 140));       // 72 pt × 2 = 144 px
        Assert.False(IsDark(full, 292, 150));
        Assert.Equal(SKColors.White, full.GetPixel(10, 10));
    }

    [Fact]
    public void RenderTile_UsesPagePixelsForTheOrigin()
    {
        using var doc = Document(new DrawRectangleCommand(new Rect(72, 72, 72, 72), Color.Black, null));
        using var tile = doc.RenderTile(0, 2, new SKRectI(140, 140, 200, 200));

        Assert.Equal(60, tile.Width);
        Assert.False(IsDark(tile, 1, 1));           // px 141 de la página
        Assert.True(IsDark(tile, 10, 10));          // px 150
    }

    [Fact]
    public void Tiles_MatchACropOfTheWholePage()
    {
        // Elementos que cruzan bordes de tile: líneas gruesas, bordes, texto, path.
        var commands = new List<RenderCommand>
        {
            new DrawLineCommand(new Point(0, 130), new Point(612, 130), 18, Color.Black),
            new DrawLineCommand(new Point(250, 0), new Point(250, 792), 0.5, new Color(200, 0, 0)),
            new DrawRectangleCommand(new Rect(100, 300, 160, 100), new Color(0, 120, 200), new BorderLine(12, Color.Black), 6),
            new DrawTextCommand(new Rect(60, 240, 400, 30), "Factura 001-002 · Total L 1,234.56", TextStyle(18)),
            // Fuente grande en una caja baja: el painter recorta el texto a la caja ± 0,35 em (≈ 25 pt), más que el
            // margen fijo del culling. Las mayúsculas caen por debajo de la caja y entran en la fila de tiles
            // siguiente (bordes cada 128 px / 1,5 = 85,33 pt: 170,67 y 682,67), que la caja + 8 pt no alcanza.
            new DrawTextCommand(new Rect(20, 145, 560, 10), "HÉÑMW", TextStyle(72)),
            new DrawTextCommand(new Rect(20, 657, 560, 10), "HÉÑMW", TextStyle(72)),
            DrawPathCommand.Create([new Point(300, 500), new Point(560, 520), new Point(420, 760)], true, new Color(20, 160, 60), null),
        };
        using var doc = Document([.. commands]);
        const double scale = 1.5;
        var (width, height) = PageStack.PixelSize(PageSetup.Letter.Size, scale);
        using var full = doc.RenderTile(0, scale, SKRectI.Create(0, 0, width, height));

        var tiles = Tiles.Covering(0, scale, width, height, SKRectI.Create(0, 0, width, height), tileSize: 128).ToList();
        Assert.True(tiles.Count > 40);
        var culledCommands = 0;
        foreach (var t in tiles)
        {
            using var tile = doc.RenderTile(0, scale, t.Bounds);
            using var unculled = doc.RenderTile(0, scale, t.Bounds, cull: false);
            // Culling must not change a single pixel of the tile.
            Assert.True(tile.Bytes.AsSpan().SequenceEqual(unculled.Bytes), $"Culling changed tile {t.Key}");

            // And a tile is the crop of the whole page, up to Skia's anti-aliasing of edges, which shifts by a
            // few levels with the translation (a missing command would be hundreds of levels off).
            for (var y = 0; y < tile.Height; y++)
            for (var x = 0; x < tile.Width; x++)
            {
                var expected = full.GetPixel(t.Bounds.Left + x, t.Bounds.Top + y);
                var actual = tile.GetPixel(x, y);
                if (Distance(expected, actual) > 64)
                    Assert.Fail($"Tile {t.Key} differs at page px ({t.Bounds.Left + x}, {t.Bounds.Top + y}): {actual} ≠ {expected}");
            }
            culledCommands += commands.Count(c => !Reaches(c, t.Bounds, scale));
        }
        Assert.True(culledCommands > 0, "the test should exercise culling");
    }

    private static bool Reaches(RenderCommand c, SKRectI tile, double scale) =>
        c.Bounds.X * scale <= tile.Right + 40 && c.Bounds.Right * scale >= tile.Left - 40 &&
        c.Bounds.Y * scale <= tile.Bottom + 40 && c.Bounds.Bottom * scale >= tile.Top - 40;

    [Fact]
    public async Task CreateAsync_LaysTheReportOut()
    {
        using var doc = await DesktopReportDocument.CreateAsync(Report(PageSetup.Letter, bands: Header("Hola")));

        Assert.Equal(1, doc.PageCount);
        Assert.Equal(PageSetup.Letter.Size, doc.PageSize);
        Assert.Contains(doc.RenderList.Pages[0].Commands, c => c is DrawTextCommand { Text: "Hola" });
        Assert.Equal("Test report", doc.Title);
    }

    [Fact]
    public async Task ContinuousRoll_TakesThePageSizeFromTheLayout()
    {
        using var doc = await DesktopReportDocument.CreateAsync(Report(PageSetup.Roll80, bands: Header("Ticket")));

        Assert.Equal(PageSetup.Roll80.Size.Width, doc.PageSize.Width);
        Assert.True(doc.PageSize.Height > 30, $"Roll height {doc.PageSize.Height}");
        Assert.Equal(doc.RenderList.Page.Size, doc.PageSize);
    }

    [Fact]
    public void Export_ProducesEachFormat()
    {
        using var doc = DesktopReportDocument.Create(Report(PageSetup.Letter, title: "Mi factura", bands: Header("Hola")));

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(doc.Export(ExportFormat.Pdf), 0, 4));
        Assert.Contains("<title>Mi factura</title>", Encoding.UTF8.GetString(doc.Export(ExportFormat.Html)));
        Assert.Equal("PK"u8.ToArray(), doc.Export(ExportFormat.Xlsx)[..2]);

        using var stream = new MemoryStream();
        doc.Export(ExportFormat.Pdf, stream);
        Assert.True(stream.Length > 100);
    }

    [Fact]
    public async Task ExportAsync_WritesToTheStream()
    {
        using var doc = DesktopReportDocument.Create(Report(PageSetup.Letter, bands: Header("Hola")));
        using var stream = new MemoryStream();
        await doc.ExportAsync(ExportFormat.Html, stream);
        Assert.Contains("Hola", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Theory]
    [InlineData("Factura: 001/002", "Título", "Factura_ 001_002.pdf")]
    [InlineData(null, "Estado de cuenta", "Estado de cuenta.pdf")]
    [InlineData(null, null, "Test report.pdf")]
    [InlineData(" ... ", null, "report.pdf")]
    public void SuggestedFileName_FollowsTheWebViewerRule(string? fileName, string? title, string expected)
    {
        using var doc = new DesktopReportDocument(Report(PageSetup.Letter, title, fileName),
            new RlRenderList(PageSetup.Letter, []));
        Assert.Equal(expected, doc.SuggestedFileName(ExportFormat.Pdf));
        Assert.EndsWith(".xlsx", doc.SuggestedFileName(ExportFormat.Xlsx));
    }

    [Fact]
    public void Paint_ValidatesThePageAndTheLifetime()
    {
        var doc = Document(new DrawRectangleCommand(new Rect(0, 0, 10, 10), Color.Black, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.RenderTile(1, 1, SKRectI.Create(0, 0, 10, 10)));
        Assert.Throws<ArgumentException>(() => doc.RenderTile(0, 1, SKRectI.Create(0, 0, 0, 10)));

        doc.Dispose();
        doc.Dispose();
        Assert.Throws<ObjectDisposedException>(() => doc.RenderTile(0, 1, SKRectI.Create(0, 0, 10, 10)));
        Assert.Throws<ObjectDisposedException>(() => doc.Export(ExportFormat.Pdf));
    }
}
