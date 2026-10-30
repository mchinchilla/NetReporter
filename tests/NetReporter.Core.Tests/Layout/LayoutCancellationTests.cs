using NetReporter.Core.Bands;
using NetReporter.Core.DataBinding;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Layout;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Tests.Layout;

public sealed class LayoutCancellationTests
{
    private static readonly StyleSheet Styles = new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Helvetica").FontSize(10))
        .Add("TableHeader", s => s.BasedOn("Default").Bold())
        .Add("TableRow", s => s.BasedOn("Default"))
        .Build();

    private static string[] Rows(int count) => Enumerable.Range(1, count).Select(i => $"Fila {i}").ToArray();

    private static ReportDefinition Report(IReadOnlyList<string> rows, Func<string, object?>? binding = null) => new()
    {
        Name = "Cancel",
        Page = new PageSetup(new Size(400, 300), new Thickness(20)),
        Styles = Styles,
        Bands =
        [
            new DetailBand
            {
                Height = 0,
                Elements =
                [
                    new TableElement<string>
                    {
                        Bounds = new Rect(0, 0, 200, 0),
                        Rows = rows,
                        RowHeight = 18,
                        HeaderHeight = 20,
                        Columns = [new TableColumn<string>("Detalle", Bind.From(binding ?? (r => r)), 200)]
                    }
                ]
            }
        ]
    };

    [Fact]
    public void Layout_WithAnAlreadyCancelledToken_Throws()
    {
        Assert.ThrowsAny<OperationCanceledException>(() =>
            new LayoutEngine().Layout(Report(Rows(1)), new CancellationToken(canceled: true)));
    }

    [Fact]
    public void Layout_CancelledWhileEmittingRows_StopsBeforeTheEnd()
    {
        using var cts = new CancellationTokenSource();
        var emitted = 0;
        // El binding corre una vez por fila emitida: se cancela en la fila 50 de 10 000.
        var report = Report(Rows(10_000), r =>
        {
            if (++emitted == 50) cts.Cancel();
            return r;
        });

        Assert.ThrowsAny<OperationCanceledException>(() => new LayoutEngine().Layout(report, cts.Token));
        Assert.InRange(emitted, 50, 51);
    }

    [Fact]
    public void Layout_WithANoneToken_ProducesTheSameOutputAsTheOverloadWithoutToken()
    {
        var report = Report(Rows(40));

        var withToken = new LayoutEngine().Layout(report, CancellationToken.None);
        var without = new LayoutEngine().Layout(report);

        Assert.Equal(without.Pages.Count, withToken.Pages.Count);
        Assert.Equal(without.Pages.SelectMany(p => p.Commands), withToken.Pages.SelectMany(p => p.Commands));
    }
}
