using SkiaSharp;

namespace NetReporter.Viewer.Desktop;

/// <summary>Identifies a painted tile: page, scale and position in the page's tile grid.</summary>
public readonly record struct TileKey(int Page, double PixelsPerPoint, int Column, int Row);

/// <summary>A tile of a page: its key and its rectangle in page pixels (clipped to the page).</summary>
public readonly record struct PageTile(TileKey Key, SKRectI Bounds);

/// <summary>
/// Splits pages into fixed-size square tiles so a viewer paints and caches only what is on screen.
/// A whole-page bitmap per zoom does not scale: a continuous roll can be tens of thousands of pixels
/// tall, and a Letter page at 400 % on a 200 % screen is ~260 MB in BGRA.
/// </summary>
public static class Tiles
{
    /// <summary>Tile edge in pixels (4 MB per BGRA tile). An A4 page at 100 % on a 96 dpi screen is 1 × 2 tiles.</summary>
    public const int Size = 1024;

    /// <summary>
    /// The tiles of a page of <paramref name="pageWidth"/> × <paramref name="pageHeight"/> pixels that intersect
    /// <paramref name="region"/> (page pixels), top-to-bottom, left-to-right.
    /// </summary>
    public static IEnumerable<PageTile> Covering(int pageIndex, double pixelsPerPoint, int pageWidth, int pageHeight, SKRectI region,
        int tileSize = Size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize);
        var left = Math.Max(0, region.Left);
        var top = Math.Max(0, region.Top);
        var right = Math.Min(pageWidth, region.Right);
        var bottom = Math.Min(pageHeight, region.Bottom);
        if (left >= right || top >= bottom) yield break;

        var firstColumn = left / tileSize;
        var lastColumn = (right - 1) / tileSize;
        var firstRow = top / tileSize;
        var lastRow = (bottom - 1) / tileSize;
        for (var row = firstRow; row <= lastRow; row++)
        for (var column = firstColumn; column <= lastColumn; column++)
        {
            var x = column * tileSize;
            var y = row * tileSize;
            var bounds = new SKRectI(x, y, Math.Min(pageWidth, x + tileSize), Math.Min(pageHeight, y + tileSize));
            yield return new PageTile(new TileKey(pageIndex, pixelsPerPoint, column, row), bounds);
        }
    }
}

/// <summary>
/// Least-recently-used cache of painted tiles with a byte budget. Evicted and replaced values are
/// disposed. Not thread-safe: the viewer touches it from the UI thread only.
/// </summary>
public sealed class TileCache<T> : IDisposable where T : class, IDisposable
{
    private readonly Dictionary<TileKey, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _lru = new();     // first = most recently used

    /// <param name="budgetBytes">Total size kept; the least recently used tiles go first. Default 256 MB.</param>
    public TileCache(long budgetBytes = 256L * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budgetBytes);
        BudgetBytes = budgetBytes;
    }

    public long BudgetBytes { get; }
    public long Bytes { get; private set; }
    public int Count => _map.Count;

    public bool TryGet(TileKey key, out T value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        value = null!;
        return false;
    }

    /// <summary>Stores a tile (replacing and disposing an existing one), then evicts down to the budget.
    /// The tile just added is never evicted, even when it alone exceeds the budget.</summary>
    public void Add(TileKey key, T value, long bytes)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        Remove(key);
        var node = _lru.AddFirst(new Entry(key, value, bytes));
        _map[key] = node;
        Bytes += bytes;
        while (Bytes > BudgetBytes && _lru.Last is { } last && last != node)
            Evict(last);
    }

    public bool Remove(TileKey key)
    {
        if (!_map.TryGetValue(key, out var node)) return false;
        Evict(node);
        return true;
    }

    /// <summary>Drops every tile whose key matches (e.g. tiles painted at another zoom).</summary>
    public int RemoveWhere(Func<TileKey, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var removed = 0;
        for (var node = _lru.First; node is not null;)
        {
            var next = node.Next;
            if (predicate(node.Value.Key))
            {
                Evict(node);
                removed++;
            }
            node = next;
        }
        return removed;
    }

    public void Clear()
    {
        foreach (var entry in _lru) entry.Value.Dispose();
        _lru.Clear();
        _map.Clear();
        Bytes = 0;
    }

    public void Dispose() => Clear();

    private void Evict(LinkedListNode<Entry> node)
    {
        _lru.Remove(node);
        _map.Remove(node.Value.Key);
        Bytes -= node.Value.Bytes;
        node.Value.Value.Dispose();
    }

    private readonly record struct Entry(TileKey Key, T Value, long Bytes);
}
