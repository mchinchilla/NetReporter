using System.Text.Json;
using NetReporter.Core.Expressions;
using NetReporter.Templates.Binding;

namespace NetReporter.Templates.Tests.Binding;

public sealed class TemplateStringTests
{
    private sealed class TestContext : IEvaluationContext
    {
        public int PageNumber { get; init; } = 1;
        public int TotalPages { get; init; } = 1;
        public int RowIndex { get; init; }
        public object? CurrentRow { get; init; }
        public object? GetParameter(string name) => null;
        public object? GetAggregate(string name) => null;
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Compile_LiteralOnly_ReturnsSameString()
    {
        var expr = TemplateString.Compile("Hola mundo", Parse("{}"));
        Assert.Equal("Hola mundo", expr.Evaluate(new TestContext()));
    }

    [Fact]
    public void Compile_EmptyString_ReturnsEmpty()
    {
        var expr = TemplateString.Compile("", Parse("{}"));
        Assert.Equal("", expr.Evaluate(new TestContext()));
    }

    [Fact]
    public void Compile_PageNumberPlaceholder()
    {
        var expr = TemplateString.Compile("Página {{ pageNumber }}", Parse("{}"));
        Assert.Equal("Página 7", expr.Evaluate(new TestContext { PageNumber = 7 }));
    }

    [Fact]
    public void Compile_TotalPagesPlaceholder()
    {
        var expr = TemplateString.Compile("{{ totalPages }} págs", Parse("{}"));
        Assert.Equal("5 págs", expr.Evaluate(new TestContext { TotalPages = 5 }));
    }

    [Fact]
    public void Compile_CombinedPlaceholders()
    {
        var expr = TemplateString.Compile("Página {{ pageNumber }} de {{ totalPages }}", Parse("{}"));
        Assert.Equal("Página 2 de 10",
            expr.Evaluate(new TestContext { PageNumber = 2, TotalPages = 10 }));
    }

    [Fact]
    public void Compile_JsonPathFromRoot()
    {
        var data = Parse("""{"cliente":{"nombre":"Marvin"}}""");
        var expr = TemplateString.Compile("Hola {{ $.cliente.nombre }}", data);
        Assert.Equal("Hola Marvin", expr.Evaluate(new TestContext()));
    }

    [Fact]
    public void Compile_JsonPath_MissingPath_RendersEmpty()
    {
        var data = Parse("""{"a":1}""");
        var expr = TemplateString.Compile("valor={{ $.missing }}", data);
        Assert.Equal("valor=", expr.Evaluate(new TestContext()));
    }

    [Fact]
    public void Compile_NumericJsonPath_RendersAsRawText()
    {
        var data = Parse("""{"total":1234}""");
        var expr = TemplateString.Compile("{{ $.total }}", data);
        Assert.Equal("1234", expr.Evaluate(new TestContext()));
    }

    [Fact]
    public void Compile_UnclosedPlaceholder_Throws()
    {
        Assert.Throws<FormatException>(() =>
            TemplateString.Compile("incomplete {{ $.foo", Parse("{}")));
    }

    [Fact]
    public void Compile_UnknownKeyword_Throws()
    {
        Assert.Throws<FormatException>(() =>
            TemplateString.Compile("{{ elvis }}", Parse("{}")));
    }

    [Fact]
    public void Compile_EmptyExpression_IsEmptyLiteral()
    {
        var expr = TemplateString.Compile("a{{}}b", Parse("{}"));
        Assert.Equal("ab", expr.Evaluate(new TestContext()));
    }

    [Fact]
    public void Compile_WhitespaceInExpression_IsTrimmed()
    {
        var expr = TemplateString.Compile("{{   pageNumber   }}", Parse("{}"));
        Assert.Equal("3", expr.Evaluate(new TestContext { PageNumber = 3 }));
    }

    [Fact]
    public void CompileForRow_JsonPathResolvesAgainstCurrentRow()
    {
        var row = Parse("""{"nombre":"Laptop"}""");
        var expr = TemplateString.CompileForRow("{{ $.nombre }}");
        Assert.Equal("Laptop", expr.Evaluate(new TestContext { CurrentRow = row }));
    }

    [Fact]
    public void CompileForRow_NoCurrentRow_ReturnsEmpty()
    {
        var expr = TemplateString.CompileForRow("{{ $.nombre }}");
        Assert.Equal("", expr.Evaluate(new TestContext { CurrentRow = null }));
    }

    [Fact]
    public void Compile_RowIndex_ReadsFromContext()
    {
        var expr = TemplateString.Compile("row {{ rowIndex }}", Parse("{}"));
        Assert.Equal("row 4", expr.Evaluate(new TestContext { RowIndex = 4 }));
    }
}
