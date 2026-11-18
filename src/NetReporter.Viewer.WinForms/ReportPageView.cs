using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using NetReporter.Viewer.Desktop;
using SkiaSharp;

namespace NetReporter.Viewer.WinForms;

/// <summary>
/// Scrollable stack of report pages. Pages are painted in 1024 px tiles with the PDF's Skia painter
/// (<see cref="DesktopReportDocument.PaintTile"/>) straight into GDI+ bitmaps, cached by
/// <see cref="TileCache{T}"/> and blitted unscaled, so scrolling only paints tiles it has not seen at the
/// current zoom. Geometry (<see cref="PageStack"/>) and zoom maths (<see cref="ZoomLevels"/>) live in the
/// UI-agnostic core.
/// <para>
/// The view does not own its <see cref="Document"/>; <see cref="ReportViewer"/> disposes the ones it opens.
/// </para>
/// </summary>
[ToolboxItem(true)]
public class ReportPageView : ScrollableControl
{
    private const int PageGap = 12;
    private const int PagePadding = 16;
    private const int LineStep = 48;

    private readonly TileCache<Bitmap> _tiles = new();
    private DesktopReportDocument? _document;
    private PageStack? _stack;
    private ZoomMode _zoomMode = ZoomMode.PageWidth;
    private double _customZoom = 1;
    private double _zoom = 1;
    private int _currentPage;
    private bool _updating;
    private Point? _panStart;
    private Point _panScroll;

