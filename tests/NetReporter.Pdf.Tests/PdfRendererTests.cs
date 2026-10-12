using System.Text;
using System.Text.RegularExpressions;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;
using NetReporter.Svg;
using SkiaSharp;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Pdf.Tests;

public sealed class PdfRendererTests
{
    private static readonly ResolvedStyle DefaultStyle = new(
        FontFamily: "Helvetica",
        FontSize: 10,
        Weight: FontWeight.Normal,
        Italic: FontStyleKind.Normal,
        Foreground: Color.Black,
        Background: Color.Transparent,
        TextAlign: Core.Styles.TextAlignment.Left,
        VerticalAlign: VerticalAlignment.Top,
        Padding: Thickness.Zero,
        Border: null,
        NumberFormat: null);

    private static DrawTextCommand Text(string text) => new(new Rect(36, 36, 400, 14), text, DefaultStyle);

    private static RlRenderList BuildList(params RenderCommand[] commands) =>
        new(PageSetup.Letter, new[] { new RenderPage(1, commands) });

    // Los diccionarios del PDF son ASCII; Latin-1 permite buscarlos sin que los streams binarios estorben.
    private static string Raw(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [Fact]
    public void Render_ProducesOnePdfPagePerRenderPage_WithPageSetupSize()
    {
        var list = new RlRenderList(PageSetup.Letter, new[]
        {
            new RenderPage(1, [Text("Página 1")]),
            new RenderPage(2, [Text("Página 2")]),
            new RenderPage(3, [])
        });

        var pdf = Raw(new PdfRenderer().Render(list));

        Assert.StartsWith("%PDF-", pdf);
        Assert.Equal(3, Regex.Matches(pdf, @"/Type /Page\b").Count);
        Assert.Equal(3, Regex.Matches(pdf, @"/MediaBox \[0 0 612 792\]").Count);
        Assert.Contains("/Producer (NetReporter)", pdf);
    }

    [Fact]
    public void Render_PngImage_IsEmbeddedLossless()
    {
        // Un `new SKDocumentPdfMetadata()` deja EncodingQuality en 0 y Skia recomprime las imágenes
        // como JPEG de calidad mínima (/DCTDecode). El renderer parte de Default para evitarlo.
        var list = BuildList(new DrawImageCommand(new Rect(36, 36, 120, 120), OpaquePng(), "image/png"));

        var pdf = Raw(new PdfRenderer().Render(list));

        Assert.Contains("/Subtype /Image", pdf);
        Assert.DoesNotContain("/DCTDecode", pdf);
    }

    [Fact]
    public void Render_SubsetFonts_ShrinksTheFile_WhenTheSystemFontIsTrueType()
    {
        var list = BuildList(Text("Factura 000-001-01-00001234 · Total: L 169,740.00"));

        var subset = new PdfRenderer().Render(list);
        var full = new PdfRenderer(new PdfRendererOptions { SubsetFonts = false }).Render(list);

        Assert.True(subset.Length <= full.Length, $"subset {subset.Length} b > completo {full.Length} b");
        using var plan = FontSubsetPlan.Build(list);
        var typeface = SkiaFonts.Resolve(DefaultStyle);
        if (!ReferenceEquals(plan.Map(typeface), typeface))
            Assert.True(subset.Length < full.Length / 2, $"subset {subset.Length} b vs completo {full.Length} b");
        // Si no, la fuente del sistema no es recortable (CFF, variable…) y se embebe completa.
    }

    [Fact]
    public void FontSubsetPlan_MapsTypefaceToSubsetWithSameAdvances()
    {
        const string text = "Factura 001 ÁÉÍÓÚÑ áéíóúñ";
        using var lato = TrueTypeSubsetterTests.Load(TrueTypeSubsetterTests.Lato);
        var used = new Dictionary<SKTypeface, HashSet<ushort>>(ReferenceEqualityComparer.Instance)
        {
            [lato] = [.. TrueTypeSubsetterTests.GlyphsOf(lato, text)]
        };

        using var plan = FontSubsetPlan.FromGlyphUsage(used);

        var mapped = plan.Map(lato);
        Assert.NotSame(lato, mapped);
        using var expected = new SKFont(lato, 12);
        using var actual = new SKFont(mapped, 12);
        Assert.Equal(expected.MeasureText(text), actual.MeasureText(text));
    }

    private static byte[] OpaquePng()
    {
        using var bitmap = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Opaque);
        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 64; x++)
            bitmap.SetPixel(x, y, new SKColor((byte)(x * 4), (byte)(y * 4), (byte)((x ^ y) * 4)));
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
