using NetReporter.Core.Styles;
using NetReporter.Fonts.Liberation;
using NetReporter.Svg;
using SkiaSharp;

namespace NetReporter.Svg.Tests;

// El registro de fuentes es global al proceso: estos tests no corren en paralelo con los demás
// y dejan el registro vacío al terminar.
[CollectionDefinition(nameof(RegisteredFontsCollection), DisableParallelization = true)]
public sealed class RegisteredFontsCollection;

[Collection(nameof(RegisteredFontsCollection))]
public sealed class RegisteredFontsTests : IDisposable
{
    private static readonly string FontsDir = Path.Combine(AppContext.BaseDirectory, "Fonts");
    private static readonly string LatoPath = Path.Combine(FontsDir, "Lato-Regular.ttf");

    public void Dispose() => SkiaFonts.ResetRegisteredFonts();

    [Fact]
    public void RegisterFont_FromFile_ResolvesItsFamily()
    {
        var face = Assert.Single(SkiaFonts.RegisterFont(LatoPath));

        Assert.Equal("Lato", face.FamilyName);
        Assert.Same(face, SkiaFonts.Resolve("Lato", FontWeight.Normal, FontStyleKind.Normal));
    }

    [Fact]
    public void RegisterFont_WithAFamilyAlias_ResolvesTheAlias()
    {
        using var stream = File.OpenRead(LatoPath);
        var face = Assert.Single(SkiaFonts.RegisterFont(stream, "NR Test Brand"));

        Assert.Same(face, SkiaFonts.Resolve("NR Test Brand", FontWeight.Normal, FontStyleKind.Normal));
    }

    [Fact]
    public void RegisterFont_StyleNotRegistered_UsesTheClosestRegisteredFace()
    {
        var face = Assert.Single(SkiaFonts.RegisterFont(File.ReadAllBytes(LatoPath), "NR Test Brand"));

        // Solo hay regular: la negrita cursiva usa esa cara en lugar de ir al sistema.
        Assert.Same(face, SkiaFonts.Resolve("NR Test Brand", FontWeight.Bold, FontStyleKind.Italic));
    }

    [Fact]
    public void RegisterFont_DataThatIsNotAFont_Throws()
    {
        Assert.Throws<ArgumentException>(() => SkiaFonts.RegisterFont("no es una fuente"u8.ToArray()));
    }

    [Fact]
    public void RegisterFontDirectory_RegistersTheFontsAndSkipsOtherFiles()
    {
        var dir = Directory.CreateTempSubdirectory("nr-fonts-");
        try
        {
            File.Copy(LatoPath, Path.Combine(dir.FullName, "Lato-Regular.ttf"));
            File.WriteAllText(Path.Combine(dir.FullName, "readme.txt"), "no es una fuente");
            File.WriteAllText(Path.Combine(dir.FullName, "roto.ttf"), "tampoco");

            var face = Assert.Single(SkiaFonts.RegisterFontDirectory(dir.FullName));

            Assert.Equal("Lato", face.FamilyName);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void SkiaTextMeasurer_AfterRegisteringAFamily_MeasuresWithTheRegisteredFont()
    {
        var measurer = new SkiaTextMeasurer();
        var style = Style("NR Test Brand");
        var before = measurer.MeasureWidth("Distribuidora", style);   // aún no registrada: fuente del sistema

        var face = Assert.Single(SkiaFonts.RegisterFont(LatoPath, "NR Test Brand"));
        var after = measurer.MeasureWidth("Distribuidora", style);

        using var font = SkiaFonts.CreateFont(face, 10);
        Assert.Equal(font.MeasureText("Distribuidora"), (float)after);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void LiberationFonts_Register_ProvidesSansSerifAndMonoInEveryStyle()
    {
        var faces = LiberationFonts.Register();

        Assert.Equal(12, faces.Count);
        Assert.Same(faces, LiberationFonts.Register());
        foreach (var family in new[] { "Liberation Sans", "Liberation Serif", "Liberation Mono" })
        {
            var bold = SkiaFonts.Resolve(family, FontWeight.Bold, FontStyleKind.Italic);
            Assert.Equal(family, bold.FamilyName);
            Assert.True(bold.FontWeight >= (int)SKFontStyleWeight.Bold);
            Assert.NotEqual(SKFontStyleSlant.Upright, bold.FontSlant);
            Assert.True(bold.GlyphCount > 0);
        }
    }

    private static ResolvedStyle Style(string family) => new(
        FontFamily: family,
        FontSize: 10,
        Weight: FontWeight.Normal,
        Italic: FontStyleKind.Normal,
        Foreground: Core.Primitives.Color.Black,
        Background: Core.Primitives.Color.Transparent,
        TextAlign: TextAlignment.Left,
        VerticalAlign: VerticalAlignment.Top,
        Padding: Core.Primitives.Thickness.Zero,
        Border: null,
        NumberFormat: null);
}
