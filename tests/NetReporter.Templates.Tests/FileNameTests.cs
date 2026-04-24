using System.Text.Json;
using NetReporter.Templates;
using NetReporter.Templates.Binding;

namespace NetReporter.Templates.Tests;

public sealed class FileNameTests
{
    private const string YamlWithoutFileName = """
        name: Demo
        title: Mi Reporte
        page: { size: Letter }
        styles:
          Default: { fontFamily: Helvetica, fontSize: 10 }
        bands:
          - kind: ReportHeader
            height: 20
            elements: []
        """;

    private const string YamlWithStaticFileName = """
        name: Demo
        title: Mi Reporte
        fileName: "ventas-2026"
        page: { size: Letter }
        styles:
          Default: { fontFamily: Helvetica, fontSize: 10 }
        bands:
          - kind: ReportHeader
            height: 20
            elements: []
        """;

    private const string YamlWithTemplateFileName = """
        name: Demo
        title: Mi Reporte
        fileName: "factura-{{ $.numero }}"
        page: { size: Letter }
        styles:
          Default: { fontFamily: Helvetica, fontSize: 10 }
        bands:
          - kind: ReportHeader
            height: 20
            elements: []
        """;

    // === Bind → FileName ===

    [Fact]
    public void Bind_NoFileName_ReturnsNull()
    {
        var report = YamlReportLoader.Parse(YamlWithoutFileName).Bind("{}");
        Assert.Null(report.FileName);
    }

    [Fact]
    public void Bind_StaticFileName_ReturnsCleanName()
    {
        var report = YamlReportLoader.Parse(YamlWithStaticFileName).Bind("{}");
        Assert.Equal("ventas-2026", report.FileName);
    }

    [Fact]
    public void Bind_TemplateFileName_ResolvesAgainstData()
    {
        var report = YamlReportLoader.Parse(YamlWithTemplateFileName)
            .Bind("""{"numero":"000-001-001-00001234"}""");
        Assert.Equal("factura-000-001-001-00001234", report.FileName);
    }

    [Fact]
    public void Bind_TemplateWithMissingPath_ProducesCleanPartialName()
    {
        var report = YamlReportLoader.Parse(YamlWithTemplateFileName).Bind("{}");
        // "factura-" queda sin nada atrás → sanitize trimea el '-' terminal.
        Assert.Equal("factura", report.FileName);
    }

    [Fact]
    public void Bind_TemplateWithInvalidCharsInData_Sanitizes()
    {
        var report = YamlReportLoader.Parse(YamlWithTemplateFileName)
            .Bind("""{"numero":"A/B:C*?\"<>|"}""");
        Assert.Equal("factura-A-B-C", report.FileName);
    }

    // === SanitizeFileName ===

    [Theory]
    [InlineData("report",             "report")]
    [InlineData("  hola  ",           "hola")]
    [InlineData("A/B",                "A-B")]
    [InlineData("A\\B:C*D?E\"F<G>H|I","A-B-C-D-E-F-G-H-I")]
    [InlineData("---trailing---",     "trailing")]
    [InlineData("...dots...",         "dots")]
    [InlineData("a   b",              "a-b")]
    public void SanitizeFileName_Cases(string input, string expected)
    {
        Assert.Equal(expected, YamlReportLoader.SanitizeFileName(input));
    }

    [Fact]
    public void SanitizeFileName_TruncatesAt200()
    {
        var input = new string('a', 500);
        var result = YamlReportLoader.SanitizeFileName(input);
        Assert.Equal(200, result.Length);
    }

    [Fact]
    public void SanitizeFileName_ControlChars_Replaced()
    {
        var input = "a\u0001b\u0002c";
        Assert.Equal("a-b-c", YamlReportLoader.SanitizeFileName(input));
    }

    [Fact]
    public void SanitizeFileName_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => YamlReportLoader.SanitizeFileName(null!));
    }

    [Fact]
    public void SanitizeFileName_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, YamlReportLoader.SanitizeFileName(""));
        Assert.Equal(string.Empty, YamlReportLoader.SanitizeFileName("   "));
    }

    // === TemplateString.Resolve ===

    [Fact]
    public void TemplateString_Resolve_Literal()
    {
        var result = TemplateString.Resolve("hola", JsonDocument.Parse("{}").RootElement);
        Assert.Equal("hola", result);
    }

    [Fact]
    public void TemplateString_Resolve_Path()
    {
        var data = JsonDocument.Parse("""{"numero":"42"}""").RootElement;
        var result = TemplateString.Resolve("n={{ $.numero }}", data);
        Assert.Equal("n=42", result);
    }

    [Fact]
    public void TemplateString_Resolve_PageNumberIsZero()
    {
        // Fuera del contexto de layout, pageNumber resuelve a "0".
        var result = TemplateString.Resolve("{{ pageNumber }}", JsonDocument.Parse("{}").RootElement);
        Assert.Equal("0", result);
    }
}
