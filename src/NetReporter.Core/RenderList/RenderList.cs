using NetReporter.Core.Primitives;

namespace NetReporter.Core.RenderList;

public sealed record RenderList(
    PageSetup Page,
    IReadOnlyList<RenderPage> Pages);
