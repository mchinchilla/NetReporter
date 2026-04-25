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
    public int PageNumber { get; }
    public List<RenderCommand> Commands { get; } = new();

    public PageBuilder(int pageNumber) => PageNumber = pageNumber;

    public RenderPage Build() => new(PageNumber, Commands);
}
