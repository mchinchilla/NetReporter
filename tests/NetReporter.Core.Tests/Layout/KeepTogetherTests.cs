using NetReporter.Core.Bands;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Expressions;
using NetReporter.Core.Layout;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Tests.Layout;

public sealed class KeepTogetherTests
{
    private static readonly StyleSheet Styles = new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Helvetica").FontSize(10))
        .Build();

    private static TextElement Text(string content) => new()
    {
        Bounds = new Rect(0, 0, 100, 14),
        Content = Expr.Str(content),
        WordWrap = false
    };

    private static ReportDefinition BuildReport(double pageHeight, params Band[] bands) => new()
    {
        Name = "Test",
        Page = new PageSetup(new Size(612, pageHeight), new Thickness(20)),
        Styles = Styles,
        Bands = bands
    };

    [Fact]
    public void ReportHeader_WithoutKeepTogether_FitsOnFirstPage_NoBreak()
    {
        // 200pt page útil = 200 - 40 margins = 160. Header de 50pt cabe.
        var report = BuildReport(200,
            new ReportHeaderBand
            {
                Height = 50,
                Elements = new ReportElement[] { Text("Header") }
            });

        var rl = new LayoutEngine().Layout(report);
        Assert.Single(rl.Pages);
    }

    [Fact]
    public void ReportHeader_WithKeepTogether_AndFits_DoesNotBreak()
    {
        var report = BuildReport(200,
            new ReportHeaderBand
            {
                Height = 50,
                KeepTogether = true,
                Elements = new ReportElement[] { Text("Header") }
            });

        var rl = new LayoutEngine().Layout(report);
        Assert.Single(rl.Pages);
    }

    [Fact]
    public void ReportFooter_NoExplicitKeepTogether_BreaksIfDoesNotFit()
    {
        // ReportHeader 100, Detail 0 (sin tabla, vacío), ReportFooter 80.
        // Page útil = 100. Footer de 80 NO cabe después del header.
        var report = BuildReport(140,
            new ReportHeaderBand
            {
                Height = 80,
                Elements = new ReportElement[] { Text("Header largo") }
            },
            new ReportFooterBand
            {
                Height = 80,
                Elements = new ReportElement[] { Text("Footer") }
            });

        var rl = new LayoutEngine().Layout(report);
        // Expected: header en pag 1, footer movido a pag 2.
        Assert.Equal(2, rl.Pages.Count);
    }

    [Fact]
    public void ReportHeader_WithKeepTogether_DoesNotProduceBrokenLayout()
    {
        // Confirma que el flag se respeta y no genera duplicación de comandos.
        var report = BuildReport(800,
            new ReportHeaderBand
            {
                Height = 60,
                KeepTogether = true,
                Elements = new ReportElement[] { Text("ATÓMICO") }
            });

        var rl = new LayoutEngine().Layout(report);
        var headerCommands = rl.Pages.SelectMany(p => p.Commands).OfType<NetReporter.Core.RenderList.DrawTextCommand>()
                               .Where(c => c.Text == "ATÓMICO").ToArray();
        // Solo una emisión del header.
        Assert.Single(headerCommands);
    }

    [Fact]
    public void Band_KeepTogetherFalseByDefault()
    {
        var band = new ReportHeaderBand
        {
            Height = 10,
            Elements = Array.Empty<ReportElement>()
        };
        Assert.False(band.KeepTogether);
        Assert.False(band.AutoHeight);
    }
}
