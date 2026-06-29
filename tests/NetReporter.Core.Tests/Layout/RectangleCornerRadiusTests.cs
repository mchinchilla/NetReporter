using NetReporter.Core.Bands;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Layout;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Tests.Layout;

public sealed class RectangleCornerRadiusTests
{
    private static readonly StyleSheet Styles = new StyleSheetBuilder()
        .Add("Default", s => s.FontFamily("Helvetica").FontSize(10))
        .Build();

    private static ReportDefinition BuildReport(params ReportElement[] elements) => new()
    {
        Name = "Test",
        Page = PageSetup.Letter.WithMargins(new Thickness(0)),
        Styles = Styles,
        Bands = new Band[] { new ReportHeaderBand { Height = 100, Elements = elements } }
    };

    [Fact]
    public void RectangleElement_CornerRadius_FlowsIntoDrawCommand()
    {
        var report = BuildReport(new RectangleElement
        {
            Bounds = new Rect(0, 0, 100, 50),
            Fill = Color.FromHex("#E3F1F4"),
            CornerRadius = 6
        });

        var rl = new LayoutEngine().Layout(report);
        var rect = rl.Pages[0].Commands.OfType<DrawRectangleCommand>().Single();
        Assert.Equal(6, rect.CornerRadius, 3);
    }

    [Fact]
    public void RectangleElement_DefaultCornerRadius_IsZero()
    {
        var report = BuildReport(new RectangleElement
        {
            Bounds = new Rect(0, 0, 100, 50),
            Fill = Color.FromHex("#E3F1F4")
        });

        var rl = new LayoutEngine().Layout(report);
        var rect = rl.Pages[0].Commands.OfType<DrawRectangleCommand>().Single();
        Assert.Equal(0, rect.CornerRadius, 3);
    }
}
