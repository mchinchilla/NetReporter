using NetReporter.Core.Layout;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.OneD;
using BarcodeFormat = NetReporter.Core.Elements.BarcodeFormat;
using ZXingFormat = ZXing.BarcodeFormat;

namespace NetReporter.Barcodes;

/// <summary>
/// Implementación de <see cref="IBarcodeMatrixGenerator"/> usando ZXing.Net.
/// Genera la matriz binaria del código que el LayoutEngine convierte en módulos
/// (rectángulos vectoriales) — el código resulta vectorial en PDF/SVG/HTML.
/// </summary>
public sealed class ZXingBarcodeGenerator : IBarcodeMatrixGenerator
{
    public static readonly ZXingBarcodeGenerator Instance = new();

    public bool[,] Generate(string value, BarcodeFormat format)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrEmpty(value)) value = " ";

        // Pedimos tamaño 1×1 → ZXing usa el tamaño natural mínimo (un píxel por módulo).
        BitMatrix matrix = format switch
        {
            BarcodeFormat.QrCode  => GenerateQr(value),
            BarcodeFormat.Code128 => Generate1D(new Code128Writer(), value, ZXingFormat.CODE_128),
            BarcodeFormat.Code39  => Generate1D(new Code39Writer(), value, ZXingFormat.CODE_39),
            BarcodeFormat.Ean13   => Generate1D(new EAN13Writer(), value, ZXingFormat.EAN_13),
            _ => throw new NotSupportedException($"BarcodeFormat '{format}' no soportado.")
        };

        var w = matrix.Width;
        var h = matrix.Height;
        var result = new bool[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                result[x, y] = matrix[x, y];
        return result;
    }

    private static BitMatrix GenerateQr(string value)
    {
        var writer = new QRCodeWriter();
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.MARGIN] = 0,                 // sin margen — el LayoutEngine añade quiet zone si quiere
            [EncodeHintType.ERROR_CORRECTION] = ZXing.QrCode.Internal.ErrorCorrectionLevel.M
        };
        // 1×1 → ZXing eleva al tamaño natural del QR para los datos.
        return writer.encode(value, ZXingFormat.QR_CODE, 1, 1, hints);
    }

    private static BitMatrix Generate1D(Writer writer, string value, ZXingFormat fmt)
    {
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.MARGIN] = 0
        };
        return writer.encode(value, fmt, 1, 1, hints);
    }
}
