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
        Assert.Contains("rx=\"6\"", roundedSvg[0]);
        Assert.DoesNotContain("rx=", squareSvg[0]);
    }

    [Fact]
    public void Render_OnePageStringPerPage_InOrder()
    {
        var list = new RlRenderList(PageSetup.A4,
        [
            new RenderPage(1, [new DrawLineCommand(new Point(0, 1), new Point(10, 1), 1, Color.Black)]),
            new RenderPage(2, [new DrawLineCommand(new Point(0, 2), new Point(10, 2), 1, Color.Black)]),
        ]);

        var svg = new SvgRenderer().Render(list);

        Assert.Equal(2, svg.Count);
        Assert.Contains("y1=\"1\"", svg[0]);
        Assert.Contains("y1=\"2\"", svg[1]);
        Assert.All(svg, s => Assert.Contains("width=\"595.276\" height=\"841.89\"", s));
    }

    [Fact]
    public void Render_ClosedPath_EmitsFilledPath()
    {
        var points = new[] { new Point(10, 10), new Point(60, 10), new Point(35, 50) };
        var list = BuildList(DrawPathCommand.Create(points, closed: true, Color.FromHex("#2563EB"), null));

        var svg = new SvgRenderer().Render(list)[0];

        Assert.Contains("<path", svg);
        Assert.Contains("2563EB", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_OpenPath_IsStrokedNotFilled()
    {
        var points = new[] { new Point(10, 10), new Point(60, 40), new Point(110, 20) };
        var list = BuildList(DrawPathCommand.Create(points, closed: false, null,
            new NetReporter.Core.Styles.BorderLine(2, Color.FromHex("#F59E0B"))));

        var svg = new SvgRenderer().Render(list)[0];

        Assert.Contains("<path", svg);
        Assert.Contains("fill=\"none\"", svg);
        Assert.Contains("stroke=\"#F59E0B\"", svg);
    }

    [Fact]
    public void Render_DegeneratePath_IsSkipped()
    {
        var list = BuildList(DrawPathCommand.Create(new[] { new Point(1, 1) }, closed: true, Color.Black, null));
        var svg = new SvgRenderer().Render(list)[0];
        Assert.DoesNotContain("<path", svg);
    }

    [Fact]
    public void RenderPage_StandsAlone_AndEqualsTheDocumentRender()
    {
        var list = BuildList(new DrawRectangleCommand(new Rect(1, 2, 3, 4), Color.Black, null));
        var renderer = new SvgRenderer();

        Assert.Equal(renderer.Render(list)[0], renderer.RenderPage(list.Pages[0], 612, 792));
    }
}
