using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;
using SkiaSharp;

namespace NetReporter.Svg;

/// <summary>
/// Geometría que comparten <see cref="SkiaPagePainter"/> (PDF) y <see cref="SvgPageWriter"/> (SVG):
/// dónde empieza un texto, su línea base y su recorte, el rectángulo destino de una imagen y los
/// radios reales de un rectángulo redondeado. Vive aquí para que los dos formatos salgan de los
/// mismos números por construcción, no por coincidencia.
/// </summary>
internal static class PageGeometry
{
    /// <summary>Margen vertical del recorte de un texto, en fracción del tamaño de fuente (ver <see cref="TextClip"/>).</summary>
    private const double VerticalSlackEm = 0.35;

    /// <summary>X del primer glifo según la alineación horizontal del estilo.</summary>
    public static float TextStartX(DrawTextCommand cmd, float textWidth) => cmd.Style.TextAlign switch
    {
        TextAlignment.Center => (float)(cmd.Bounds.X + (cmd.Bounds.Width - textWidth) / 2),
        TextAlignment.Right  => (float)(cmd.Bounds.Right - textWidth),
        _                    => (float)cmd.Bounds.X
    };

    /// <summary>
    /// Línea base de la (única) línea dentro de la caja: top + desplazamiento vertical + ascent.
    /// Center / Bottom la bajan por el alto sobrante; Top (el valor por defecto) la deja arriba.
    /// </summary>
    public static float TextBaseline(DrawTextCommand cmd, in SKFontMetrics metrics)
    {
        var lineHeight = metrics.Descent - metrics.Ascent;
        float topOffset = cmd.Style.VerticalAlign switch
        {
            VerticalAlignment.Center => (float)Math.Max(0, (cmd.Bounds.Height - lineHeight) / 2),
            VerticalAlignment.Bottom => (float)Math.Max(0, cmd.Bounds.Height - lineHeight),
            _                        => 0f
        };
        return (float)cmd.Bounds.Y + topOffset + (-metrics.Ascent);
    }

    /// <summary>
    /// Recorte del texto: exacto a lo ancho (el desborde no escapa la caja) y con ~0.35em de holgura
    /// en vertical, porque los acentos de las mayúsculas (Á, Í, Ó, Ñ…) sobresalen del ascent de la
    /// fuente y con un recorte exacto se perdían ("MARÍA" se veía como "MARIA").
    /// </summary>
    public static SKRect TextClip(DrawTextCommand cmd)
    {
        var verticalSlack = (float)(cmd.Style.FontSize * VerticalSlackEm);
        return new SKRect(
            (float)cmd.Bounds.X,
            (float)cmd.Bounds.Y - verticalSlack,
            (float)cmd.Bounds.Right,
            (float)cmd.Bounds.Bottom + verticalSlack);
    }

    /// <summary>
    /// Rectángulo donde se dibuja la imagen: la caja entera con <see cref="Core.Elements.ImageFit.Fill"/>;
    /// con Contain, la mayor caja con la proporción de la imagen, centrada.
    /// </summary>
    public static SKRect ImageTarget(DrawImageCommand cmd, int imageWidth, int imageHeight)
    {
        var dest = new SKRect(
            (float)cmd.Bounds.X,
            (float)cmd.Bounds.Y,
            (float)cmd.Bounds.Right,
            (float)cmd.Bounds.Bottom);
        if (cmd.Fit == Core.Elements.ImageFit.Fill || imageWidth <= 0 || imageHeight <= 0 || cmd.Bounds.Height <= 0)
            return dest;

        var imgRatio = (double)imageWidth / imageHeight;
        var dstRatio = cmd.Bounds.Width / cmd.Bounds.Height;
        if (imgRatio > dstRatio)
        {
            // Limitado por ancho
            var h = cmd.Bounds.Width / imgRatio;
            var top = cmd.Bounds.Y + (cmd.Bounds.Height - h) / 2;
            return new SKRect((float)cmd.Bounds.X, (float)top, (float)cmd.Bounds.Right, (float)(top + h));
        }
        else
        {
            var w = cmd.Bounds.Height * imgRatio;
            var left = cmd.Bounds.X + (cmd.Bounds.Width - w) / 2;
            return new SKRect((float)left, (float)cmd.Bounds.Y, (float)(left + w), (float)cmd.Bounds.Bottom);
        }
    }

    /// <summary>
    /// Radio de cada esquina (superior-izq, superior-der, inferior-der, inferior-izq, el orden de
    /// Skia) de un rectángulo redondeado: el radio pedido en las esquinas seleccionadas, 0 en las
    /// demás, y todos reducidos en la misma proporción cuando dos radios adyacentes no caben en un
    /// lado — la misma regla que aplica <c>SkRRect</c>. Devuelve false si no hay nada que redondear.
    /// </summary>
    public static bool RoundRectRadii(SKRect rect, float radius, RectCorners corners, Span<float> radii)
    {
        if (radii.Length < 4) throw new ArgumentException("Se necesitan 4 radios.", nameof(radii));
        if (!(radius > 0) || corners == RectCorners.None) return false;

        radii[0] = corners.HasFlag(RectCorners.TopLeft)     ? radius : 0;
        radii[1] = corners.HasFlag(RectCorners.TopRight)    ? radius : 0;
        radii[2] = corners.HasFlag(RectCorners.BottomRight) ? radius : 0;
        radii[3] = corners.HasFlag(RectCorners.BottomLeft)  ? radius : 0;

        var scale = 1f;
        scale = Limit(scale, rect.Width,  radii[0] + radii[1]);   // arriba
        scale = Limit(scale, rect.Height, radii[1] + radii[2]);   // derecha
        scale = Limit(scale, rect.Width,  radii[2] + radii[3]);   // abajo
        scale = Limit(scale, rect.Height, radii[3] + radii[0]);   // izquierda
        if (scale < 1)
            for (var i = 0; i < 4; i++) radii[i] *= scale;
        return true;

        static float Limit(float scale, float side, float sum) =>
            sum > side && sum > 0 ? Math.Min(scale, side / sum) : scale;
    }
}
