using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using NetReporter.Core.Definition;
using NetReporter.Svg;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Viewer;

/// <summary>
/// A report that was laid out once when the viewer opened it. Pages are rendered to SVG on first
/// request and kept; exports reuse the same <see cref="RenderList"/>, so what is downloaded or printed
/// is exactly what was shown.
/// </summary>
internal sealed class ViewerDocument
{
    private readonly string?[] _svgPages;

    public ViewerDocument(string reportId, ReportDefinition report, RlRenderList renderList, string? owner)
    {
        ReportId = reportId;
        Report = report;
        RenderList = renderList;
        Owner = owner;
        _svgPages = new string?[renderList.Pages.Count];
    }

    public string ReportId { get; }
    public ReportDefinition Report { get; }
    public RlRenderList RenderList { get; }

    /// <summary>User name of whoever opened the document (null when anonymous). Other users get 404.</summary>
    public string? Owner { get; }

    public int PageCount => _svgPages.Length;

    /// <summary>SVG of page <paramref name="index"/> (0-based). Rendering is idempotent, so a race only wastes work.</summary>
    public string GetSvgPage(int index)
    {
        var svg = Volatile.Read(ref _svgPages[index]);
        if (svg is not null) return svg;

        var size = RenderList.Page.Size;
        svg = new SvgRenderer().RenderPage(RenderList.Pages[index], (float)size.Width, (float)size.Height);
        Volatile.Write(ref _svgPages[index], svg);
        return svg;
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
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = _maxDocuments });
    }

    public string Add(ViewerDocument document)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(18));
        var entry = new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = _lifetime };

        // MemoryCache drops (does not store) an entry that would exceed SizeLimit and only compacts in
        // the background, so make room synchronously: the document must exist when the viewer asks for pages.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (_cache.Count >= _maxDocuments) _cache.Compact(0.1);
            _cache.Set(token, document, entry);
            if (_cache.TryGetValue(token, out _)) return token;
            _cache.Compact(0.25);
        }

        throw new InvalidOperationException("The viewer document cache is full.");
    }

    public ViewerDocument? Get(string token, string? user)
    {
        if (!_cache.TryGetValue(token, out ViewerDocument? doc) || doc is null) return null;
        return string.Equals(doc.Owner, user, StringComparison.Ordinal) ? doc : null;
    }

    public void Dispose() => _cache.Dispose();
}
