using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;
using SkiaSharp;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Svg.Tests;

public sealed class SkiaPaintCacheTests
{
    private static readonly SKTypeface Lato =
        SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "Fonts", "Lato-Regular.ttf"))
        ?? throw new InvalidOperationException("No se pudo cargar Lato");

    [Fact]
    public void Font_SameTypefaceAndSize_ReturnsTheSameInstance()
    {
        using var cache = new SkiaPaintCache();

        var font = cache.Font(Lato, 10);

        Assert.Same(font, cache.Font(Lato, 10));
        Assert.NotSame(font, cache.Font(Lato, 12));
        Assert.Equal(10, font.Size);
        Assert.True(font.LinearMetrics);   // sale de SkiaFonts.CreateFont, no de un SKFont por defecto
    }

    [Fact]
    public void Metrics_MatchTheCachedFont()
    {
        using var cache = new SkiaPaintCache();

        var metrics = cache.Metrics(Lato, 10);

        Assert.Equal(cache.Font(Lato, 10).Metrics, metrics);
        Assert.Equal(metrics, cache.Metrics(Lato, 10));
        Assert.NotEqual(metrics.Ascent, cache.Metrics(Lato, 20).Ascent);
    }

    [Fact]
    public void Image_SameArray_IsDecodedOnce_AndInvalidDataIsNull()
    {
        using var cache = new SkiaPaintCache();
        var png = Png();

        var image = cache.Image(png);

        Assert.NotNull(image);
        Assert.Same(image, cache.Image(png));
        Assert.Null(cache.Image([1, 2, 3]));
        Assert.Null(cache.Image([]));
    }

    [Fact]
    public void FillAndStroke_ReuseOnePaintEach_WithTheRequestedState()
    {
        using var cache = new SkiaPaintCache();

        var fill = cache.Fill(SKColors.Red);
        Assert.Same(fill, cache.Fill(SKColors.Blue));
        Assert.Equal(SKColors.Blue, fill.Color);
        Assert.Equal(SKPaintStyle.Fill, fill.Style);

        var stroke = cache.Stroke(SKColors.Black, 2, SKStrokeJoin.Round, SKStrokeCap.Round);
        Assert.NotSame(fill, stroke);
        Assert.Equal(SKPaintStyle.Stroke, stroke.Style);
        Assert.Equal(2, stroke.StrokeWidth);
        Assert.Equal(SKStrokeJoin.Round, stroke.StrokeJoin);

        // Sin unión/remate explícitos vuelve a los valores por defecto de Skia, no a los de la llamada anterior.
        Assert.Same(stroke, cache.Stroke(SKColors.Black, 1));
        Assert.Equal(SKStrokeJoin.Miter, stroke.StrokeJoin);
        Assert.Equal(SKStrokeCap.Butt, stroke.StrokeCap);
    }

    [Fact]
    public void SvgRenderer_SharingTheCacheAcrossPages_ProducesTheSamePagesAsRenderingThemAlone()
    {
        var style = new ResolvedStyle(
            FontFamily: "Helvetica", FontSize: 10, Weight: FontWeight.Normal, Italic: FontStyleKind.Normal,
            Foreground: Color.Black, Background: Color.Transparent,
            TextAlign: Core.Styles.TextAlignment.Left, VerticalAlign: VerticalAlignment.Top,
            Padding: Thickness.Zero, Border: null, NumberFormat: null);
        var logo = Png();
        var list = new RlRenderList(PageSetup.Letter, new RenderPage[]
        {
            new(1, [new DrawImageCommand(new Rect(36, 36, 120, 60), logo, "image/png"),
                    new DrawTextCommand(new Rect(36, 120, 400, 14), "Página 1 ▸ Región Norte", style),
                    new DrawLineCommand(new Point(36, 140), new Point(400, 140), 0.5, Color.FromHex("#CBD5E1"))]),
            new(2, [new DrawImageCommand(new Rect(36, 36, 120, 60), logo, "image/png"),
                    new DrawTextCommand(new Rect(36, 120, 400, 14), "Página 2", style with { FontSize = 12 }),
                    new DrawRectangleCommand(new Rect(36, 140, 100, 40), Color.FromHex("#F1F5F9"), new BorderLine(1, Color.Black), 4)])
        });
        var renderer = new SvgRenderer();

        var shared = renderer.Render(list);

        Assert.Equal(2, shared.Count);
        for (var i = 0; i < shared.Count; i++)
            Assert.Equal(WithoutClipIds(renderer.RenderPage(list.Pages[i], 612, 792)), WithoutClipIds(shared[i]));
    }

    // Skia numera los <clipPath> con un contador global del proceso (cl_3, cl_5…), así que dos
    // renders idénticos solo coinciden una vez quitados esos ids.
    private static string WithoutClipIds(string svg) => System.Text.RegularExpressions.Regex.Replace(svg, @"cl_\d+", "cl_#");

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(16, 16, SKColorType.Rgba8888, SKAlphaType.Opaque);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
