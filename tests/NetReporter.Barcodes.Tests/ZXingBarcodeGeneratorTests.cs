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
