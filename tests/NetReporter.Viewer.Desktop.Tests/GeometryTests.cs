using NetReporter.Core.Primitives;
using SkiaSharp;

namespace NetReporter.Viewer.Desktop.Tests;

public sealed class PageStackTests
{
    private static readonly Size Letter = PageSetup.Letter.Size;   // 612 × 792 pt

    [Fact]
    public void Pages_AreCenteredAndStackedWithGaps()
    {
        var stack = new PageStack(3, Letter, 1, viewportWidth: 1000, gap: 12, padding: 16);

        Assert.Equal(612, stack.PageWidth);
        Assert.Equal(792, stack.PageHeight);
        Assert.Equal(1000, stack.ContentWidth);
        Assert.Equal(16 + 3 * 792 + 2 * 12 + 16, stack.ContentHeight);
        Assert.Equal(SKRectI.Create(194, 16, 612, 792), stack.PageBounds(0));
        Assert.Equal(SKRectI.Create(194, 16 + 804, 612, 792), stack.PageBounds(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => stack.PageBounds(3));
    }

    [Fact]
    public void ContentWidth_GrowsWhenThePageIsWiderThanTheViewport()
    {
        var stack = new PageStack(1, Letter, 2, viewportWidth: 500, padding: 16);

        Assert.Equal(1224, stack.PageWidth);
        Assert.Equal(1224 + 32, stack.ContentWidth);
        Assert.Equal(16, stack.PageBounds(0).Left);
    }

    [Fact]
    public void PixelSize_RoundsUp()
    {
        Assert.Equal((794, 1123), PageStack.PixelSize(PageSetup.A4.Size, 96 / 72.0));
        Assert.Equal((1, 1), PageStack.PixelSize(new Size(0, 0), 1));
    }

    [Theory]
    [InlineData(0, 16, 0, -1)]        // solo el padding superior
    [InlineData(0, 17, 0, 0)]         // primer píxel de la página 1
    [InlineData(800, 30, 0, 1)]       // final de la página 1 + inicio de la 2
    [InlineData(808, 12, 0, -1)]       // solo el hueco entre páginas
    [InlineData(808, 13, 1, 1)]
    [InlineData(1500, 2000, 1, 2)]    // se recorta a la última página
    [InlineData(5000, 100, 0, -1)]     // por debajo del documento
    public void VisiblePages_ReturnsTheIntersectingRange(int top, int height, int first, int last)
    {
        var stack = new PageStack(3, Letter, 1, 1000, gap: 12, padding: 16);
        Assert.Equal((first, last), stack.VisiblePages(top, height));
    }

    [Fact]
    public void ScrollOffsetOf_MakesThatPageCurrent()
    {
        var stack = new PageStack(5, Letter, 1.3, 1000);
        for (var i = 0; i < 5; i++)
            Assert.Equal(i, stack.CurrentPage(stack.ScrollOffsetOf(i), 600));
    }

    [Theory]
    [InlineData(5, 0.25, 750)]     // 5 páginas Letter al 25 % en 750 px: casi todo cabe
    [InlineData(5, 1.0, 750)]
    [InlineData(40, 0.5, 900)]
    public void ScrolledToTheEnd_IsOnTheLastPage(int pages, double zoom, int viewport)
    {
        var stack = new PageStack(pages, Letter, zoom * 96 / 72, 1000);
        var max = stack.MaxScrollOffset(viewport);
        Assert.True(max > 0);
        Assert.Equal(pages - 1, stack.CurrentPage(max, viewport));
        Assert.Equal(pages - 1, stack.CurrentPage(max + 50, viewport));
        Assert.Equal(0, stack.CurrentPage(0, viewport));
        // Every page's clamped scroll target is reachable and the pages before the end still map to themselves.
        for (var i = 0; i < pages; i++)
            if (stack.ScrollOffsetOf(i) < max)
                Assert.Equal(i, stack.CurrentPage(stack.ScrollOffsetOf(i), viewport));
    }

    [Fact]
    public void EverythingFits_StaysOnTheFirstPageWithoutScrolling()
    {
        var stack = new PageStack(2, Letter, 0.25 * 96 / 72, 1000);
        Assert.Equal(0, stack.MaxScrollOffset(2000));
        Assert.Equal(0, stack.CurrentPage(0, 2000));
    }

    [Fact]
    public void PageAt_SnapsGapsAndPaddingToTheNearestPage()
    {
        var stack = new PageStack(2, Letter, 1, 1000, gap: 12, padding: 16);
        Assert.Equal(0, stack.PageAt(-50));
        Assert.Equal(0, stack.PageAt(813));    // primera mitad del hueco (808–820)
        Assert.Equal(1, stack.PageAt(815));    // segunda mitad
        Assert.Equal(1, stack.PageAt(100_000));
    }

    [Fact]
    public void ToDocument_And_ToContent_KeepASpotAcrossZooms()
    {
        var before = new PageStack(4, Letter, 1, 1000);
        var spot = before.ToDocument(500, 2000);                     // página 3 (índice 2)
        Assert.Equal(2, spot.Page);
        Assert.Equal(new SKPointI(500, 2000), before.ToContent(spot));

        var after = new PageStack(4, Letter, 2.5, 1000);
        var moved = after.ToContent(spot);
        Assert.Equal(spot.Page, after.ToDocument(moved.X, moved.Y).Page);
        Assert.Equal(spot.X, after.ToDocument(moved.X, moved.Y).X, 0);
        Assert.Equal(spot.Y, after.ToDocument(moved.X, moved.Y).Y, 0);
    }

    [Fact]
    public void EmptyDocument_HasNoContent()
    {
        var stack = new PageStack(0, Letter, 1, 800);
        Assert.Equal(0, stack.ContentHeight);
        Assert.Equal((0, -1), stack.VisiblePages(0, 600));
        Assert.Equal(0, stack.CurrentPage(0, 600));
    }
}

public sealed class ZoomLevelsTests
{
    [Theory]
    [InlineData(1, 1.25)]
    [InlineData(1.1, 1.25)]
    [InlineData(0.1, 0.25)]
    [InlineData(4, 4)]
    public void ZoomIn_GoesToTheNextPreset(double from, double expected) => Assert.Equal(expected, ZoomLevels.ZoomIn(from));

