using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using NetReporter.Core.Definition;
using NetReporter.Core.RenderList;
using NetReporter.Svg;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Viewer;

/// <summary>
/// A report that was laid out once when the viewer opened it. Pages are written to SVG on request and
/// exports reuse the same <see cref="RenderList"/>, so what is downloaded or printed is exactly what was shown.
/// <para>
/// One <see cref="SvgWriterContext"/> per document, like <c>SvgRenderer.Render</c>: fonts, metrics and image
/// headers are resolved once, not once per page. The context isn't thread-safe and a browser asks for several
/// pages at once, so page renders of one document are serialized (different documents still render in
/// parallel). Images are not embedded in the pages: each distinct image gets an index and the pages reference
/// <c>{ImagesUrl}/{index}</c>, so a logo repeated on every page is downloaded once. The SVG itself is not kept:
/// the browser caches it.
/// </para>
/// </summary>
internal sealed class ViewerDocument : IDisposable
{
    private readonly SvgWriterContext _svg;
    private readonly Lock _gate = new();
    private readonly Dictionary<byte[], int> _imageIndex = new(ReferenceEqualityComparer.Instance);
    private readonly List<(byte[] Data, string? MimeHint)> _images = [];
    private volatile string? _imagesUrl;
    private bool _disposed;

    public ViewerDocument(string reportId, ReportDefinition report, RlRenderList renderList, string? owner)
    {
        ReportId = reportId;
        Report = report;
        RenderList = renderList;
        Owner = owner;

        // The layout emits the same byte[] instance for an element on every page it appears on.
        foreach (var page in renderList.Pages)
            foreach (var cmd in page.Commands)
                if (cmd is DrawImageCommand { Data.Length: > 0 } image && _imageIndex.TryAdd(image.Data, _images.Count))
                    _images.Add((image.Data, image.MimeType));

        _svg = new SvgWriterContext(ResolveImageHref);
    }

    public string ReportId { get; }
    public ReportDefinition Report { get; }
    public RlRenderList RenderList { get; }

    /// <summary>User name of whoever opened the document (null when anonymous). Other users get 404.</summary>
    public string? Owner { get; }

    public int PageCount => RenderList.Pages.Count;

    /// <summary>Distinct images of the document, in the order the pages reference them.</summary>
    public int ImageCount => _images.Count;

    /// <summary>Bytes and declared MIME type of image <paramref name="index"/>, or null when out of range.</summary>
    public (byte[] Data, string? MimeHint)? Image(int index) =>
        index >= 0 && index < _images.Count ? _images[index] : null;

    /// <summary>
    /// Base URL of the image endpoint (<c>…/_doc/{token}/images</c>). Until it is set, pages embed images
    /// as <c>data:</c> URIs; the store sets it right after assigning the token, before any page is served.
    /// </summary>
    public void SetImagesUrl(string url) => _imagesUrl = url;

    private string? ResolveImageHref(byte[] data, string mime)
    {
        var baseUrl = _imagesUrl;
        return baseUrl is not null && _imageIndex.TryGetValue(data, out var index) ? $"{baseUrl}/{index}" : null;
    }

    /// <summary>
    /// SVG of page <paramref name="index"/> (0-based). Throws <see cref="OperationCanceledException"/> when
    /// <paramref name="cancellationToken"/> is cancelled before the page is written.
    /// </summary>
    public string RenderSvgPage(int index, CancellationToken cancellationToken)
    {
        var page = RenderList.Pages[index];
        var size = RenderList.Page.Size;
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            // Waiting for the lock may have outlived the request.
            cancellationToken.ThrowIfCancellationRequested();
            if (!_disposed)
                return SvgRenderer.RenderPage(page, (float)size.Width, (float)size.Height, _svg);
        }
        // Evicted while this request held it: write with a throwaway context (same image URLs).
        using var context = new SvgWriterContext(ResolveImageHref);
        return SvgRenderer.RenderPage(page, (float)size.Width, (float)size.Height, context);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _svg.Dispose();
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
