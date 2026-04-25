namespace NetReporter.Core.Elements;

/// <summary>Cómo encajar una imagen en su <c>Bounds</c>.</summary>
public enum ImageFit
{
    /// <summary>Estira la imagen para llenar el rectángulo (puede deformar el aspect ratio).</summary>
    Fill,
    /// <summary>Encaja preservando aspect ratio; deja espacio si las proporciones no coinciden.</summary>
    Contain
}

/// <summary>
/// Imagen embebida en el reporte. Los bytes se transportan tal cual al renderer
/// (PDF/SVG/HTML), que decodifica usando el <see cref="MimeType"/> para incluirla.
/// Formatos típicos: <c>image/png</c>, <c>image/jpeg</c>, <c>image/gif</c>, <c>image/webp</c>.
/// </summary>
public sealed record ImageElement : ReportElement
{
    /// <summary>Bytes de la imagen ya codificada (PNG, JPEG, etc.).</summary>
    public required byte[] Data { get; init; }

    /// <summary>MIME type, ej. <c>image/png</c>. Default png.</summary>
    public string MimeType { get; init; } = "image/png";

    public ImageFit Fit { get; init; } = ImageFit.Contain;
}
