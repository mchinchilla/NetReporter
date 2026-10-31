using System.Text;
using SkiaSharp;

namespace NetReporter.Svg;

/// <summary>
/// Estado que <see cref="SvgPageWriter"/> reutiliza entre las páginas de un documento: el buffer
/// de salida, las fuentes y métricas (vía <see cref="SkiaPaintCache"/>), los atributos de fuente
/// ya formateados y, por imagen, sus dimensiones y su <c>data:</c> URI en base64, calculados una
/// sola vez aunque la imagen aparezca en todas las páginas (un logo en el PageHeader).
/// <para>Una instancia por documento y por hilo: no es thread-safe.</para>
/// </summary>
internal sealed class SvgWriterContext : IDisposable
{
    /// <summary>Dimensiones de una imagen y, cuando se incrusta, su data URI.</summary>
    internal sealed class ImageEntry(int width, int height, string mime)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public string Mime { get; } = mime;
        private string? _dataUri;

        /// <summary>Se construye la primera vez que una página la incrusta.</summary>
        public string DataUri(byte[] data) => _dataUri ??= string.Concat("data:", Mime, ";base64,", Convert.ToBase64String(data));
    }

    /// <summary>Atributos de fuente de un tramo, formateados y escapados una vez por (familia pedida, tipografía).</summary>
    internal sealed record FontAttributes(string FamilyAttribute, int Weight, bool Italic);

    private readonly Dictionary<byte[], ImageEntry?> _images = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(string Family, SKTypeface Typeface), FontAttributes> _fonts = [];

    public SvgWriterContext(Func<byte[], string, string?>? imageHref = null)
    {
        ImageHref = imageHref;
    }

    /// <summary>Fuentes y métricas compartidas con el PDF: lo que se mide es lo que se escribe.</summary>
    public SkiaPaintCache Paints { get; } = new();

    /// <summary>Buffer de la página en curso; conserva su capacidad entre páginas.</summary>
    public StringBuilder Buffer { get; } = new(16 * 1024);

    /// <summary>
    /// Dónde encontrar una imagen (bytes, mime) como URL; null incrusta un <c>data:</c> URI. El visor
    /// web lo usa para que el navegador descargue un logo repetido una sola vez.
    /// </summary>
    public Func<byte[], string, string?>? ImageHref { get; }

    /// <summary>Dimensiones de la imagen (leyendo solo su cabecera), o null si los bytes no son una imagen.</summary>
    public ImageEntry? Image(byte[] data, string? mimeHint)
    {
        if (_images.TryGetValue(data, out var entry)) return entry;
        var info = data.Length > 0 ? SKBitmap.DecodeBounds(data) : SKImageInfo.Empty;
        entry = info.Width > 0 && info.Height > 0 ? new ImageEntry(info.Width, info.Height, ImageMime.Resolve(data, mimeHint)) : null;
        _images[data] = entry;
        return entry;
    }

    /// <summary>Atributos de fuente para la familia pedida en el estilo y la tipografía que la resolvió (o la de respaldo).</summary>
    public FontAttributes Font(string requestedFamily, SKTypeface typeface)
    {
        var key = (requestedFamily, typeface);
        if (_fonts.TryGetValue(key, out var attrs)) return attrs;

        // Familia pedida, familia que realmente se dibujó y el genérico: el navegador usa la mejor
        // que tenga; como cada glifo lleva su x, la maquetación se conserva con cualquiera de ellas.
        var resolved = typeface.FamilyName;
        var generic = SkiaFonts.GenericFamily(requestedFamily);
        var sb = new StringBuilder(64);
        AppendFamily(sb, requestedFamily);
        if (!string.IsNullOrEmpty(resolved) && !string.Equals(resolved, requestedFamily, StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(", ");
            AppendFamily(sb, resolved);
        }
        sb.Append(", ").Append(generic);
        attrs = new FontAttributes(sb.ToString(), typeface.FontWeight, typeface.FontSlant != SKFontStyleSlant.Upright);
        _fonts[key] = attrs;
        return attrs;

        static void AppendFamily(StringBuilder sb, string family)
        {
            var quote = family.Any(c => !char.IsLetterOrDigit(c) && c != '-');
            if (quote) sb.Append('\'');
            foreach (var c in family)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("\\'"); break;
                    case < ' ': sb.Append(' '); break;
                    default: sb.Append(c); break;
                }
            }
            if (quote) sb.Append('\'');
        }
    }

    public void Dispose()
    {
        Paints.Dispose();
        _images.Clear();
        _fonts.Clear();
    }
}

/// <summary>Tipo MIME de una imagen: el declarado si es creíble, si no el que dicen sus primeros bytes.</summary>
internal static class ImageMime
{
    public static string Resolve(ReadOnlySpan<byte> data, string? hint)
    {
        var sniffed = Sniff(data);
        if (sniffed is not null) return sniffed;
        return !string.IsNullOrWhiteSpace(hint) && hint.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? hint.Trim().ToLowerInvariant()
            : "image/png";
    }

    public static string? Sniff(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G') return "image/png";
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        if (data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8') return "image/gif";
        if (data.Length >= 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
            && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P') return "image/webp";
        if (data.Length >= 2 && data[0] == 'B' && data[1] == 'M') return "image/bmp";
        return null;
    }
}
