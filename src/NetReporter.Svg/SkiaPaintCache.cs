using SkiaSharp;

namespace NetReporter.Svg;

/// <summary>
/// Objetos de Skia que <see cref="SkiaPagePainter"/> reutiliza mientras pinta un documento: un
/// <see cref="SKFont"/> por (tipografía, tamaño), cada imagen decodificada una sola vez y una
/// pintura de relleno y otra de trazo a las que solo se les cambia el color.
/// <para>
/// Crear y destruir un SKFont y un SKPaint por cada texto dominaba el tiempo del painter y, peor,
/// serializaba a los hilos que renderizan en paralelo: SkiaSharp registra cada objeto administrado
/// en un diccionario global bajo un lock. Compartir el <see cref="SKImage"/> entre páginas hace
/// además que el backend PDF de Skia embeba la imagen una sola vez (la deduplica por instancia,
/// no por bytes): un logo en el PageHeader ya no se repite en cada página del archivo.
/// </para>
/// Una instancia por documento y por hilo: no es thread-safe.
/// </summary>
public sealed class SkiaPaintCache : IDisposable
{
    private readonly Dictionary<(SKTypeface Typeface, float Size), SKFont> _fonts = [];
    private readonly Dictionary<(SKTypeface Typeface, float Size), SKFontMetrics> _metrics = [];
    private readonly Dictionary<byte[], SKImage?> _images = new(ReferenceEqualityComparer.Instance);
    private readonly SKPaint _fill = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private readonly SKPaint _stroke = new() { Style = SKPaintStyle.Stroke, IsAntialias = true };
    private readonly SKTextBlobBuilder _blobs = new();

    /// <summary>Constructor de blobs de texto que el painter reutiliza: <c>Build()</c> lo deja vacío para el siguiente.</summary>
    internal SKTextBlobBuilder BlobBuilder => _blobs;

    /// <summary>
    /// SKFont de <see cref="SkiaFonts.CreateFont"/> para esa tipografía y tamaño. Vive hasta
    /// <see cref="Dispose"/>; no hay que liberarlo.
    /// </summary>
    public SKFont Font(SKTypeface typeface, float size)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        if (!_fonts.TryGetValue((typeface, size), out var font))
            _fonts[(typeface, size)] = font = SkiaFonts.CreateFont(typeface, size);
        return font;
    }

    /// <summary>
    /// Métricas de <see cref="Font"/> para esa tipografía y tamaño. Leer <see cref="SKFont.Metrics"/>
    /// pasa por el strike cache de Skia, que tiene un lock global: con varios documentos en paralelo
    /// era, por sí solo, una de las llamadas que serializaba a los hilos.
    /// </summary>
    public SKFontMetrics Metrics(SKTypeface typeface, float size)
    {
        if (!_metrics.TryGetValue((typeface, size), out var metrics))
            _metrics[(typeface, size)] = metrics = Font(typeface, size).Metrics;
        return metrics;
    }

    /// <summary>
    /// Imagen decodificada de <paramref name="data"/>, o null si los bytes no son una imagen. Se
    /// identifica por la instancia del arreglo: el LayoutEngine emite el mismo <c>byte[]</c> del
    /// elemento en cada página en la que aparece.
    /// </summary>
    public SKImage? Image(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!_images.TryGetValue(data, out var image))
            _images[data] = image = data.Length > 0 ? SKImage.FromEncodedData(data) : null;
        return image;
    }

    /// <summary>
    /// Pintura de relleno antialias con ese color. Es una única instancia compartida: solo es
    /// válida hasta la siguiente llamada a <see cref="Fill"/>.
    /// </summary>
    public SKPaint Fill(SKColor color)
    {
        _fill.Color = color;
        return _fill;
    }

    /// <summary>
    /// Pintura de trazo antialias. Los valores por defecto de unión y remate son los de Skia
    /// (Miter / Butt). Misma advertencia que <see cref="Fill"/>: una única instancia compartida.
    /// </summary>
    public SKPaint Stroke(SKColor color, float width,
                          SKStrokeJoin join = SKStrokeJoin.Miter, SKStrokeCap cap = SKStrokeCap.Butt)
    {
        _stroke.Color = color;
        _stroke.StrokeWidth = width;
        _stroke.StrokeJoin = join;
        _stroke.StrokeCap = cap;
        return _stroke;
    }

    public void Dispose()
    {
        foreach (var font in _fonts.Values) font.Dispose();
        _fonts.Clear();
        _metrics.Clear();
        foreach (var image in _images.Values) image?.Dispose();
        _images.Clear();
        _fill.Dispose();
        _stroke.Dispose();
        _blobs.Dispose();
    }
}
