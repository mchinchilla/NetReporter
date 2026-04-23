using NetReporter.Core.Expressions;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Elements;

public abstract record ReportElement
{
    public required Rect Bounds { get; init; }
    public StyleRef Style { get; init; } = StyleRef.Default;
    public IExpression<bool>? Visible { get; init; }
}
