using System.Globalization;
using NetReporter.Core.Expressions;
using NetReporter.Core.RenderList;

namespace NetReporter.Core.Layout;

internal sealed class LayoutContext : IEvaluationContext
{
    public int PageNumber { get; set; }
    public int TotalPages { get; set; }
    public int RowIndex { get; set; }
    public object? CurrentRow { get; set; }
    public object? GroupKey { get; set; }
    public int GroupRowCount { get; set; }
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    public object? GetParameter(string name) => null;  // prototipo
    public object? GetAggregate(string name) => null;  // prototipo
}

internal sealed class PageBuilder
{
    // Settable so a paginating table can hand its final page's number back to the caller's workingPage
    // (the caller still owns that page for the ReportFooter + final close).
    public int PageNumber { get; set; }
    public List<RenderCommand> Commands { get; } = new();

    public PageBuilder(int pageNumber) => PageNumber = pageNumber;

    // Snapshot the commands so a later mutation of this builder's Commands list (e.g. a paginating table
    // that reuses the caller's workingPage) can't corrupt a page already emitted into the render list.
    public RenderPage Build() => new(PageNumber, new List<RenderCommand>(Commands));
}