    [Theory]
    [InlineData(1, 0.75)]
    [InlineData(0.8, 0.75)]
    [InlineData(0.25, 0.1)]
    [InlineData(0.1, 0.1)]
    public void ZoomOut_GoesToThePreviousPreset(double from, double expected) => Assert.Equal(expected, ZoomLevels.ZoomOut(from));

    [Fact]
    public void FitWidth_FillsTheViewportMinusPadding()
    {
        var letter = PageSetup.Letter.Size;
        Assert.Equal(1, ZoomLevels.FitWidth(letter, 816 + 32, 96), 6);   // 612 pt = 816 px a 96 dpi
        Assert.Equal(0.5, ZoomLevels.FitWidth(letter, 816 + 32, 192), 6);
    }

    [Fact]
    public void FitPage_TakesTheSmallerOfBothFits()
    {
        var letter = PageSetup.Letter.Size;
        var zoom = ZoomLevels.FitPage(letter, 2000, 528 + 32, 96);     // 792 pt = 1056 px → mitad de alto
        Assert.Equal(0.5, zoom, 6);
        Assert.Equal(zoom, ZoomLevels.Resolve(ZoomMode.WholePage, 3, letter, 2000, 560, 96), 6);
        Assert.Equal(3, ZoomLevels.Resolve(ZoomMode.Custom, 3, letter, 2000, 560, 96));
    }

    [Fact]
    public void Clamp_KeepsTheZoomInRange()
    {
        Assert.Equal(ZoomLevels.Min, ZoomLevels.Clamp(0.001));
        Assert.Equal(ZoomLevels.Max, ZoomLevels.Clamp(50));
        Assert.Equal(1, ZoomLevels.Clamp(double.NaN));
        Assert.Equal(ZoomLevels.Min, ZoomLevels.FitPage(new Size(226, 100_000), 800, 600, 96));   // rollo largo
    }
}

public sealed class TilesTests
{
    [Fact]
    public void Covering_TheWholePage_ClipsTheLastRowAndColumn()
    {
        var tiles = Tiles.Covering(0, 1, 2500, 1500, SKRectI.Create(0, 0, 2500, 1500)).ToList();

        Assert.Equal(6, tiles.Count);
        Assert.Equal(new SKRectI(0, 0, 1024, 1024), tiles[0].Bounds);
        Assert.Equal(new SKRectI(2048, 1024, 2500, 1500), tiles[^1].Bounds);
        Assert.Equal(new TileKey(0, 1, 2, 1), tiles[^1].Key);
    }

    [Fact]
    public void Covering_ARegion_ReturnsOnlyTheTilesItTouches()
    {
        var tiles = Tiles.Covering(3, 2, 2500, 2500, new SKRectI(1000, 1000, 1100, 1100)).ToList();
        Assert.Equal(4, tiles.Count);
        Assert.All(tiles, t => Assert.Equal(3, t.Key.Page));

        Assert.Empty(Tiles.Covering(0, 1, 500, 500, new SKRectI(600, 0, 700, 100)));
        Assert.Empty(Tiles.Covering(0, 1, 500, 500, new SKRectI(100, 100, 100, 200)));
    }
}

public sealed class TileCacheTests
{
    private sealed class Probe : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private static TileKey Key(int row) => new(0, 1, 0, row);

    [Fact]
    public void Add_EvictsTheLeastRecentlyUsedAndDisposesIt()
    {
        using var cache = new TileCache<Probe>(budgetBytes: 300);
        var a = new Probe();
        var b = new Probe();
        var c = new Probe();
        cache.Add(Key(0), a, 100);
        cache.Add(Key(1), b, 100);
        cache.Add(Key(2), c, 100);
        Assert.True(cache.TryGet(Key(0), out _));     // "a" pasa a ser el más reciente

        cache.Add(Key(3), new Probe(), 100);

        Assert.True(b.Disposed);
        Assert.False(a.Disposed);
        Assert.False(cache.TryGet(Key(1), out _));
        Assert.Equal(3, cache.Count);
        Assert.Equal(300, cache.Bytes);
    }

    [Fact]
    public void Add_KeepsATileBiggerThanTheBudget()
    {
        using var cache = new TileCache<Probe>(budgetBytes: 100);
        var small = new Probe();
        cache.Add(Key(0), small, 50);
        cache.Add(Key(1), new Probe(), 500);

        Assert.True(small.Disposed);
        Assert.True(cache.TryGet(Key(1), out _));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Replace_RemoveWhere_AndClear_DisposeValues()
    {
        var cache = new TileCache<Probe>();
        var first = new Probe();
        cache.Add(Key(0), first, 10);
        cache.Add(Key(0), new Probe(), 10);
        Assert.True(first.Disposed);
        Assert.Equal(10, cache.Bytes);

        var other = new Probe();
        cache.Add(new TileKey(0, 2, 0, 0), other, 10);
        Assert.Equal(1, cache.RemoveWhere(k => k.PixelsPerPoint != 2));
        Assert.True(cache.TryGet(new TileKey(0, 2, 0, 0), out _));

        cache.Dispose();
        Assert.True(other.Disposed);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
    }
}
