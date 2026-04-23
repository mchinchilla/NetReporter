namespace NetReporter.Core.RenderList;

public sealed record RenderPage(
    int PageNumber,
    IReadOnlyList<RenderCommand> Commands);
