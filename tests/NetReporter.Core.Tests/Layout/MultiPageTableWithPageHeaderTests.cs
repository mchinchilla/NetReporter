using NetReporter.Core.Bands;
using NetReporter.Core.DataBinding;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Expressions;
using NetReporter.Core.Layout;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Tests.Layout;

/// <summary>Regression guard for the multi-page table + repeating PageHeader interaction. A long table
/// that spans several pages must: (1) render EVERY row exactly once (no rows dropped, none duplicated),
/// (2) repeat the PageHeader on every page, and (3) number pages sequentially. The original bug shared
/// the caller's command list with the first emitted page, so paginating corrupted earlier pages.</summary>
public sealed class MultiPageTableWithPageHeaderTests
{
    private sealed record Row(string Name);

    private static readonly StyleSheet Styles = new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Helvetica").FontSize(10))
        .Add("TableHeader", s => s.BasedOn("Default").Bold())
        .Add("TableRow", s => s.BasedOn("Default"))
        .Build();

    private static ReportDefinition Report(int rowCount)
    {
        var rows = Enumerable.Range(1, rowCount).Select(i => new Row($"ROW-{i:000}")).ToArray();
        var table = new TableElement<Row>
        {
            Bounds = new Rect(0, 0, 400, 0),
            Rows = rows,
            HeaderHeight = 20,
            RowHeight = 16,
            HeaderMode = TableHeaderMode.RepeatOnPageBreak,
            HeaderStyle = new StyleRef("TableHeader"),
            RowStyle = new StyleRef("TableRow"),
            Columns = new[] { new TableColumn<Row>("Name", Bind.From<Row, object?>(r => r.Name), Width: 400) },
        };
        return new ReportDefinition
        {
            Name = "T",
            Page = PageSetup.Letter.WithMargins(new Thickness(36)),
            Styles = Styles,
            Bands = new Band[]
            {
                new PageHeaderBand { Height = 40, Elements = new ReportElement[]
                {
                    new TextElement { Bounds = new Rect(0, 0, 400, 16), Content = Expr.Str("ACME REPORT"), Style = new StyleRef("Default") },
                } },
                new DetailBand { Height = 0, AutoHeight = true, Elements = new[] { (ReportElement)table } },
            },
        };
    }

    [Fact]
    public void EveryRow_RenderedExactlyOnce_AcrossPages()
    {
        const int n = 120;   // forces several pages
        var rl = new LayoutEngine().Layout(Report(n));
        Assert.True(rl.Pages.Count >= 2, "expected multiple pages");

        var allTexts = rl.Pages.SelectMany(p => p.Commands).OfType<DrawTextCommand>().Select(t => t.Text).ToList();
        for (var i = 1; i <= n; i++)
        {
            var key = $"ROW-{i:000}";
            Assert.Equal(1, allTexts.Count(t => t == key));   // exactly once — not dropped, not duplicated
        }
    }

    [Fact]
    public void PageHeader_RepeatsOnEveryPage()
    {
        var rl = new LayoutEngine().Layout(Report(120));
        foreach (var page in rl.Pages)
            Assert.Contains(page.Commands.OfType<DrawTextCommand>(), t => t.Text == "ACME REPORT");
    }

    [Fact]
    public void Pages_NumberedSequentially()
    {
        var rl = new LayoutEngine().Layout(Report(120));
        var numbers = rl.Pages.Select(p => p.PageNumber).ToList();
        Assert.Equal(Enumerable.Range(1, rl.Pages.Count).ToList(), numbers);
    }
}
