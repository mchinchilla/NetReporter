using System.Text;
using NetReporter.Barcodes;
using NetReporter.Core.Definition;
using NetReporter.Core.Layout;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Html;
using NetReporter.Pdf;
using NetReporter.Svg;
using NetReporter.Xlsx;
using SkiaSharp;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Viewer.Desktop;

/// <summary>
/// A report opened in a desktop viewer: laid out <b>once</b> (<see cref="RenderList"/>), painted on demand
/// with <see cref="SkiaPagePainter"/> — the same painter as the PDF, so the screen shows exactly what the
/// PDF will contain — and exported from the same layout.
/// <para>
/// Owns one <see cref="SkiaPaintCache"/> (fonts, decoded images, paints) for the whole document. Painting
/// is serialized by a lock, so tiles can be painted from a background thread; exports don't touch the
/// cache and can run concurrently. Dispose the document when the viewer closes it.
/// </para>
/// </summary>
public sealed class DesktopReportDocument : IDisposable
{
    /// <summary>
    /// Extra margin (points) around a tile when deciding which commands can reach it, on top of each
    /// command's own reach (stroke width; for text, the painter's clip <c>PageGeometry.TextClip</c>).
    /// </summary>
    internal const double CullSlack = 8;

    private readonly Lock _paintLock = new();
    private readonly SkiaPaintCache _cache = new();
    private readonly DesktopReportOptions _options;
    private bool _disposed;

    /// <summary>Wraps an already laid-out report. Use <see cref="CreateAsync"/> to lay it out off the UI thread.</summary>
    public DesktopReportDocument(ReportDefinition report, RlRenderList renderList, DesktopReportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(renderList);
        Report = report;
        RenderList = renderList;
        _options = options ?? new DesktopReportOptions();
    }

    /// <summary>Lays the report out on the thread pool and returns the document.</summary>
    public static Task<DesktopReportDocument> CreateAsync(
        ReportDefinition report, DesktopReportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        return Task.Run(() => Create(report, options, cancellationToken), cancellationToken);
    }

    /// <summary>Lays the report out on the calling thread and returns the document.</summary>
    public static DesktopReportDocument Create(
        ReportDefinition report, DesktopReportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        options ??= new DesktopReportOptions();
        var engine = new LayoutEngine(
            options.TextMeasurer ?? SkiaTextMeasurer.Instance,
            options.BarcodeGenerator ?? ZXingBarcodeGenerator.Instance);
        return new DesktopReportDocument(report, engine.Layout(report, cancellationToken), options);
    }

    public ReportDefinition Report { get; }

    /// <summary>The layout. Page size comes from <c>RenderList.Page</c> (continuous rolls carry their computed height there).</summary>
    public RlRenderList RenderList { get; }

    public int PageCount => RenderList.Pages.Count;

    /// <summary>Page size in points (1/72 in), shared by every page.</summary>
    public Size PageSize => RenderList.Page.Size;

    /// <summary>Title for the window or tab: the report's title, else its name.</summary>
    public string Title => string.IsNullOrWhiteSpace(Report.Title) ? Report.Name : Report.Title;

    /// <summary>
    /// Paints the part <paramref name="tile"/> (page pixels at <paramref name="pixelsPerPoint"/>) of a page
    /// onto <paramref name="canvas"/>, whose origin is the tile's top-left corner. The tile is filled white first.
    /// </summary>
    public void PaintTile(SKCanvas canvas, int pageIndex, double pixelsPerPoint, SKRectI tile) =>
        PaintTile(canvas, pageIndex, pixelsPerPoint, tile, cull: true);

