using System.Buffers;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Svg;
using SkiaSharp;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.EscPos;

/// <summary>
/// Renderer ESC/POS para impresoras térmicas de tickets. Cada página maquetada se pinta con
/// <see cref="SkiaPagePainter"/> (el mismo pintor del PDF, así que fuentes, códigos de barras, QR,
/// logos y líneas salen idénticos) en un bitmap de 8 bits a la resolución de la impresora, se
/// convierte a puntos negro/blanco y se envía como bloques raster <c>GS v 0</c>, seguidos de la
/// alimentación, el corte y, opcionalmente, la apertura de la gaveta. Lo natural es combinarlo con
/// <see cref="PageSetup.Roll80"/> / <see cref="PageSetup.Roll58"/> (papel continuo); una página de
/// tamaño fijo también se imprime, escalada al ancho imprimible si no cabe.
/// </summary>
public sealed class EscPosRenderer
{
    private readonly EscPosRendererOptions _options;

    public EscPosRenderer() : this(new EscPosRendererOptions()) { }

    /// <summary>Las opciones se leen en cada <c>Render</c>, así que pueden ajustarse en tiempo de ejecución.</summary>
    public EscPosRenderer(EscPosRendererOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    public EscPosRendererOptions Options => _options;

    public byte[] Render(RlRenderList renderList) => Render(renderList, CancellationToken.None);

    /// <summary>
    /// Bytes listos para enviar a la impresora. Aborta con <see cref="OperationCanceledException"/>
    /// al cancelar <paramref name="cancellationToken"/> (se revisa por página y por bloque raster).
    /// </summary>
    public byte[] Render(RlRenderList renderList, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(renderList);
        using var output = new MemoryStream(EstimateSize(renderList));
        Render(renderList, output, cancellationToken);
        return output.ToArray();
    }

    /// <summary>Escribe los bytes ESC/POS en <paramref name="output"/> (un <c>NetworkStream</c>, un archivo, un puerto serie…).</summary>
    public void Render(RlRenderList renderList, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(renderList);
        ArgumentNullException.ThrowIfNull(output);
        _options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var geometry = GeometryFor(renderList.Page);
        var bytesPerRow = _options.DotsPerLine / 8;

        if (_options.Initialize) output.Write(EscPosCommands.Initialize);
        if (_options.Prologue is { Length: > 0 } prologue) output.Write(prologue);
        if (_options.OpenCashDrawer) EscPosCommands.WriteDrawerKick(output, _options.CashDrawerPin);

        // Fuentes, imágenes y pinturas compartidas por todas las páginas (regla: un cache por documento).
        using var cache = new SkiaPaintCache();
        var packed = ArrayPool<byte>.Shared.Rent(bytesPerRow * _options.BandRows);
        try
        {
            // Las copias se rasterizan una vez y se reescriben: el bloque raster de cada página se
            // recuerda la primera vez (un ticket pesa decenas de KB; copiarlo es más barato que pintarlo).
            var copies = _options.Copies;
            var remembered = copies > 1 ? new MemoryStream[renderList.Pages.Count] : null;
            for (var copy = 0; copy < copies; copy++)
            {
                for (var i = 0; i < renderList.Pages.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (remembered is not null && copy > 0)
                    {
                        remembered[i]!.WriteTo(output);
                    }
                    else
                    {
                        var target = remembered is null ? output : remembered[i] = new MemoryStream();
                        using var bitmap = Rasterize(renderList.Pages[i], geometry, cache);
                        WriteRaster(target, bitmap, bytesPerRow, packed, cancellationToken);
                        if (remembered is not null) remembered[i]!.WriteTo(output);
                    }

                    var last = i == renderList.Pages.Count - 1;
                    if (_options.CutAfterEachPage || last)
                    {
                        EscPosCommands.WriteFeedLines(output, _options.FeedLinesBeforeCut);
                        EscPosCommands.WriteCut(output, _options.Cut);
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packed);
        }

        if (_options.Epilogue is { Length: > 0 } epilogue) output.Write(epilogue);
        output.Flush();
    }

    /// <summary>Escala (puntos por pt), desplazamiento horizontal y alto en puntos con los que se pinta una página.</summary>
    internal readonly record struct RasterGeometry(float Scale, float OffsetX, int HeightDots);

    internal RasterGeometry GeometryFor(PageSetup page)
    {
        var dotsPerPoint = _options.Dpi / 72.0;
        var fit = _options.PageFit;
        if (fit == EscPosPageFit.Auto)
            fit = page.ContentWidth * dotsPerPoint <= _options.DotsPerLine + 0.5 ? EscPosPageFit.Crop : EscPosPageFit.Scale;

        var scale = fit == EscPosPageFit.Scale ? _options.DotsPerLine / page.Size.Width : dotsPerPoint;
        var offsetX = fit == EscPosPageFit.Scale ? 0 : (_options.DotsPerLine - page.Size.Width * dotsPerPoint) / 2;
        var height = Math.Max(1, (int)Math.Ceiling(page.Size.Height * scale - 1e-6));
        return new RasterGeometry((float)scale, (float)offsetX, height);
    }

    /// <summary>Pinta la página en un bitmap gris de 8 bits del ancho imprimible (blanco = papel).</summary>
    internal SKBitmap Rasterize(RenderPage page, RasterGeometry geometry, SkiaPaintCache cache)
    {
        var bitmap = new SKBitmap(new SKImageInfo(_options.DotsPerLine, geometry.HeightDots, SKColorType.Gray8, SKAlphaType.Opaque));
        try
        {
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            canvas.Translate(geometry.OffsetX, 0);
            canvas.Scale(geometry.Scale);
            SkiaPagePainter.Draw(canvas, page, cache);
            canvas.Flush();
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private void WriteRaster(Stream output, SKBitmap bitmap, int bytesPerRow, byte[] packed, CancellationToken cancellationToken)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var rowBytes = bitmap.RowBytes;
        var pixels = bitmap.GetPixelSpan();
        var threshold = _options.Threshold;
        var dither = _options.Dithering == EscPosDithering.FloydSteinberg;
        var errorCurrent = dither ? new int[width + 2] : null;
        var errorNext = dither ? new int[width + 2] : null;

        for (var y0 = 0; y0 < height; y0 += _options.BandRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = Math.Min(_options.BandRows, height - y0);
            var block = packed.AsSpan(0, rows * bytesPerRow);
            block.Clear();
            for (var r = 0; r < rows; r++)
            {
                var source = pixels.Slice((y0 + r) * rowBytes, width);
                var line = block.Slice(r * bytesPerRow, bytesPerRow);
                if (dither) DitherRow(source, line, ref errorCurrent!, ref errorNext!, threshold);
                else PackRow(source, line, threshold);
            }
            EscPosCommands.WriteRasterBlock(output, bytesPerRow, rows, block);
        }
    }

    /// <summary>Umbral fijo: gris &lt; threshold → punto negro (bit 1, MSB primero).</summary>
    private static void PackRow(ReadOnlySpan<byte> source, Span<byte> line, int threshold)
    {
        for (var x = 0; x < source.Length; x++)
            if (source[x] < threshold) line[x >> 3] |= (byte)(0x80 >> (x & 7));
    }

    /// <summary>Floyd–Steinberg: el error de cada punto se reparte 7/16 a la derecha y 3/16, 5/16, 1/16 a la fila siguiente.</summary>
    private static void DitherRow(ReadOnlySpan<byte> source, Span<byte> line, ref int[] errorCurrent, ref int[] errorNext, int threshold)
    {
        Array.Clear(errorNext);
        for (var x = 0; x < source.Length; x++)
        {
            var value = source[x] + errorCurrent[x + 1];
            var black = value < threshold;
            if (black) line[x >> 3] |= (byte)(0x80 >> (x & 7));
            var error = value - (black ? 0 : 255);
            errorCurrent[x + 2] += error * 7 / 16;
            errorNext[x] += error * 3 / 16;
            errorNext[x + 1] += error * 5 / 16;
            errorNext[x + 2] += error / 16;
        }
        (errorCurrent, errorNext) = (errorNext, errorCurrent);
    }

    private int EstimateSize(RlRenderList renderList)
    {
        var geometry = GeometryFor(renderList.Page);
        var perPage = (long)(_options.DotsPerLine / 8) * geometry.HeightDots + 64;
        return (int)Math.Min(perPage * Math.Max(1, renderList.Pages.Count) * _options.Copies + 64, 16 * 1024 * 1024);
    }
}
