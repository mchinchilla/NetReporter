using System.Globalization;
using System.Text.RegularExpressions;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;
using SkiaSharp;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Svg.Tests;

/// <summary>
/// El SVG se escribe sin el canvas de Skia; estos tests fijan su geometría contra las mismas tablas
/// que usa el PDF y contra el canvas de Skia como oráculo. Usan Lato registrada para no depender de
/// las fuentes del sistema (en CI no hay casi ninguna), así que van en la colección sin paralelismo.
/// </summary>
[Collection(nameof(RegisteredFontsCollection))]
public sealed class SvgPageWriterTests : IDisposable
{
    private static readonly string LatoPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "Lato-Regular.ttf");

    private static readonly ResolvedStyle Lato = new(
        FontFamily: "Lato", FontSize: 10, Weight: FontWeight.Normal, Italic: FontStyleKind.Normal,
        Foreground: Color.Black, Background: Color.Transparent,
        TextAlign: TextAlignment.Left, VerticalAlign: VerticalAlignment.Top,
        Padding: Thickness.Zero, Border: null, NumberFormat: null);

    public SvgPageWriterTests() => SkiaFonts.RegisterFont(LatoPath);

    public void Dispose() => SkiaFonts.ResetRegisteredFonts();

    private static string Render(params RenderCommand[] commands) =>
        new SvgRenderer().Render(new RlRenderList(PageSetup.Letter, [new RenderPage(1, commands)]))[0];

    private static float[] Xs(string svg, int textIndex = 0)
    {
        var m = Regex.Matches(svg, "<text [^>]*? x=\"([^\"]*)\" y=\"([^\"]*)\"")[textIndex];
        return m.Groups[1].Value.Split(' ').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
    }

    private static float Y(string svg, int textIndex = 0) =>
        float.Parse(Regex.Matches(svg, "<text [^>]*? y=\"([^\"]*)\"")[textIndex].Groups[1].Value, CultureInfo.InvariantCulture);

    // === Texto ===

    [Fact]
    public void Text_EachGlyphGetsTheAdvanceOfTheMeasuringTable()
    {
        const string text = "Cliente número 7";
        var svg = Render(new DrawTextCommand(new Rect(36, 50, 300, 14), text, Lato));

        var typeface = SkiaFonts.Resolve(Lato);
        var advances = SkiaFonts.Advances(typeface, 10);
        var expected = new List<float>();
        float x = 36;
        foreach (var glyph in advances.Glyphs(text)) { expected.Add(x); x += advances.Of(glyph); }

        var xs = Xs(svg);
        Assert.Equal(text.Length, xs.Length);
        for (var i = 0; i < xs.Length; i++) Assert.Equal(expected[i], xs[i], 0.001);

        using var font = SkiaFonts.CreateFont(typeface, 10);
        Assert.Equal(50 - font.Metrics.Ascent, Y(svg), 0.001);
        Assert.Contains("xml:space=\"preserve\"", svg);
        Assert.Contains(">Cliente número 7</text>", svg);
    }

    [Fact]
    public void Text_HorizontalAndVerticalAlignment_MatchThePainterGeometry()
    {
        var width = SkiaFonts.Advances(SkiaFonts.Resolve(Lato), 10).Width("abc");
        var box = new Rect(10, 20, 200, 40);
        var center = Render(new DrawTextCommand(box, "abc", Lato with { TextAlign = TextAlignment.Center, VerticalAlign = VerticalAlignment.Center }));
        var right = Render(new DrawTextCommand(box, "abc", Lato with { TextAlign = TextAlignment.Right, VerticalAlign = VerticalAlignment.Bottom }));

        using var font = SkiaFonts.CreateFont(SkiaFonts.Resolve(Lato), 10);
        var m = font.Metrics;
        var lineHeight = m.Descent - m.Ascent;

        Assert.Equal(10 + (200 - width) / 2, Xs(center)[0], 0.001);
        Assert.Equal(20 + (40 - lineHeight) / 2 - m.Ascent, Y(center), 0.001);
        Assert.Equal(210 - width, Xs(right)[0], 0.001);
        Assert.Equal(20 + (40 - lineHeight) - m.Ascent, Y(right), 0.001);
    }

    [Fact]
    public void Text_IsEscaped_AndControlCharactersKeepOnePositionPerCharacter()
    {
        var text = "a & b < c > d\te";
        var svg = Render(new DrawTextCommand(new Rect(0, 0, 300, 14), text, Lato));

        Assert.Contains(">a &amp; b &lt; c &gt; d e</text>", svg);
        Assert.Equal(text.Length, Xs(svg).Length);
    }

    [Fact]
    public void Text_SurrogatePairs_CountAsOneCharacterAndOneGlyph()
    {
        var text = "a😀b";   // 4 UTF-16 chars, 3 code points
        var svg = Render(new DrawTextCommand(new Rect(0, 0, 300, 14), text, Lato));

        var positions = Regex.Matches(svg, "<text [^>]*? x=\"([^\"]*)\"").Sum(m => m.Groups[1].Value.Split(' ').Length);
        Assert.Equal(3, positions);
        Assert.Contains("😀", svg);
    }

    [Fact]
    public void Text_ClipPath_OnlyWhenTheTextLeavesItsBox()
    {
        var fits = Render(new DrawTextCommand(new Rect(0, 0, 300, 14), "corto", Lato));
        var overflows = Render(new DrawTextCommand(new Rect(0, 0, 20, 14), "este texto es demasiado largo", Lato));
        var tooShortBox = Render(new DrawTextCommand(new Rect(0, 0, 300, 2), "corto", Lato));

        Assert.DoesNotContain("<clipPath", fits);
        Assert.Contains("<clipPath id=\"c1\"><rect x=\"0\" y=\"-3.5\" width=\"20\" height=\"21\"/></clipPath>", overflows);
        Assert.Contains("<g clip-path=\"url(#c1)\">", overflows);
        Assert.Contains("</g>", overflows);
        Assert.Contains("<clipPath", tooShortBox);
    }

    [Fact]
    public void Text_FontAttributes_NameTheRequestedFamilyTheResolvedOneAndTheGeneric()
    {
        var plain = Render(new DrawTextCommand(new Rect(0, 0, 300, 14), "x", Lato with { FontFamily = "Mi Marca" }));
        var colored = Render(new DrawTextCommand(new Rect(0, 0, 300, 14), "x", Lato with { Foreground = new Color(0x25, 0x63, 0xEB, 128) }));

        // "Mi Marca" no existe: SkiaFonts cae a la registrada (Lato) del mismo genérico.
        Assert.Contains("font-family=\"'Mi Marca', Lato, sans-serif\"", plain);
        Assert.Contains("font-size=\"10\"", plain);
        Assert.DoesNotContain("font-weight", plain);   // solo hay Lato Regular: se dibujó en 400
        Assert.Contains("fill=\"#2563EB\" fill-opacity=\"0.502\"", colored);
    }

    [Fact]
    public void Text_CharactersTheFontLacks_StillGetOnePositionEach()
    {
        // Lato no tiene CJK: con fuentes del sistema sale en un tramo de respaldo, sin ellas se queda en Lato.
        var text = "precio 漢字 final";
        var svg = Render(new DrawTextCommand(new Rect(0, 0, 300, 14), text, Lato));

        var texts = Regex.Matches(svg, "<text [^>]*? x=\"([^\"]*)\"[^>]*>(.*?)</text>");
        Assert.True(texts.Count >= 1);
        Assert.Equal(text.Length, texts.Sum(m => m.Groups[1].Value.Split(' ').Length));
        Assert.Equal(text, string.Concat(texts.Select(m => m.Groups[2].Value)));
    }

    [Fact]
    public void Text_Empty_WritesNothing()
    {
        var svg = Render(new DrawTextCommand(new Rect(0, 0, 300, 14), "", Lato));
        Assert.DoesNotContain("<text", svg);
    }

    // === Formas ===

    [Fact]
    public void Rectangle_UniformRadius_UsesRx_AndPartialCornersUseArcs()
    {
        var fill = Color.FromHex("#E3F1F4");
        var uniform = Render(new DrawRectangleCommand(new Rect(10, 10, 100, 50), fill, new BorderLine(1, Color.Black), 6));
        var partial = Render(new DrawRectangleCommand(new Rect(10, 10, 100, 50), fill, null, 6, RectCorners.TopLeft | RectCorners.TopRight));
        var square = Render(new DrawRectangleCommand(new Rect(10, 10, 100, 50), fill, null));

        Assert.Contains("<rect x=\"10\" y=\"10\" width=\"100\" height=\"50\" rx=\"6\" fill=\"#E3F1F4\" stroke=\"#000000\" stroke-width=\"1\"/>", uniform);
        Assert.Contains("<path d=\"M16 10H104A6 6 0 0 1 110 16V60H10V16A6 6 0 0 1 16 10Z\" fill=\"#E3F1F4\"/>", partial);
        Assert.Contains("<rect x=\"10\" y=\"10\" width=\"100\" height=\"50\" fill=\"#E3F1F4\"/>", square);
    }

    [Fact]
    public void Rectangle_RadiiLargerThanTheBox_ShrinkLikeSkia()
    {
        var svg = Render(new DrawRectangleCommand(new Rect(0, 0, 10, 10), Color.Black, null, 8));
        Assert.Contains("rx=\"5\"", svg);
    }

    [Fact]
    public void Rectangle_WithoutFillOrBorder_WritesNothing()
    {
        var svg = Render(new DrawRectangleCommand(new Rect(0, 0, 10, 10), Color.Transparent, null));
        Assert.DoesNotContain("<rect", svg);
    }

    [Fact]
    public void Line_UsesStrokeWidth_AndZeroThicknessIsAHairline()
    {
        var line = Render(new DrawLineCommand(new Point(0, 5), new Point(100, 5), 0.5, Color.FromHex("#CBD5E1")));
        var hairline = Render(new DrawLineCommand(new Point(0, 5), new Point(100, 5), 0, Color.Black));

        Assert.Contains("<line x1=\"0\" y1=\"5\" x2=\"100\" y2=\"5\" stroke=\"#CBD5E1\" stroke-width=\"0.5\"/>", line);
        Assert.Contains("stroke-width=\"0.5\"", hairline);
    }

    [Fact]
    public void Path_ClosedIsFilled_OpenIsStrokedWithRoundJoins()
    {
        var points = new[] { new Point(10, 10), new Point(60, 10), new Point(35, 50) };
        var closed = Render(DrawPathCommand.Create(points, true, Color.FromHex("#2563EB"), null));
        var open = Render(DrawPathCommand.Create(points, false, Color.FromHex("#2563EB"), new BorderLine(2, Color.FromHex("#F59E0B"))));

        Assert.Contains("<path d=\"M10 10L60 10L35 50Z\" fill=\"#2563EB\"/>", closed);
        Assert.Contains("<path d=\"M10 10L60 10L35 50\" fill=\"none\" stroke=\"#F59E0B\" stroke-width=\"2\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>", open);
    }

    // === Imágenes ===

    [Fact]
    public void Image_Contain_CentersTheImageBox_AndEmbedsTheOriginalBytes()
    {
        var png = Png(40, 20);
        var svg = Render(new DrawImageCommand(new Rect(0, 0, 100, 100), png, "image/png"));

        // 2:1 en una caja cuadrada: ancho completo, alto 50, centrada en vertical.
        Assert.Contains("<image x=\"0\" y=\"25\" width=\"100\" height=\"50\" preserveAspectRatio=\"none\" href=\"data:image/png;base64,"
                        + Convert.ToBase64String(png) + "\"/>", svg);
    }

    [Fact]
    public void Image_Fill_StretchesToTheBox_AndMimeComesFromTheBytes()
    {
        var png = Png(40, 20);
        var svg = Render(new DrawImageCommand(new Rect(5, 5, 100, 100), png, "image/jpeg", Core.Elements.ImageFit.Fill));

        Assert.Contains("<image x=\"5\" y=\"5\" width=\"100\" height=\"100\" preserveAspectRatio=\"none\" href=\"data:image/png;base64,", svg);
    }

    [Fact]
    public void Image_InvalidBytes_IsSkippedLikeThePdf()
    {
        var svg = Render(new DrawImageCommand(new Rect(0, 0, 100, 100), "no es una imagen"u8.ToArray(), "image/png"));
        Assert.DoesNotContain("<image", svg);
    }

    [Fact]
    public void Image_WithAnHrefResolver_IsReferencedNotEmbedded()
    {
        var png = Png(8, 8);
        using var ctx = new SvgWriterContext((data, mime) => $"/img/{mime}/{data.Length}");
        var page = new RenderPage(1, [new DrawImageCommand(new Rect(0, 0, 10, 10), png, "image/png")]);

        var svg = SvgRenderer.RenderPage(page, 612, 792, ctx);

        Assert.Contains($"href=\"/img/image/png/{png.Length}\"", svg);
        Assert.DoesNotContain("data:", svg);
    }

    [Fact]
    public void Context_DecodesEachImageOnce_AndReusesItsBase64()
    {
        var png = Png(8, 8);
        using var ctx = new SvgWriterContext();

        var first = ctx.Image(png, null)!;
        Assert.Same(first, ctx.Image(png, null));
        Assert.Same(first.DataUri(png), first.DataUri(png));
        Assert.Equal((8, 8, "image/png"), (first.Width, first.Height, first.Mime));
        Assert.Null(ctx.Image("x"u8.ToArray(), null));
    }

    // === Documento ===

    [Fact]
    public void Render_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var list = new RlRenderList(PageSetup.Letter, [new RenderPage(1, [])]);
        Assert.Throws<OperationCanceledException>(() => new SvgRenderer().Render(list, cts.Token));
    }

    [Fact]
    public void Render_RootDeclaresTheNamespaceAndThePageSize()
    {
        var svg = Render();
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"612\" height=\"792\" viewBox=\"0 0 612 792\">", svg);
        Assert.EndsWith("</svg>\n", svg);
    }

    /// <summary>
    /// El canvas SVG de Skia pintado por <see cref="SkiaPagePainter"/> es el oráculo: la x de cada
    /// glifo y la línea base de cada texto deben coincidir, porque es lo que el PDF dibuja.
    /// </summary>
    [Fact]
    public void Text_PositionsMatchTheSkiaSvgCanvasPaintedByThePdfPainter()
    {
        var commands = new RenderCommand[]
        {
            new DrawTextCommand(new Rect(36, 36, 300, 14), "Factura 000-001", Lato with { FontSize = 18 }),
            new DrawTextCommand(new Rect(36, 60, 300, 14), "Total: 1,234.50", Lato with { TextAlign = TextAlignment.Right }),
            new DrawTextCommand(new Rect(36, 80, 300, 30), "Centrado", Lato with { TextAlign = TextAlignment.Center, VerticalAlign = VerticalAlignment.Center, FontSize = 7.5 }),
            new DrawTextCommand(new Rect(36, 120, 300, 14), "Abajo", Lato with { VerticalAlign = VerticalAlignment.Bottom }),
        };
        var page = new RenderPage(1, commands);

        using var ms = new MemoryStream();
        using (var canvas = SKSvgCanvas.Create(new SKRect(0, 0, 612, 792), ms))
            SkiaPagePainter.Draw(canvas, page);
        var skia = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        var ours = new SvgRenderer().RenderPage(page, 612, 792);

        var skiaTexts = Regex.Matches(skia, "<text [^>]*? x=\"([^\"]*)\" y=\"([^\"]*)\"");
        Assert.Equal(commands.Length, skiaTexts.Count);
        for (var i = 0; i < commands.Length; i++)
        {
            var skiaXs = skiaTexts[i].Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            var skiaY = float.Parse(skiaTexts[i].Groups[2].Value.TrimEnd(',', ' '), CultureInfo.InvariantCulture);
            var xs = Xs(ours, i);
            Assert.Equal(skiaXs.Length, xs.Length);
            for (var g = 0; g < xs.Length; g++) Assert.Equal(skiaXs[g], xs[g], 0.002);
            Assert.Equal(skiaY, Y(ours, i), 0.002);
        }
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}

