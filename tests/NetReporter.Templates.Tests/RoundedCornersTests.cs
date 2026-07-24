using NetReporter.Core.Bands;
using NetReporter.Core.Elements;
using NetReporter.Core.Primitives;
using NetReporter.Templates;

namespace NetReporter.Templates.Tests;

/// <summary>
/// <c>roundedCorners</c> permite apilar una banda de cabecera y su cuerpo de modo que se lean
/// como una sola tarjeta: la cabecera redondea arriba, el cuerpo abajo y el canto donde se
/// tocan queda recto.
/// </summary>
public sealed class RoundedCornersTests
{
    private const string Preamble = """
        name: Demo
        page: { size: Letter }
        styles:
          Default: { fontFamily: Helvetica, fontSize: 10 }
        bands:
          - kind: ReportHeader
            height: 60
            elements:
              - type: rectangle
                bounds: { x: 0, y: 0, width: 100, height: 20 }
                fill: "#0B7285"
                cornerRadius: 5

        """;

    /// <summary>Propiedades extra del rectángulo (indentadas a su nivel: 8 espacios).</summary>
    private static RectangleElement FirstRectangle(string? rectangleExtras = null)
    {
        var yaml = rectangleExtras is null ? Preamble : Preamble + "        " + rectangleExtras + "\n";
        var report = YamlReportLoader.Parse(yaml).Bind("{}");
        return report.Bands.OfType<ReportHeaderBand>().Single()
            .Elements.OfType<RectangleElement>().First();
    }

    [Fact]
    public void NoRoundedCorners_DefaultsToAll()
        => Assert.Equal(RectCorners.All, FirstRectangle().Corners);

    [Theory]
    [InlineData("top", RectCorners.Top)]
    [InlineData("bottom", RectCorners.Bottom)]
    [InlineData("left", RectCorners.Left)]
    [InlineData("right", RectCorners.Right)]
    [InlineData("all", RectCorners.All)]
    [InlineData("none", RectCorners.None)]
    [InlineData("topLeft", RectCorners.TopLeft)]
    [InlineData("bottomRight", RectCorners.BottomRight)]
    public void RoundedCorners_ParsesShorthands(string value, RectCorners expected)
        => Assert.Equal(expected, FirstRectangle("roundedCorners: " + value).Corners);

    [Fact]
    public void RoundedCorners_ParsesCommaSeparatedList()
        => Assert.Equal(
            RectCorners.TopLeft | RectCorners.BottomRight,
            FirstRectangle("roundedCorners: \"topLeft, bottomRight\"").Corners);

    [Fact]
    public void RoundedCorners_IsCaseAndSeparatorInsensitive()
        => Assert.Equal(
            RectCorners.TopLeft | RectCorners.BottomRight,
            FirstRectangle("roundedCorners: \"TOP-LEFT, bottom_right\"").Corners);

    [Fact]
    public void RoundedCorners_UnknownValue_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => FirstRectangle("roundedCorners: diagonal"));

        Assert.Contains("diagonal", ex.Message);
    }
}
