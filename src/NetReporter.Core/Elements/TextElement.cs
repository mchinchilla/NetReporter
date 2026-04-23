using NetReporter.Core.Expressions;

namespace NetReporter.Core.Elements;

public sealed record TextElement : ReportElement
{
    public required IExpression<string> Content { get; init; }
    public bool WordWrap { get; init; } = true;
    public bool AutoHeight { get; init; } = false;
}
