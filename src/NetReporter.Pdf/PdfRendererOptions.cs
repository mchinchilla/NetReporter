namespace NetReporter.Pdf;

public sealed record PdfRendererOptions
{
    /// <summary>
    /// Embebe cada fuente recortada a los glifos que usa el documento (por defecto). Desactivarlo
    /// embebe las fuentes completas: el PDF se ve igual pero pesa ~200 KB más por variante usada.
    /// Pensado como válvula de escape si una fuente concreta se ve mal recortada.
    /// </summary>
    public bool SubsetFonts { get; init; } = true;

    /// <summary>
    /// Comparte entre todos los documentos del proceso la fuente recortada de cada tipografía
    /// (requiere <see cref="SubsetFonts"/>). El recorte contiene todos los glifos que el proceso ha
    /// usado hasta ahora y crece cuando aparece uno nuevo: los primeros documentos lo completan y
    /// después ya no cambia. Así no se recorta ni se crea una tipografía por documento y Skia mantiene
    /// caliente su cache de glifos, lo que importa cuando muchos hilos generan documentos pequeños a
    /// la vez. A cambio, el PDF embebe los glifos que haya usado cualquier documento anterior y sus
    /// bytes dependen de lo que el proceso haya renderizado antes. Desactivado por defecto.
    /// </summary>
    public bool ShareFontSubsets { get; init; }
}
