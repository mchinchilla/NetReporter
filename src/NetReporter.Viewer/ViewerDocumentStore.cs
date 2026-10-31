using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using NetReporter.Core.Definition;
using NetReporter.Svg;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Viewer;

/// <summary>
/// A report that was laid out once when the viewer opened it. Pages are rendered to SVG on request and
/// exports reuse the same <see cref="RenderList"/>, so what is downloaded or printed is exactly what was shown.
/// <para>
/// One <see cref="SkiaPaintCache"/> per document, like <c>SvgRenderer.Render</c>: fonts, paints and decoded
/// images are created once, not once per page. The cache isn't thread-safe and a browser asks for several
/// pages at once, so page renders of one document are serialized (different documents still render in
/// parallel). The SVG itself is not kept: the browser caches it, and a page with an embedded image weighs
/// hundreds of KB.
/// </para>
/// </summary>
internal sealed class ViewerDocument : IDisposable
{
    private readonly SkiaPaintCache _paintCache = new();
    private readonly Lock _gate = new();
    private bool _disposed;

    public ViewerDocument(string reportId, ReportDefinition report, RlRenderList renderList, string? owner)
    {
        ReportId = reportId;
        Report = report;
        RenderList = renderList;
        Owner = owner;
    }

    public string ReportId { get; }
    public ReportDefinition Report { get; }
    public RlRenderList RenderList { get; }

    /// <summary>User name of whoever opened the document (null when anonymous). Other users get 404.</summary>
    public string? Owner { get; }

    public int PageCount => RenderList.Pages.Count;

    /// <summary>SVG of page <paramref name="index"/> (0-based).</summary>
    public string RenderSvgPage(int index)
    {
        var page = RenderList.Pages[index];
        var size = RenderList.Page.Size;
        lock (_gate)
        {
            if (!_disposed)
                return SvgRenderer.RenderPage(page, (float)size.Width, (float)size.Height, _paintCache);
        }
        // Evicted while this request held it: render with a throwaway cache.
        return new SvgRenderer().RenderPage(page, (float)size.Width, (float)size.Height);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _paintCache.Dispose();
        }
    }
}

/// <summary>In-memory documents keyed by an unguessable token, with sliding expiration and a count limit.</summary>
internal sealed class ViewerDocumentStore : IDisposable
{
    private readonly MemoryCache _cache;
    private readonly TimeSpan _lifetime;
    private readonly int _maxDocuments;

    public ViewerDocumentStore(ReportViewerOptions options)
    {
        _lifetime = options.DocumentLifetime;
        _maxDocuments = Math.Max(1, options.MaxCachedDocuments);
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    public string Add(ViewerDocument document)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(18));
        var entry = new MemoryCacheEntryOptions { SlidingExpiration = _lifetime };
        // Evicted (expired, compacted, disposed store) → free the document's Skia objects.
        entry.RegisterPostEvictionCallback(static (_, value, _, _) => (value as ViewerDocument)?.Dispose());

        // No SizeLimit on purpose: MemoryCache rejects an entry that would exceed it — and runs its eviction
        // callback, which would dispose the document being added. Make room first (least recently used go),
        // then Set always stores. Concurrent opens may briefly overshoot the limit by a few documents.
        var excess = _cache.Count - _maxDocuments + 1;
        // Compact floors Count * percentage: the +0.5 keeps that from rounding down to one document less.
        if (excess > 0) _cache.Compact(Math.Min(1.0, (excess + 0.5) / _cache.Count));
        _cache.Set(token, document, entry);
        return token;
    }

    public ViewerDocument? Get(string token, string? user)
    {
        if (!_cache.TryGetValue(token, out ViewerDocument? doc) || doc is null) return null;
        return string.Equals(doc.Owner, user, StringComparison.Ordinal) ? doc : null;
    }

    public void Dispose() => _cache.Dispose();
}
