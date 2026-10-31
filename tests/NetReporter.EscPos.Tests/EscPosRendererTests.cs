using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;
using NetReporter.Svg;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.EscPos.Tests;

// El registro de fuentes es global al proceso: Lato se registra una vez para toda la colección.
[CollectionDefinition(nameof(EscPosCollection), DisableParallelization = true)]
public sealed class EscPosCollection : ICollectionFixture<LatoFixture>;

public sealed class LatoFixture
{
    public LatoFixture() => SkiaFonts.RegisterFont(Path.Combine(AppContext.BaseDirectory, "Fonts", "Lato-Regular.ttf"));
}

[Collection(nameof(EscPosCollection))]
public sealed class EscPosRendererTests
{
    private const double DotsPerPoint = 203.2 / 72.0;

    /// <summary>Rollo de 80 mm con una altura dada (como la dejaría el layout continuo).</summary>
    private static PageSetup Roll80(double height) => PageSetup.Roll80 with { Size = new Size(PageSetup.Roll80.Size.Width, height) };

    private static RlRenderList List(PageSetup page, params RenderCommand[] commands) =>
        new(page, [new RenderPage(1, commands)]);

    private static ResolvedStyle Style(string family, double size) =>
        new(family, size, FontWeight.Normal, FontStyleKind.Normal, Color.Black, Color.Transparent,
            TextAlignment.Left, VerticalAlignment.Top, Thickness.Zero, null, null);

    [Fact]
    public void Roll80_RectangleLandsOnTheExpectedDots()
    {
        // Rectángulo negro de 36×36 pt en el origen del contenido (tras el margen de 4 mm), a 10 pt del borde superior.
        var page = Roll80(100);
        var left = page.Margins.Left;
        var list = List(page, new DrawRectangleCommand(new Rect(left, 10, 36, 36), Color.Black, null));

        var decoded = EscPosDecoder.Decode(new EscPosRenderer().Render(list));

        Assert.True(decoded.Initialized);
        Assert.Equal(576, decoded.Width);
        Assert.Equal((int)Math.Ceiling(100 * DotsPerPoint - 1e-6), decoded.Height);
        // El margen de 4 mm (32 puntos) se recorta: el contenido empieza en el punto 0.
        var rectRight = (int)(36 * DotsPerPoint);           // ≈ 101
        var rectTop = (int)Math.Ceiling(10 * DotsPerPoint);  // ≈ 29
        var rectBottom = (int)(46 * DotsPerPoint);           // ≈ 129
        Assert.True(decoded.IsBlack(1, rectTop + 1));
        Assert.True(decoded.IsBlack(rectRight - 2, rectBottom - 2));
        Assert.False(decoded.IsBlack(rectRight + 4, rectTop + 10));
        Assert.False(decoded.IsBlack(300, 80));
        Assert.False(decoded.IsBlack(1, rectTop - 3));
        Assert.Equal(0, decoded.CountBlack(rectRight + 8, 0, 576, decoded.Height));
    }

    [Fact]
    public void Roll58_Uses384Dots()
    {
        var page = PageSetup.Roll58 with { Size = new Size(PageSetup.Roll58.Size.Width, 50) };
        var list = List(page, new DrawRectangleCommand(new Rect(page.Margins.Left, 0, page.ContentWidth, 50), Color.Black, null));

        var decoded = EscPosDecoder.Decode(new EscPosRenderer(EscPosRendererOptions.ForRoll58()).Render(list));

        Assert.Equal(384, decoded.Width);
        // El contenido (48 mm = 384 puntos) cubre exactamente el ancho imprimible.
        Assert.True(decoded.IsBlack(0, 10));
        Assert.True(decoded.IsBlack(383, 10));
    }

    [Fact]
    public void FixedPageWiderThanPrinter_IsScaledToTheWidth()
    {
        // Letter: franja negra de borde a borde en los primeros 72 pt → toda la primera fila negra, página escalada.
        var list = List(PageSetup.Letter, new DrawRectangleCommand(new Rect(0, 0, 612, 72), Color.Black, null));

        var decoded = EscPosDecoder.Decode(new EscPosRenderer().Render(list));

        Assert.Equal((int)Math.Ceiling(792 * 576.0 / 612 - 1e-6), decoded.Height);
        Assert.Equal(576, decoded.CountBlack(0, 5, 576, 6));
        Assert.Equal(0, decoded.CountBlack(0, decoded.Height - 10, 576, decoded.Height));
    }

    [Fact]
    public void Crop_KeepsOneToOne_AndCentersThePage()
    {
        // Página de 100 pt de ancho (282 puntos) en 576: queda centrada, con (576-282)/2 = 147 puntos blancos a cada lado.
        var page = PageSetup.Custom(100, 20, new Thickness(0));
        var list = List(page, new DrawRectangleCommand(new Rect(0, 0, 100, 20), Color.Black, null));

        var decoded = EscPosDecoder.Decode(new EscPosRenderer(new EscPosRendererOptions { PageFit = EscPosPageFit.Crop }).Render(list));

        var pageDots = (int)Math.Round(100 * DotsPerPoint);
        var offset = (576 - pageDots) / 2;
        Assert.False(decoded.IsBlack(offset - 3, 10));
        Assert.True(decoded.IsBlack(offset + 2, 10));
        Assert.True(decoded.IsBlack(offset + pageDots - 3, 10));
        Assert.False(decoded.IsBlack(offset + pageDots + 3, 10));
    }