    public ReportPageView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        AutoScroll = true;
        BackColor = Color.FromArgb(0x52, 0x56, 0x5A);
        PageBorderColor = Color.FromArgb(0x30, 0x30, 0x30);
        TabStop = true;
    }

    /// <summary>Raised when <see cref="CurrentPage"/> changes (scrolling, navigation, new document).</summary>
    public event EventHandler? CurrentPageChanged;

    /// <summary>Raised when the effective <see cref="Zoom"/> or the <see cref="ZoomMode"/> changes.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>The document shown. The view does not dispose it.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DesktopReportDocument? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(_document, value)) return;
            _document = value;
            _tiles.Clear();
            _stack = null;
            UpdateGeometry(anchor: null);
            AutoScrollPosition = Point.Empty;
            Invalidate();
            RaiseCurrentPage(force: true);
        }
    }

    /// <summary>How the zoom is chosen. Fit modes follow the control's size.</summary>
    [DefaultValue(ZoomMode.PageWidth)]
    [Category("Behavior")]
    public ZoomMode ZoomMode
    {
        get => _zoomMode;
        set
        {
            if (_zoomMode == value) return;
            _zoomMode = value;
            UpdateGeometry(CenterAnchor());
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The effective zoom (1 = actual size). Setting it switches <see cref="ZoomMode"/> to
    /// <see cref="Desktop.ZoomMode.Custom"/>; the value is clamped to 10 %–400 %.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Zoom
    {
        get => _zoom;
        set => SetZoom(value, CenterAnchor());
    }

    /// <summary>Zero-based index of the page the reader is on.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CurrentPage => _currentPage;

    [Browsable(false)]
    public int PageCount => _document?.PageCount ?? 0;

    /// <summary>Color of the one-pixel frame drawn around each page.</summary>
    [Category("Appearance")]
    public Color PageBorderColor { get; set; }

    private bool ShouldSerializePageBorderColor() => PageBorderColor != Color.FromArgb(0x30, 0x30, 0x30);
    private void ResetPageBorderColor() => PageBorderColor = Color.FromArgb(0x30, 0x30, 0x30);
    private bool ShouldSerializeBackColor() => BackColor != Color.FromArgb(0x52, 0x56, 0x5A);
    public override void ResetBackColor() => BackColor = Color.FromArgb(0x52, 0x56, 0x5A);

    /// <summary>Scrolls so the page's top is at the top of the view.</summary>
    public void GoToPage(int pageIndex)
    {
        if (_stack is null || PageCount == 0) return;
        var index = Math.Clamp(pageIndex, 0, PageCount - 1);
        // The scroll range may stop short of the page's top near the end of the document, so the page is
        // made current explicitly instead of being derived from the clamped scroll position.
        AutoScrollPosition = new Point(-AutoScrollPosition.X, _stack.ScrollOffsetOf(index));
        Invalidate();
        if (index != _currentPage)
        {
            _currentPage = index;
            CurrentPageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ZoomIn() => SetZoom(ZoomLevels.ZoomIn(_zoom), CenterAnchor());

    public void ZoomOut() => SetZoom(ZoomLevels.ZoomOut(_zoom), CenterAnchor());

    // ---- geometry -------------------------------------------------------------------------------

    private void SetZoom(double zoom, ViewAnchor? anchor)
    {
        var clamped = ZoomLevels.Clamp(zoom);
        if (_zoomMode == ZoomMode.Custom && Math.Abs(clamped - _customZoom) < 1e-9) return;
        _customZoom = clamped;
        _zoomMode = ZoomMode.Custom;
        UpdateGeometry(anchor);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Recomputes zoom, page stack and scroll range; keeps <paramref name="anchor"/> under the same pixel.</summary>
    private void UpdateGeometry(ViewAnchor? anchor)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            if (_document is null || _document.PageCount == 0)
            {
                _stack = null;
                AutoScrollMinSize = Size.Empty;
                Invalidate();
                return;
            }

            // Setting AutoScrollMinSize can show or hide a scroll bar and change the client size; a second
            // pass settles fit modes against the final viewport.
            for (var pass = 0; pass < 2; pass++)
            {
                var client = ClientSize;
                double dpi = DeviceDpi;
                var zoom = ZoomLevels.Resolve(_zoomMode, _customZoom, _document.PageSize, client.Width, client.Height, dpi, PagePadding);
                var pixelsPerPoint = ZoomLevels.PixelsPerPoint(zoom, dpi);
                if (_stack is null || Math.Abs(_stack.PixelsPerPoint - pixelsPerPoint) > 1e-9)
                    _tiles.Clear();
                _zoom = zoom;
                _stack = new PageStack(_document.PageCount, _document.PageSize, pixelsPerPoint, client.Width, PageGap, PagePadding);
                var minSize = new Size(_stack.ContentWidth > client.Width ? _stack.ContentWidth : 0, _stack.ContentHeight);
                if (AutoScrollMinSize != minSize) AutoScrollMinSize = minSize;
                if (ClientSize == client) break;
            }

            if (anchor is { } a && _stack is { } stack)
            {
                var content = stack.ToContent(a.Point);
                ScrollTo(content.X - a.ViewX, content.Y - a.ViewY);
            }
            Invalidate();
        }
        finally
        {
            _updating = false;
        }
        RaiseCurrentPage(force: false);
    }

    /// <summary>The document spot under a viewport pixel, to restore after a zoom.</summary>
    private ViewAnchor? AnchorAt(Point viewport)
    {
        if (_stack is null) return null;
        var point = _stack.ToDocument(viewport.X - AutoScrollPosition.X, viewport.Y - AutoScrollPosition.Y);
        return new ViewAnchor(point, viewport.X, viewport.Y);
    }

    private ViewAnchor? CenterAnchor() => AnchorAt(new Point(ClientSize.Width / 2, ClientSize.Height / 2));

    private void ScrollTo(int x, int y)
    {
        // AutoScrollPosition takes positive offsets and clamps them to the scroll range.
        AutoScrollPosition = new Point(Math.Max(0, x), Math.Max(0, y));
        Invalidate();
        RaiseCurrentPage(force: false);
    }

    private void RaiseCurrentPage(bool force)
    {
        var page = _stack?.CurrentPage(-AutoScrollPosition.Y, ClientSize.Height) ?? 0;
        if (!force && page == _currentPage) return;
        _currentPage = page;
        CurrentPageChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- painting -------------------------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_document is null || _stack is null) return;

        var scroll = AutoScrollPosition;           // negative offsets
        var clip = e.ClipRectangle;
        var (first, last) = _stack.VisiblePages(clip.Top - scroll.Y, clip.Height);
        if (first > last) return;

        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;      // 1:1 pixel mapping for the tile blits
        using var border = new Pen(PageBorderColor);
        using var shadow = new SolidBrush(Color.FromArgb(0x3A, 0x3D, 0x40));

        for (var page = first; page <= last; page++)
        {
            var bounds = _stack.PageBounds(page);
            var screen = new Rectangle(bounds.Left + scroll.X, bounds.Top + scroll.Y, bounds.Width, bounds.Height);
            g.FillRectangle(shadow, screen.X + 3, screen.Y + 3, screen.Width, screen.Height);

            // Region of the page (page pixels) that the clip rectangle exposes.
            var exposed = Rectangle.Intersect(screen, clip);
            if (!exposed.IsEmpty)
            {
                var region = SKRectI.Create(exposed.X - screen.X, exposed.Y - screen.Y, exposed.Width, exposed.Height);
                foreach (var tile in Tiles.Covering(page, _stack.PixelsPerPoint, bounds.Width, bounds.Height, region))
                {
                    var bitmap = Tile(tile);
                    if (bitmap is null) continue;
                    // Explicit size in pixels: DrawImageUnscaled would scale by the bitmap's dpi metadata.
                    g.DrawImage(bitmap,
                        new Rectangle(screen.X + tile.Bounds.Left, screen.Y + tile.Bounds.Top, tile.Bounds.Width, tile.Bounds.Height),
                        0, 0, tile.Bounds.Width, tile.Bounds.Height, GraphicsUnit.Pixel);
                }
            }
            g.CompositingMode = CompositingMode.SourceOver;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.DrawRectangle(border, screen.X - 1, screen.Y - 1, screen.Width + 1, screen.Height + 1);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.PixelOffsetMode = PixelOffsetMode.Half;
        }
    }

    private Bitmap? Tile(PageTile tile)
    {
        if (_tiles.TryGet(tile.Key, out var cached)) return cached;
        var document = _document;
        if (document is null) return null;

        var bitmap = TileBitmaps.Render(document, tile.Key.Page, tile.Key.PixelsPerPoint, tile.Bounds);
        if (bitmap is null) return null;
        _tiles.Add(tile.Key, bitmap, 4L * tile.Bounds.Width * tile.Bounds.Height);
        return bitmap;
    }

    // ---- input ----------------------------------------------------------------------------------

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) != 0 && _document is not null)
        {
            var anchor = AnchorAt(e.Location);
            SetZoom(e.Delta > 0 ? ZoomLevels.ZoomIn(_zoom) : ZoomLevels.ZoomOut(_zoom), anchor);
            if (e is HandledMouseEventArgs handled) handled.Handled = true;
            return;
        }
        base.OnMouseWheel(e);
        Invalidate();
        RaiseCurrentPage(force: false);
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
        RaiseCurrentPage(force: false);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left && _stack is not null)
        {
            _panStart = e.Location;
            _panScroll = new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y);
            Cursor = Cursors.SizeAll;
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panStart is { } start)
            ScrollTo(_panScroll.X - (e.X - start.X), _panScroll.Y - (e.Y - start.Y));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndPan();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) EndPan();
    }

    private void EndPan()
    {
        if (_panStart is null) return;
        _panStart = null;
        Cursor = Cursors.Default;
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.PageUp or Keys.PageDown
            or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_stack is null) return;
        var x = -AutoScrollPosition.X;
        var y = -AutoScrollPosition.Y;
        var page = ClientSize.Height - LineStep;
        switch (e.KeyCode)
        {
            case Keys.Up: ScrollTo(x, y - LineStep); break;
            case Keys.Down: ScrollTo(x, y + LineStep); break;
            case Keys.Left: ScrollTo(x - LineStep, y); break;
            case Keys.Right: ScrollTo(x + LineStep, y); break;
            case Keys.PageUp: ScrollTo(x, y - page); break;
            case Keys.PageDown: ScrollTo(x, y + page); break;
            case Keys.Home when e.Control: GoToPage(0); break;
            case Keys.End when e.Control: GoToPage(PageCount - 1); break;
            case Keys.Home: GoToPage(_currentPage); break;
            case Keys.End: GoToPage(PageCount - 1); break;
            case Keys.Oemplus or Keys.Add when e.Control: ZoomIn(); break;
            case Keys.OemMinus or Keys.Subtract when e.Control: ZoomOut(); break;
            case Keys.D0 or Keys.NumPad0 when e.Control: Zoom = 1; break;
            default: return;
        }
        e.Handled = true;
    }

    // ---- size / dpi -----------------------------------------------------------------------------

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        UpdateGeometry(_zoomMode == ZoomMode.Custom ? null : TopAnchor());
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateGeometry(TopAnchor());
    }

    /// <summary>Keeps the spot at the top-left of the viewport when the scale changes because of a resize.</summary>
    private ViewAnchor? TopAnchor() => AnchorAt(new Point(0, 0));

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tiles.Dispose();
        base.Dispose(disposing);
    }

    private readonly record struct ViewAnchor(DocumentPoint Point, int ViewX, int ViewY);
}

