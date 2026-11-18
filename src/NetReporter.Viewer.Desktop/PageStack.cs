using NetReporter.Core.Primitives;
using SkiaSharp;

namespace NetReporter.Viewer.Desktop;

/// <summary>
/// Geometry of a vertical stack of equally sized pages in device pixels: where each page sits inside the
/// scrollable content, which pages a viewport shows and which one is "current". Pure maths, no UI, so
/// every desktop host (WinForms, WPF, Avalonia, MAUI) scrolls and hit-tests the same way.
/// </summary>
public sealed class PageStack
{
    /// <param name="pageCount">Number of pages (0 = empty document).</param>
    /// <param name="pageSize">Page size in points, from <c>RenderList.Page</c>.</param>
    /// <param name="pixelsPerPoint">Zoom × device dpi / 72.</param>
    /// <param name="viewportWidth">Width of the visible area; narrower pages are centered in it.</param>
    /// <param name="gap">Vertical space between pages, in pixels.</param>
    /// <param name="padding">Space around the stack, in pixels.</param>
    public PageStack(int pageCount, Size pageSize, double pixelsPerPoint, int viewportWidth, int gap = 12, int padding = 16)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelsPerPoint);
        ArgumentOutOfRangeException.ThrowIfNegative(gap);
        ArgumentOutOfRangeException.ThrowIfNegative(padding);

        PageCount = pageCount;
        PixelsPerPoint = pixelsPerPoint;
        Gap = gap;
        Padding = padding;
        (PageWidth, PageHeight) = PixelSize(pageSize, pixelsPerPoint);
        ContentWidth = Math.Max(Math.Max(0, viewportWidth), PageWidth + 2 * padding);
        ContentHeight = pageCount == 0 ? 0 : (int)Math.Min(int.MaxValue,
            2L * padding + (long)pageCount * PageHeight + (long)(pageCount - 1) * gap);
    }

    public int PageCount { get; }
    public double PixelsPerPoint { get; }
    public int Gap { get; }
    public int Padding { get; }

    /// <summary>Page width in pixels.</summary>
    public int PageWidth { get; }

    /// <summary>Page height in pixels.</summary>
    public int PageHeight { get; }

    /// <summary>Scrollable width: the viewport, or the page plus padding when that is wider.</summary>
    public int ContentWidth { get; }

    /// <summary>Scrollable height.</summary>
    public int ContentHeight { get; }

    /// <summary>Distance between the tops of two consecutive pages.</summary>
    public int Pitch => PageHeight + Gap;

    /// <summary>Page size in whole pixels (rounded up, at least 1 × 1).</summary>
    public static (int Width, int Height) PixelSize(Size pageSize, double pixelsPerPoint) =>
        (Math.Max(1, (int)Math.Ceiling(pageSize.Width * pixelsPerPoint - 1e-6)),
         Math.Max(1, (int)Math.Ceiling(pageSize.Height * pixelsPerPoint - 1e-6)));

    /// <summary>Page rectangle in content coordinates (before subtracting the scroll offset).</summary>
    public SKRectI PageBounds(int pageIndex)
    {
        if ((uint)pageIndex >= (uint)PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, null);
        var left = (ContentWidth - PageWidth) / 2;
        var top = Padding + pageIndex * Pitch;
        return SKRectI.Create(left, top, PageWidth, PageHeight);
    }

    /// <summary>
    /// Pages intersecting the vertical band [<paramref name="top"/>, <paramref name="top"/> + <paramref name="height"/>)
    /// of the content. <c>(0, -1)</c> when no page is visible (only gaps or padding, or outside the document).
    /// </summary>
    public (int First, int Last) VisiblePages(int top, int height)
    {
        if (PageCount == 0 || height <= 0) return (0, -1);
        var bottom = (long)top + height;               // exclusive
        // First page whose bottom edge is below the band's top.
        var first = (int)Math.Max(0, FloorDiv((long)top - Padding - PageHeight, Pitch) + 1);
        // Last page whose top edge is above the band's bottom.
        var last = (int)Math.Min(PageCount - 1, FloorDiv(bottom - 1 - Padding, Pitch));
        return first <= last ? (first, last) : (0, -1);
    }

    /// <summary>
    /// The page a reader is on: the one under a point a third of the way down the viewport (the nearest
    /// page when that point falls in a gap) — except that the top of the content is the first page and a
    /// viewport scrolled to the end of a scrollable stack is on the last page, which the "third of the way down" point may never reach when the last
    /// pages fit in the viewport. Hosts should still set the current page explicitly on page navigation,
    /// because a short scroll range can't scroll every page to the top.
    /// </summary>
    public int CurrentPage(int top, int height)
    {
        if (PageCount == 0) return 0;
        if (top <= 0) return 0;
        var max = MaxScrollOffset(height);
        if (max > 0 && top >= max) return PageCount - 1;
        return PageAt(top + Math.Max(0, height) / 3);
    }

    /// <summary>Largest vertical scroll offset for a viewport of <paramref name="height"/> pixels.</summary>
    public int MaxScrollOffset(int height) => Math.Max(0, ContentHeight - Math.Max(0, height));

    /// <summary>The page at content y, or the nearest page when y falls in a gap or the padding.</summary>
    public int PageAt(int y)
    {
        if (PageCount == 0) return 0;
        var index = FloorDiv((long)y - Padding + Gap / 2, Pitch);
        return (int)Math.Clamp(index, 0, PageCount - 1);
    }

    /// <summary>
    /// Scroll offset that puts the top of the page at the top of the viewport (with half the padding showing).
    /// Not clamped: the host's scroll range may stop short of it near the end of the document.
    /// </summary>
    public int ScrollOffsetOf(int pageIndex)
    {
        if (PageCount == 0) return 0;
        var index = Math.Clamp(pageIndex, 0, PageCount - 1);
        return Math.Max(0, Padding / 2 + index * Pitch);
    }

    /// <summary>
    /// Converts a content pixel to a document position: the page at that y (nearest page in gaps) and the
    /// point inside it, in points. Use it with <see cref="ToContent"/> to keep a spot of the document under
    /// the same pixel across a zoom or resize.
    /// </summary>
    public DocumentPoint ToDocument(int x, int y)
    {
        if (PageCount == 0) return new DocumentPoint(0, 0, 0);
        var page = PageAt(y);
        var bounds = PageBounds(page);
        return new DocumentPoint(page, (x - bounds.Left) / PixelsPerPoint, (y - bounds.Top) / PixelsPerPoint);
    }

    /// <summary>The content pixel of a document position (inverse of <see cref="ToDocument"/>).</summary>
    public SKPointI ToContent(DocumentPoint point)
    {
        if (PageCount == 0) return SKPointI.Empty;
        var bounds = PageBounds(Math.Clamp(point.Page, 0, PageCount - 1));
        return new SKPointI(
            (int)Math.Round(bounds.Left + point.X * PixelsPerPoint),
            (int)Math.Round(bounds.Top + point.Y * PixelsPerPoint));
    }

    private static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);
}

/// <summary>A position in the document: page index and a point inside it, in points from the page's top-left corner.</summary>
public readonly record struct DocumentPoint(int Page, double X, double Y);