    [Fact]
    public void Commands_InitFeedCutAndDrawer()
    {
        var list = List(Roll80(10));

        var partial = EscPosDecoder.Decode(new EscPosRenderer().Render(list));
        Assert.True(partial.Initialized);
        Assert.Equal([3], partial.FeedLines);
        Assert.Equal([(byte)1], partial.Cuts);
        Assert.Empty(partial.DrawerPins);

        var full = EscPosDecoder.Decode(new EscPosRenderer(new EscPosRendererOptions
        {
            Cut = EscPosCut.Full, FeedLinesBeforeCut = 5, OpenCashDrawer = true, CashDrawerPin = 1, Initialize = false,
        }).Render(list));
        Assert.False(full.Initialized);
        Assert.Equal([5], full.FeedLines);
        Assert.Equal([(byte)0], full.Cuts);
        Assert.Equal([(byte)1], full.DrawerPins);

        var none = EscPosDecoder.Decode(new EscPosRenderer(new EscPosRendererOptions { Cut = EscPosCut.None, FeedLinesBeforeCut = 0 }).Render(list));
        Assert.Empty(none.Cuts);
        Assert.Empty(none.FeedLines);
    }

    [Fact]
    public void PrologueAndEpilogue_AreWrittenVerbatim()
    {
        var options = new EscPosRendererOptions { Prologue = [0x1B, 0x74, 0x10], Epilogue = [0x0A, 0x0A] };
        var bytes = new EscPosRenderer(options).Render(List(Roll80(5)));

        Assert.Equal([0x1B, 0x40, 0x1B, 0x74, 0x10], bytes[..5]);
        Assert.Equal([0x0A, 0x0A], bytes[^2..]);
    }

    [Fact]
    public void Bands_SplitTheRasterInBlocksOfBandRows()
    {
        var height = (int)Math.Ceiling(100 * DotsPerPoint - 1e-6);   // 283
        var decoded = EscPosDecoder.Decode(new EscPosRenderer(new EscPosRendererOptions { BandRows = 100 }).Render(List(Roll80(100))));

        Assert.Equal([100, 100, height - 200], decoded.BlockRows);
        Assert.Equal(height, decoded.Height);
    }

    [Fact]
    public void MultiplePages_CutAfterEachOrOnlyAtTheEnd()
    {
        var page = Roll80(20);
        var list = new RlRenderList(page, [new RenderPage(1, []), new RenderPage(2, []), new RenderPage(3, [])]);

        Assert.Equal(3, EscPosDecoder.Decode(new EscPosRenderer().Render(list)).Cuts.Count);
        Assert.Single(EscPosDecoder.Decode(new EscPosRenderer(new EscPosRendererOptions { CutAfterEachPage = false }).Render(list)).Cuts);
    }

    [Fact]
    public void Text_PutsInkInsideItsBounds_AndNowhereElse()
    {
        var page = Roll80(60);
        var left = page.Margins.Left;
        var list = List(page, new DrawTextCommand(new Rect(left, 10, 150, 20), "TOTAL 123.45", Style("Lato", 14)));

        var decoded = EscPosDecoder.Decode(new EscPosRenderer().Render(list));

        var top = (int)(10 * DotsPerPoint);
        var bottom = (int)(30 * DotsPerPoint);
        var right = (int)(150 * DotsPerPoint);
        Assert.True(decoded.CountBlack(0, top, right, bottom) > 200, "el texto debe dejar tinta dentro de su caja");
        Assert.Equal(0, decoded.CountBlack(right + 8, 0, 576, decoded.Height));
        Assert.Equal(0, decoded.CountBlack(0, bottom + 8, 576, decoded.Height));
    }

    [Fact]
    public void Gray_IsSolidWithThreshold_AndPatternedWithFloydSteinberg()
    {
        var page = Roll80(40);
        var list = List(page, new DrawRectangleCommand(new Rect(page.Margins.Left, 0, page.ContentWidth, 40), Color.FromHex("#808080"), null));
        var area = 576 * (int)(40 * DotsPerPoint);

        var threshold = EscPosDecoder.Decode(new EscPosRenderer(new EscPosRendererOptions { Threshold = 160 }).Render(list));
        Assert.Equal(area, threshold.CountBlack(0, 0, 576, (int)(40 * DotsPerPoint)));

        var dithered = EscPosDecoder.Decode(new EscPosRenderer(new EscPosRendererOptions { Dithering = EscPosDithering.FloydSteinberg, Threshold = 128 }).Render(list));
        var black = dithered.CountBlack(0, 0, 576, (int)(40 * DotsPerPoint));
        Assert.InRange(black / (double)area, 0.4, 0.6);
    }

    [Fact]
    public void Options_AreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EscPosRenderer(new EscPosRendererOptions { DotsPerLine = 100 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EscPosRenderer(new EscPosRendererOptions { Threshold = 256 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EscPosRenderer(new EscPosRendererOptions { BandRows = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EscPosRenderer(new EscPosRendererOptions { Dpi = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EscPosRenderer(new EscPosRendererOptions { CashDrawerPin = 2 }));
        Assert.Throws<ArgumentNullException>(() => new EscPosRenderer(null!));

        // Las opciones se releen en cada Render: un cambio inválido en caliente también salta.
        var options = new EscPosRendererOptions();
        var renderer = new EscPosRenderer(options);
        options.DotsPerLine = 12;
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(List(Roll80(10))));
    }

    [Fact]
    public void Cancelled_Token_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => new EscPosRenderer().Render(List(Roll80(10)), cts.Token));
    }

    [Fact]
    public void Render_ToStream_WritesTheSameBytes()
    {
        var list = List(Roll80(30), new DrawLineCommand(new Point(20, 15), new Point(200, 15), 1, Color.Black));
        var renderer = new EscPosRenderer();
        using var stream = new MemoryStream();
        renderer.Render(list, stream, CancellationToken.None);
        Assert.Equal(renderer.Render(list), stream.ToArray());
    }
}
