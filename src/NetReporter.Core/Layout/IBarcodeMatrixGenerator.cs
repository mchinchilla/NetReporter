using NetReporter.Core.Elements;

namespace NetReporter.Core.Layout;

/// <summary>
/// Abstracción que produce una matriz booleana representando un código (QR/Code128/etc.).
/// La implementación default falla — el cliente debe pasar un generador real
/// (típicamente <c>ZXingBarcodeGenerator</c> en el proyecto NetReporter.Barcodes).
/// </summary>
public interface IBarcodeMatrixGenerator
{
    /// <summary>
    /// Genera la matriz natural de módulos para el valor dado, sin escalado.
    /// El LayoutEngine se encarga de escalar al rectángulo destino.
    /// </summary>
    /// <returns>
    /// Matriz <c>bool[width, height]</c> donde <c>true</c> = módulo oscuro (pintar),
    /// <c>false</c> = claro. Para QR y otros 2D: dimensiones cuadradas.
    /// Para 1D: height típicamente 1.
    /// </returns>
    bool[,] Generate(string value, BarcodeFormat format);
}

public sealed class ThrowingBarcodeGenerator : IBarcodeMatrixGenerator
{
    public static readonly ThrowingBarcodeGenerator Instance = new();

    public bool[,] Generate(string value, BarcodeFormat format)
    {
        throw new InvalidOperationException(
            "El reporte usa BarcodeElement pero el LayoutEngine no recibió un IBarcodeMatrixGenerator. " +
            "Pásalo via constructor: new LayoutEngine(measurer, ZXingBarcodeGenerator.Instance) " +
            "(requiere referencia al proyecto NetReporter.Barcodes).");
    }
}
