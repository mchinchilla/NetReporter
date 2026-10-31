using NetReporter.Core.Primitives;

namespace NetReporter.Templates.Tests;

/// <summary>page.size roll80 / roll58 / roll → papel continuo (<see cref="PageSetup.Continuous"/>).</summary>
public sealed class RollPageTests
{
    private static string Yaml(string page) => $$"""
        name: Ticket
        page:
        {{page}}
        styles:
          Default: { fontFamily: Helvetica, fontSize: 9 }
        bands:
          - kind: Detail
            height: 20
            elements:
              - type: text
                bounds: { x: 0, y: 0, width: 100, height: 12 }
                content: Hola
        """;

    private static PageSetup Page(string page) => YamlReportLoader.Parse(Yaml(page)).Bind("{}").Page;

    [Fact]
    public void Roll80_IsContinuous_80mmWide_WithPrintableMargins()
    {
        var page = Page("  size: roll80");
        Assert.True(page.Continuous);
        Assert.Equal(Units.Mm(80), page.Size.Width, 6);
        Assert.Equal(Units.Mm(4), page.Margins.Left, 6);
    }

    [Fact]
    public void Roll58_IsContinuous_58mmWide()
    {
        var page = Page("  size: roll58");
        Assert.True(page.Continuous);
        Assert.Equal(Units.Mm(58), page.Size.Width, 6);
        Assert.Equal(Units.Mm(5), page.Margins.Left, 6);
    }

    [Fact]
    public void Roll_UsesTheGivenWidth_AndMargins()
    {
        var page = Page("""
              size: roll
              width: 200
              margins: { horizontal: 6pt, vertical: 2mm }
            """);
        Assert.True(page.Continuous);
        Assert.Equal(200, page.Size.Width);
        Assert.Equal(6, page.Margins.Left, 6);
        Assert.Equal(Units.Mm(2), page.Margins.Top, 6);
    }

    [Fact]
    public void Roll_WithoutWidth_Throws()
    {
        var ex = Assert.Throws<FormatException>(() => Page("  size: roll"));
        Assert.Contains("width", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Landscape_OnRoll_Throws()
    {
        var ex = Assert.Throws<FormatException>(() => Page("""
              size: roll80
              orientation: landscape
            """));
        Assert.Contains("continuo", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_IsStillFixedHeight()
    {
        var page = Page("""
              size: custom
              width: 226.77
              height: 600
            """);
        Assert.False(page.Continuous);
        Assert.Equal(600, page.Size.Height);
    }
}
