using Microsoft.AspNetCore.Http;
using NetReporter.Core.Definition;

namespace NetReporter.Viewer;

/// <summary>
/// Resolves a report id plus the request's query string into a bound <see cref="ReportDefinition"/>.
/// This is where the application loads the template, queries its data and checks permissions.
/// </summary>
public interface IReportSource
{
    /// <summary>Returns the report, or <c>null</c> when it doesn't exist or the user can't see it (the viewer answers 404).</summary>
    ValueTask<ReportDefinition?> ResolveAsync(ReportRequest request, CancellationToken cancellationToken);
}

/// <summary>A request to open a report in the viewer.</summary>
public sealed class ReportRequest
{
    internal ReportRequest(string reportId, HttpContext httpContext)
    {
        ReportId = reportId;
        HttpContext = httpContext;
    }

    /// <summary>The <c>{reportId}</c> route segment, e.g. <c>"invoice"</c> in <c>/reports/invoice?id=42</c>.</summary>
    public string ReportId { get; }

    /// <summary>The query string of the viewer URL (report parameters such as <c>id=42</c>).</summary>
    public IQueryCollection Query => HttpContext.Request.Query;

    /// <summary>The current request: user, services, headers.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>First value of a query parameter, or <c>null</c>.</summary>
    public string? this[string name] => Query.TryGetValue(name, out var v) && v.Count > 0 ? v[0] : null;
}

internal sealed class DelegateReportSource(Func<ReportRequest, CancellationToken, ValueTask<ReportDefinition?>> resolve) : IReportSource
{
    public ValueTask<ReportDefinition?> ResolveAsync(ReportRequest request, CancellationToken cancellationToken)
        => resolve(request, cancellationToken);
}