public sealed class PageGeometryTests
{
    [Theory]
    [InlineData(100, 50, 6, RectCorners.All)]
    [InlineData(10, 10, 8, RectCorners.All)]
    [InlineData(10, 40, 8, RectCorners.TopLeft | RectCorners.TopRight)]
    [InlineData(30, 10, 12, RectCorners.TopLeft | RectCorners.BottomLeft)]
    [InlineData(100, 50, 6, RectCorners.TopLeft)]
    public void RoundRectRadii_MatchWhatSkRoundRectWouldUse(float w, float h, float radius, RectCorners corners)
    {
        var rect = new SKRect(0, 0, w, h);
        Span<float> radii = stackalloc float[4];
        Assert.True(PageGeometry.RoundRectRadii(rect, radius, corners, radii));

        // Oráculo: SKRoundRect con los radios sin ajustar; Skia los reduce él mismo cuando no caben.
        SKPoint R(RectCorners c) => corners.HasFlag(c) ? new SKPoint(radius, radius) : SKPoint.Empty;
        using var rr = new SKRoundRect();
        rr.SetRectRadii(rect, [R(RectCorners.TopLeft), R(RectCorners.TopRight), R(RectCorners.BottomRight), R(RectCorners.BottomLeft)]);
        var expected = rr.Radii;

        for (var i = 0; i < 4; i++) Assert.Equal(expected[i].X, radii[i], 0.0005);
    }

    [Fact]
    public void RoundRectRadii_NoRadiusOrNoCorner_IsNotRounded()
    {
        Span<float> radii = stackalloc float[4];
        Assert.False(PageGeometry.RoundRectRadii(new SKRect(0, 0, 10, 10), 0, RectCorners.All, radii));
        Assert.False(PageGeometry.RoundRectRadii(new SKRect(0, 0, 10, 10), 4, RectCorners.None, radii));
    }
}