/// <summary>Paints document tiles into GDI+ bitmaps that share their pixel buffer with a Skia surface.</summary>
internal static class TileBitmaps
{
    /// <summary>A premultiplied BGRA bitmap of the tile (GDI+'s fastest format to blit), or null if it can't be created.</summary>
    public static Bitmap? Render(DesktopReportDocument document, int page, double pixelsPerPoint, SKRectI tile)
    {
        if (tile.Width <= 0 || tile.Height <= 0) return null;
        var bitmap = new Bitmap(tile.Width, tile.Height, PixelFormat.Format32bppPArgb);
        try
        {
            Paint(bitmap, document, page, pixelsPerPoint, tile);
            return bitmap;
        }
        catch (ObjectDisposedException)
        {
            bitmap.Dispose();
            return null;    // the document was replaced while painting
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Paints <paramref name="tile"/> into <paramref name="bitmap"/> (Format32bppPArgb, same size).</summary>
    public static void Paint(Bitmap bitmap, DesktopReportDocument document, int page, double pixelsPerPoint, SKRectI tile)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, data.Scan0, data.Stride)
                ?? throw new InvalidOperationException("Could not create a Skia surface over the bitmap.");
            document.PaintTile(surface.Canvas, page, pixelsPerPoint, tile);
            surface.Canvas.Flush();
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
