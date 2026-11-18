using NetReporter.Barcodes;
using NetReporter.Core.Elements;

namespace NetReporter.Barcodes.Tests;

public sealed class ZXingBarcodeGeneratorTests
{
    private static readonly ZXingBarcodeGenerator Gen = ZXingBarcodeGenerator.Instance;

    [Fact]
    public void QrCode_GeneratesSquareMatrix()
    {
        var matrix = Gen.Generate("https://example.com", BarcodeFormat.QrCode);
        Assert.Equal(matrix.GetLength(0), matrix.GetLength(1));
        // Algunos módulos están encendidos (no es matrix vacía).
        bool anyDark = false;
        for (int x = 0; x < matrix.GetLength(0); x++)
            for (int y = 0; y < matrix.GetLength(1); y++)
                if (matrix[x, y]) { anyDark = true; break; }
        Assert.True(anyDark, "QR matrix is empty");
    }

    [Fact]
    public void QrCode_FinderPatternsPresent()
    {
        // Los QR tienen 3 finder patterns (esquinas top-left, top-right, bottom-left)
        // que son cuadrados 7×7 con el patrón clásico. No verificamos exactamente,
        // solo que las esquinas tengan modules oscuros (sanity check).
        var matrix = Gen.Generate("test", BarcodeFormat.QrCode);
        var w = matrix.GetLength(0);
        var h = matrix.GetLength(1);
        // Cada esquina del QR (sin contar margins/quiet zone que ZXing puede agregar)
        // contiene módulos oscuros.
        Assert.True(matrix[0, 0] || matrix[1, 1] || matrix[2, 2], "top-left finder missing");
        Assert.True(matrix[w - 1, 0] || matrix[w - 2, 1] || matrix[w - 3, 2], "top-right finder missing");
        Assert.True(matrix[0, h - 1] || matrix[1, h - 2] || matrix[2, h - 3], "bottom-left finder missing");
    }

    [Theory]
    [InlineData("Ferretería El Clavo")]
    [InlineData("Año: piñata, acción, ü")]
    [InlineData("08011999123456|Ferretería|A1B2C3|001-001-01-00000042|20261117|915.47")]
    [InlineData("€ 10 · 日本")]
    public void QrCode_TextBeyondAscii_ReadsBackTheSame(string text)
    {
        // UTF-8 con su marca ECI: un lector no tiene que adivinar la codificación, así que lee las tildes bien.
        Assert.Equal(text, Read(Gen.Generate(text, BarcodeFormat.QrCode)));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("08011999123456|Ferreteria|A1B2C3|001-001-01-00000042|20261117|915.47")]
    [InlineData("test")]
    public void QrCode_AsciiText_IsTheSameMatrixAsBefore(string text)
    {
        // Un texto sólo ASCII no lleva marca de codificación: el QR sale idéntico al de antes.
        var before = new ZXing.QrCode.QRCodeWriter().encode(text, ZXing.BarcodeFormat.QR_CODE, 1, 1, new Dictionary<ZXing.EncodeHintType, object>
        {
            [ZXing.EncodeHintType.MARGIN] = 0,
            [ZXing.EncodeHintType.ERROR_CORRECTION] = ZXing.QrCode.Internal.ErrorCorrectionLevel.M,
        });
        var now = Gen.Generate(text, BarcodeFormat.QrCode);
        Assert.Equal(before.Width, now.GetLength(0));
        for (int x = 0; x < before.Width; x++)
            for (int y = 0; y < before.Height; y++)
                Assert.Equal(before[x, y], now[x, y]);
        Assert.Equal(text, Read(now));
    }

    /// <summary>Lee un QR como un teléfono: cada módulo como 4×4 píxeles, con zona de silencio, sin pistas de codificación.</summary>
    private static string? Read(bool[,] matrix)
    {
        const int scale = 4, quiet = 4;
        var size = (matrix.GetLength(0) + (2 * quiet)) * scale;
        var pixels = new byte[size * size];
        Array.Fill(pixels, (byte)255);
        for (int x = 0; x < matrix.GetLength(0); x++)
            for (int y = 0; y < matrix.GetLength(1); y++)
                if (matrix[x, y])
                    for (int dx = 0; dx < scale; dx++)
                        for (int dy = 0; dy < scale; dy++)
                            pixels[(((y + quiet) * scale) + dy) * size + ((x + quiet) * scale) + dx] = 0;
        var reader = new ZXing.BarcodeReaderGeneric { Options = new ZXing.Common.DecodingOptions { PossibleFormats = [ZXing.BarcodeFormat.QR_CODE], PureBarcode = true } };
        return reader.Decode(new ZXing.PlanarYUVLuminanceSource(pixels, size, size, 0, 0, size, size, false))?.Text;
    }

    [Fact]
    public void Code128_GeneratesNonEmptyMatrix()
    {
        var matrix = Gen.Generate("ABC123", BarcodeFormat.Code128);
        Assert.True(matrix.GetLength(0) > 0);
        Assert.True(matrix.GetLength(1) > 0);
        // Code 128 típicamente es 1D pero ZXing devuelve un rectángulo;
        // verificamos que hay barras (alternancia).
        int darkCount = 0;
        for (int x = 0; x < matrix.GetLength(0); x++)
            if (matrix[x, 0]) darkCount++;
        Assert.True(darkCount > 5, "Code128 has too few bars");
    }

    [Fact]
    public void Ean13_RequiresExactly12Or13Digits()
    {
        // EAN-13 estricto. ZXing valida; valor inválido lanza.
        Assert.ThrowsAny<Exception>(() =>
            Gen.Generate("INVALID", BarcodeFormat.Ean13));

        // Valor válido (12 dígitos + check, o 13).
        var matrix = Gen.Generate("5901234123457", BarcodeFormat.Ean13);
        Assert.True(matrix.GetLength(0) > 0);
    }

    [Fact]
    public void EmptyValue_StillProducesMatrix()
    {
        // QR puede codificar string vacío o un único espacio (la lib lo maneja).
        var matrix = Gen.Generate("", BarcodeFormat.QrCode);
        Assert.True(matrix.GetLength(0) > 0);
    }

    [Fact]
    public void NullValue_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Gen.Generate(null!, BarcodeFormat.QrCode));
    }

    [Fact]
    public void ThrowingGenerator_AlwaysThrows()
    {
        var thrower = NetReporter.Core.Layout.ThrowingBarcodeGenerator.Instance;
        Assert.Throws<InvalidOperationException>(() =>
            thrower.Generate("test", BarcodeFormat.QrCode));
    }
}