    // cull: false paints every command (tests compare both paths pixel by pixel).
    internal void PaintTile(SKCanvas canvas, int pageIndex, double pixelsPerPoint, SKRectI tile, bool cull)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelsPerPoint);
        var page = Page(pageIndex);

        // Tile in page points, grown by the slack: only commands that can reach it are painted.
        var left = tile.Left / pixelsPerPoint - CullSlack;
        var top = tile.Top / pixelsPerPoint - CullSlack;
        var right = tile.Right / pixelsPerPoint + CullSlack;
        var bottom = tile.Bottom / pixelsPerPoint + CullSlack;
        var visible = cull ? Cull(page, left, top, right, bottom) : page;

        var save = canvas.Save();
        try
        {
            canvas.ClipRect(SKRect.Create(tile.Width, tile.Height));
            canvas.Clear(SKColors.White);
            canvas.Translate(-tile.Left, -tile.Top);
            canvas.Scale((float)pixelsPerPoint);
            lock (_paintLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                SkiaPagePainter.Draw(canvas, visible, _cache);
            }
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    /// <summary>Paints a tile into a new BGRA premultiplied bitmap of the tile's size. The caller owns it.</summary>
    public SKBitmap RenderTile(int pageIndex, double pixelsPerPoint, SKRectI tile) =>
        RenderTile(pageIndex, pixelsPerPoint, tile, cull: true);

    internal SKBitmap RenderTile(int pageIndex, double pixelsPerPoint, SKRectI tile, bool cull)
    {
        if (tile.Width <= 0 || tile.Height <= 0)
            throw new ArgumentException("The tile must not be empty.", nameof(tile));
        var bitmap = new SKBitmap(new SKImageInfo(tile.Width, tile.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        try
        {
            using var canvas = new SKCanvas(bitmap);
            PaintTile(canvas, pageIndex, pixelsPerPoint, tile, cull);
            canvas.Flush();
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Renders the document to <paramref name="output"/> in <paramref name="format"/>.</summary>
    public void Export(ExportFormat format, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Write(Export(format, cancellationToken));
    }

    /// <summary>Renders the document in <paramref name="format"/>.</summary>
    public byte[] Export(ExportFormat format, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return format switch
        {
            ExportFormat.Pdf => new PdfRenderer(_options.PdfOptions).Render(RenderList, cancellationToken),
            ExportFormat.Html => Encoding.UTF8.GetBytes(new HtmlRenderer().Render(
                RenderList, new HtmlRenderOptions { Title = Title }, cancellationToken)),
            ExportFormat.Xlsx => new XlsxRenderer().Render(Report, _options.XlsxOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
    }

    /// <summary>Renders on the thread pool and writes the bytes to <paramref name="output"/>.</summary>
    public async Task ExportAsync(ExportFormat format, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var bytes = await Task.Run(() => Export(format, cancellationToken), cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>File name for a save dialog: resolved <c>fileName:</c> → title → name, sanitized, plus the extension.</summary>
    public string SuggestedFileName(ExportFormat format) => $"{FileBaseName(Report)}.{format.Extension()}";

    /// <summary>Same rule as the web viewer's download name.</summary>
    internal static string FileBaseName(ReportDefinition report)
    {
        var raw = !string.IsNullOrWhiteSpace(report.FileName) ? report.FileName
                : !string.IsNullOrWhiteSpace(report.Title) ? report.Title
                : report.Name;
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw.Trim())
            sb.Append(Array.IndexOf(invalid, c) >= 0 || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c);
        var name = sb.ToString().Trim('.', ' ');
        return name.Length == 0 ? "report" : name;
    }

    public void Dispose()
    {
        lock (_paintLock)
        {
            if (_disposed) return;
            _disposed = true;
            _cache.Dispose();
        }
    }

    private RenderPage Page(int pageIndex)
    {
        if ((uint)pageIndex >= (uint)RenderList.Pages.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"The document has {RenderList.Pages.Count} page(s).");
        return RenderList.Pages[pageIndex];
    }

    /// <summary>The page's commands that intersect the rectangle (points), in their original order.</summary>
    private static RenderPage Cull(RenderPage page, double left, double top, double right, double bottom)
    {
        List<RenderCommand>? kept = null;
        var commands = page.Commands;
        for (var i = 0; i < commands.Count; i++)
        {
            var cmd = commands[i];
            bool hit;
            if (cmd is DrawPathCommand)
            {
                hit = true;     // never culled: their bounds may be set by hand and charts are cheap to paint
            }
            else if (cmd is DrawTextCommand text)
            {
                // The painter clips text to TextClip (bounds + a vertical slack that grows with the font size).
                var clip = PageGeometry.TextClip(text);
                hit = clip.Left <= right && clip.Right >= left && clip.Top <= bottom && clip.Bottom >= top;
            }
            else
            {
                var b = cmd.Bounds;
                var reach = cmd switch
                {
                    DrawLineCommand line => line.Thickness,
                    DrawRectangleCommand rect => rect.Border?.Thickness ?? 0,
                    _ => 0,
                };
                hit = b.X - reach <= right && b.Right + reach >= left && b.Y - reach <= bottom && b.Bottom + reach >= top;
            }
            if (hit)
            {
                kept?.Add(cmd);
                continue;
            }
            if (kept is null)
            {
                kept = new List<RenderCommand>(commands.Count);
                for (var j = 0; j < i; j++) kept.Add(commands[j]);
            }
        }
        return kept is null ? page : page with { Commands = kept };
    }
}
