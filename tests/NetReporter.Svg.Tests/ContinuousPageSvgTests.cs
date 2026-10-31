using System.Globalization;
using NetReporter.Core.Bands;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Layout;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;
using NetReporter.Svg;

namespace NetReporter.Svg.Tests;

/// <summary>Un rollo continuo produce un SVG cuya altura es la que calculó el layout, no la nominal.</summary>
public sealed class ContinuousPageSvgTests
{
    [Fact]
    public void Render_UsesTheLayoutHeight()
    {
        var report = new ReportDefinition
        {
            Name = "Ticket",
            Page = PageSetup.Roll80,
            Styles = new StyleSheetBuilder().Add("Default", s => s.FontFamily("Helvetica").FontSize(9)).Build(),
            Bands = new Band[]
            {
                new DetailBand { Height = 123.5, Elements = new ReportElement[]
                {
                    new RectangleElement { Bounds = new Rect(0, 0, 100, 50), Fill = Color.Black },
                } },
            },
        };

        var list = new LayoutEngine().Layout(report);
        var svg = Assert.Single(new SvgRenderer().Render(list));

        var expectedHeight = PageSetup.Roll80.Margins.Top + 123.5 + PageSetup.Roll80.Margins.Bottom;
        Assert.Equal(expectedHeight, list.Page.Size.Height, 6);
        var h = expectedHeight.ToString("0.###", CultureInfo.InvariantCulture);
        var w = PageSetup.Roll80.Size.Width.ToString("0.###", CultureInfo.InvariantCulture);
        Assert.Contains($"width=\"{w}\"", svg);
        Assert.Contains($"height=\"{h}\"", svg);
        Assert.Contains($"viewBox=\"0 0 {w} {h}\"", svg);
    }
}
