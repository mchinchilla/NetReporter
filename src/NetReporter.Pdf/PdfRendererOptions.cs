namespace NetReporter.Pdf;

public sealed record PdfRendererOptions
{
    /// <summary>
    /// Embebe cada fuente recortada a los glifos que usa el documento (por defecto). Desactivarlo
    /// embebe las fuentes completas: el PDF se ve igual pero pesa ~200 KB más por variante usada.
    /// Pensado como válvula de escape si una fuente concreta se ve mal recortada.
    /// </summary>
    public bool SubsetFonts { get; init; } = true;
}
