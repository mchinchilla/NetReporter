using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace NetReporter.Pdf.Tests;

public sealed class TrueTypeSubsetterTests
{
    private const string Sample = "Factura 001-002 ÁÉÍÓÚÑ áéíóúñ 0123456789 Total: L 1,234.50";

    internal static readonly byte[] Lato =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "Lato-Regular.ttf"));

    internal static SKTypeface Load(byte[] font) =>
        SKTypeface.FromData(SKData.CreateCopy(font)) ?? throw new InvalidOperationException("Fuente inválida");

    internal static ushort[] GlyphsOf(SKTypeface typeface, string text)
    {
        using var font = new SKFont(typeface);
        return font.GetGlyphs(text);
    }

    [Fact]
    public void Subset_IsMuchSmaller_AndKeepsGlyphIdsAndAdvances()
    {
        using var original = Load(Lato);

        var bytes = TrueTypeSubsetter.Subset(Lato, 0, GlyphsOf(original, Sample));

        Assert.NotNull(bytes);
        Assert.True(bytes.Length < Lato.Length / 4, $"subset de {bytes.Length} b sobre {Lato.Length} b");
        using var subset = Load(bytes);
        Assert.Equal(original.GlyphCount, subset.GlyphCount);
        using var expected = new SKFont(original, 12);
        using var actual = new SKFont(subset, 12);
        Assert.Equal(expected.GetGlyphs(Sample), actual.GetGlyphs(Sample));
        Assert.Equal(expected.MeasureText(Sample), actual.MeasureText(Sample));
    }

    [Fact]
    public void Subset_KeepsOutlinesOfUsedGlyphs_IncludingCompositeComponents()
    {
        // En Lato las vocales acentuadas son glifos compuestos (letra base + acento): si el recorte
        // no siguiera sus componentes, "Á" quedaría en blanco.
        const string text = "ÁÉÍÓÚÑáéíóúñ";
        using var original = Load(Lato);
        using var subset = Load(TrueTypeSubsetter.Subset(Lato, 0, GlyphsOf(original, text))!);
        using var expectedFont = new SKFont(original, 100);
        using var actualFont = new SKFont(subset, 100);

        foreach (var glyph in expectedFont.GetGlyphs(text))
        {
            using var expected = expectedFont.GetGlyphPath(glyph);
            using var actual = actualFont.GetGlyphPath(glyph);
            Assert.NotNull(actual);
            Assert.False(actual.IsEmpty);
            Assert.Equal(expected.Bounds, actual.Bounds);
        }

        using var unused = actualFont.GetGlyphPath(expectedFont.GetGlyphs("Z")[0]);
        Assert.True(unused is null || unused.IsEmpty);
    }

    [Fact]
    public void Subset_WritesValidChecksums()
    {
        using var original = Load(Lato);
        var font = TrueTypeSubsetter.Subset(Lato, 0, GlyphsOf(original, Sample))!;

        int numTables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (var i = 0; i < numTables; i++)
        {
            var record = font.AsSpan(12 + 16 * i);
            var tag = Encoding.ASCII.GetString(record[..4]);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
            var table = font.AsSpan(offset, length).ToArray();
            if (tag == "head") BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(8), 0);
            Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(record[4..]), Checksum(table));
        }
        // Con checkSumAdjustment bien calculado, la suma de todo el archivo da este valor fijo.
        Assert.Equal(0xB1B0AFBA, Checksum(font));
    }

    [Fact]
    public void Subset_FontCollection_UsesRequestedFace()
    {
        var collection = BuildCollection(Lato, Lato);
        using var original = Load(Lato);
        var glyphs = GlyphsOf(original, Sample);

        var bytes = TrueTypeSubsetter.Subset(collection, 1, glyphs);

        Assert.NotNull(bytes);
        using var subset = Load(bytes);
        using var expected = new SKFont(original, 12);
        using var actual = new SKFont(subset, 12);
        Assert.Equal(expected.MeasureText(Sample), actual.MeasureText(Sample));
        Assert.Null(TrueTypeSubsetter.Subset(collection, 2, glyphs));
    }

    [Fact]
    public void Subset_CffOutlines_ReturnsNull()
    {
        var font = (byte[])Lato.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(font, 0x4F54544F);   // 'OTTO'

        Assert.Null(TrueTypeSubsetter.Subset(font, 0, [1, 2, 3]));
    }

    [Theory]
    [InlineData(0x0100)]   // la licencia prohíbe recortar
    [InlineData(0x0200)]   // solo se permite embeber como bitmap
    public void Subset_LicenseForbidsSubsetting_ReturnsNull(int fsType)
    {
        var font = (byte[])Lato.Clone();
        var os2 = FindTable(font, "OS/2");
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(os2 + 8), (ushort)fsType);

        Assert.Null(TrueTypeSubsetter.Subset(font, 0, [1]));
    }

    [Fact]
    public void Subset_TruncatedFont_ReturnsNull() =>
        Assert.Null(TrueTypeSubsetter.Subset(Lato.AsSpan(0, Lato.Length / 2), 0, [1]));

    // .ttc mínimo: encabezado 'ttcf' + una copia de cada fuente, con los offsets de sus tablas
    // corridos a la posición de la copia dentro de la colección.
    private static byte[] BuildCollection(params byte[][] fonts)
    {
        var header = 12 + 4 * fonts.Length;
        var result = new byte[header + fonts.Sum(f => f.Length)];
        BinaryPrimitives.WriteUInt32BigEndian(result, 0x74746366);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8), (uint)fonts.Length);

        var position = header;
        for (var i = 0; i < fonts.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(12 + 4 * i), (uint)position);
            fonts[i].CopyTo(result, position);
            int numTables = BinaryPrimitives.ReadUInt16BigEndian(fonts[i].AsSpan(4));
            for (var t = 0; t < numTables; t++)
            {
                var offset = result.AsSpan(position + 12 + 16 * t + 8, 4);
                BinaryPrimitives.WriteUInt32BigEndian(offset, BinaryPrimitives.ReadUInt32BigEndian(offset) + (uint)position);
            }
            position += fonts[i].Length;
        }
        return result;
    }

    internal static int FindTable(byte[] font, string tag)
    {
        var wanted = BinaryPrimitives.ReadUInt32BigEndian(Encoding.ASCII.GetBytes(tag));
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (var i = 0; i < numTables; i++)
        {
            var record = font.AsSpan(12 + 16 * i);
            if (BinaryPrimitives.ReadUInt32BigEndian(record) == wanted)
                return (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
        }
        throw new InvalidOperationException($"La fuente no tiene tabla {tag}");
    }

    private static uint Checksum(byte[] data)
    {
        var padded = new byte[(data.Length + 3) & ~3];
        data.CopyTo(padded, 0);
        uint sum = 0;
        for (var i = 0; i < padded.Length; i += 4)
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(padded.AsSpan(i)));
        return sum;
    }
}
