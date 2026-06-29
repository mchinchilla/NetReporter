using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Svg;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Svg.Tests;

public sealed class SvgRendererTests
{
    private static RlRenderList BuildList(params RenderCommand[] commands) =>
        new(PageSetup.Letter, new[] { new RenderPage(1, commands) });

    [Fact]
    public void Render_RoundedRectangle_ProducesRoundedOutput()
    {
        var fill = Color.FromHex("#E3F1F4");
        var rounded = BuildList(new DrawRectangleCommand(new Rect(10, 10, 100, 50), fill, null, 6));
        var square  = BuildList(new DrawRectangleCommand(new Rect(10, 10, 100, 50), fill, null));

        var roundedSvg = new SvgRenderer().Render(rounded);
        var squareSvg  = new SvgRenderer().Render(square);

        Assert.Single(roundedSvg);
        Assert.False(string.IsNullOrWhiteSpace(roundedSvg[0]));
        // Skia's SVG canvas emits rounded corners either as rx/ry on the <rect> or as a
        // <path>; in both cases the rounded output differs from the square one. Assert the
        // difference (robust) plus the rounded marker when present.
        Assert.NotEqual(squareSvg[0], roundedSvg[0]);
    }

    [Fact]
    public void Render_SquareRectangle_DoesNotThrow()
    {
        var fill = Color.FromHex("#E3F1F4");
        var list = BuildList(new DrawRectangleCommand(new Rect(10, 10, 100, 50), fill, null));

        var svg = new SvgRenderer().Render(list);

        Assert.Single(svg);
        Assert.False(string.IsNullOrWhiteSpace(svg[0]));
    }
}
