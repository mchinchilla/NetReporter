using System.Globalization;
using System.Text.Json;
using NetReporter.Core.Expressions;
using NetReporter.Templates.Binding;
using NetReporter.Templates.Expressions;

namespace NetReporter.Templates.Tests.Expressions;

public sealed class DslExpressionTests
{
    private sealed class Ctx : IEvaluationContext
    {
        public int PageNumber { get; init; } = 1;
        public int TotalPages { get; init; } = 1;
        public int RowIndex { get; init; }
        public object? CurrentRow { get; init; }
        public object? GroupKey { get; init; }
        public int GroupRowCount { get; init; }
        public IReadOnlyList<object?> GroupKeys { get; init; } = Array.Empty<object?>();
        public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;
        public object? GetParameter(string name) => null;
        public object? GetAggregate(string name) => null;
    }

    private static readonly JsonElement Data = JsonDocument.Parse("""
        {
          "cliente": { "nombre": "Marvin", "saldo": 0, "vip": true },
          "descuento": 12.5,
          "fecha": "2025-03-15",
          "lineas": [
            { "codigo": "A1", "cantidad": 2, "precio": 10.5 },
            { "codigo": "B2", "cantidad": 3, "precio": 4 },
            { "codigo": "C3", "cantidad": 1, "precio": 100 }
          ],
          "vacio": []
        }
        """).RootElement;

    private static object? Eval(string source, IEvaluationContext? ctx = null) =>
        DslExpression.Parse(source).Evaluate(ctx ?? new Ctx(), Data, Data);

    private static string Text(string template, IEvaluationContext? ctx = null) =>
        TemplateString.Compile(template, Data).Evaluate(ctx ?? new Ctx());

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("10 % 3", 1)]
    [InlineData("-5 + 2", -3)]
    [InlineData("$.descuento * 2", 25)]
    [InlineData("$.lineas[0].cantidad * $.lineas[0].precio", 21)]
    public void Arithmetic(string source, double expected) =>
        Assert.Equal((decimal)expected, Eval(source));

    [Fact]
    public void DivisionByZero_IsNull() => Assert.Null(Eval("1 / 0"));

    [Theory]
    [InlineData("$.cliente.saldo == 0", true)]
    [InlineData("$.cliente.saldo != 0", false)]
    [InlineData("$.descuento > 10 && $.cliente.vip", true)]
    [InlineData("$.descuento > 20 || not $.cliente.vip", false)]
    [InlineData("!($.descuento < 5)", true)]
    [InlineData("'abc' < 'abd'", true)]
    [InlineData("$.noExiste > 0", false)]
    [InlineData("$.noExiste == null", true)]
    [InlineData("'5' == 5", true)]
    public void Logic(string source, bool expected) => Assert.Equal(expected, Eval(source));

    [Fact]
    public void Ternary_And_Coalesce()
    {
        Assert.Equal("PAGADO", Eval("$.cliente.saldo == 0 ? 'PAGADO' : 'PENDIENTE'"));
        Assert.Equal("n/a", Eval("$.noExiste ?? 'n/a'"));
        Assert.Equal("Marvin", Eval("$.cliente.nombre ?? 'n/a'"));
    }

    [Fact]
    public void StringConcatenation_WithPlus() =>
        Assert.Equal("Cliente: Marvin", Eval("'Cliente: ' + $.cliente.nombre"));

    [Fact]
    public void Wildcard_ProjectsIntoAggregates()
    {
        Assert.Equal(6m, Eval("sum($.lineas[*].cantidad)"));
        Assert.Equal(3m, Eval("count($.lineas[*])"));
        Assert.Equal(100m, Eval("max($.lineas[*].precio)"));
        Assert.Equal(4m, Eval("min($.lineas[*].precio)"));
        Assert.Equal(0m, Eval("count($.vacio[*])"));
        Assert.Equal("A1, B2, C3", Eval("join($.lineas[*].codigo)"));
    }

    [Fact]
    public void RootPath_FromRowScope()
    {
        var row = Data.GetProperty("lineas")[1];
        var expr = DslExpression.Parse("$.cantidad * $.precio * (1 - $$.descuento / 100)");
        Assert.Equal(10.5m, expr.Evaluate(new Ctx(), row, Data));
    }

    [Theory]
    [InlineData("upper($.cliente.nombre)", "MARVIN")]
    [InlineData("lower('ABC')", "abc")]
    [InlineData("left($.cliente.nombre, 3)", "Mar")]
    [InlineData("right($.cliente.nombre, 2)", "in")]
    [InlineData("substr('abcdef', 2, 3)", "cde")]
    [InlineData("replace('a-b-c', '-', '/')", "a/b/c")]
    [InlineData("padLeft('7', 3, '0')", "007")]
    [InlineData("concat('a', 1, true)", "a1true")]
    [InlineData("trim('  x  ')", "x")]
    [InlineData("format(1234.5, 'N2')", "1,234.50")]
    [InlineData("format($.fecha, 'dd/MM/yyyy')", "15/03/2025")]
    [InlineData("if($.cliente.vip, 'VIP', 'Normal')", "VIP")]
    [InlineData("coalesce($.noExiste, '', 'fallback')", "fallback")]
    public void Functions_Text(string source, string expected) => Assert.Equal(expected, Eval(source));

    [Theory]
    [InlineData("round(2.345, 2)", 2.35)]
    [InlineData("round(2.5)", 3)]
    [InlineData("floor(2.9)", 2)]
    [InlineData("ceil(2.1)", 3)]
    [InlineData("abs(-4)", 4)]
    [InlineData("len('hola')", 4)]
    [InlineData("year($.fecha)", 2025)]
    [InlineData("month($.fecha)", 3)]
    [InlineData("day(addDays($.fecha, 20))", 4)]
    [InlineData("avg($.lineas[*].cantidad)", 2)]
    public void Functions_Numbers(string source, double expected) =>
        Assert.Equal((decimal)expected, Eval(source));

    [Fact]
    public void If_IsLazy_OnlyEvaluatesChosenBranch() =>
        Assert.Equal(0m, Eval("if($.cliente.saldo == 0, 0, 100 / $.cliente.saldo)"));

    [Fact]
    public void ContextVariables()
    {
        var ctx = new Ctx
        {
            PageNumber = 3, TotalPages = 9, RowIndex = 4, GroupKey = "Norte", GroupRowCount = 7,
            GroupKeys = new object?[] { "2025", "Norte" }
        };
        Assert.Equal(3m, Eval("pageNumber", ctx));
        Assert.Equal(true, Eval("pageNumber == totalPages - 6", ctx));
        Assert.Equal(5m, Eval("rowNumber", ctx));
        Assert.Equal("Norte", Eval("#group", ctx));
        Assert.Equal(7m, Eval("#count", ctx));
        Assert.Equal("2025", Eval("group(0)", ctx));
        Assert.Equal("Norte", Eval("group(1)", ctx));
        Assert.Null(Eval("group(5)", ctx));
    }

    [Theory]
    [InlineData("1 +")]
    [InlineData("upper()")]
    [InlineData("noExiste(1)")]
    [InlineData("foo")]
    [InlineData("'sin cerrar")]
    [InlineData("a = 1")]
    [InlineData("(1 + 2")]
    public void SyntaxErrors_ThrowWithPosition(string source)
    {
        var ex = Assert.Throws<DslSyntaxException>(() => DslExpression.Parse(source));
        Assert.InRange(ex.Position, 0, source.Length);
        Assert.IsAssignableFrom<FormatException>(ex);
    }

    [Theory]
    [InlineData("$.total:N2", "$.total", "N2")]
    [InlineData("$.a * 2 : #,##0.00", "$.a * 2", "#,##0.00")]
    [InlineData("$.a > 0 ? 'x' : 'y'", "$.a > 0 ? 'x' : 'y'", null)]
    [InlineData("$.a > 0 ? 1 : 2 : N0", "$.a > 0 ? 1 : 2", "N0")]
    [InlineData("format($.f, 'HH:mm')", "format($.f, 'HH:mm')", null)]
    [InlineData("$.a ?? 0 : N1", "$.a ?? 0", "N1")]
    public void SplitFormat_IgnoresTernaryAndQuotedColons(string text, string expr, string? format)
    {
        var (e, f) = DslExpression.SplitFormat(text);
        Assert.Equal(expr, e);
        Assert.Equal(format, f);
    }

    [Fact]
    public void TemplateString_UsesDslForExpressions()
    {
        Assert.Equal("Total: 131.00", Text("Total: {{ sum($.lineas[*].precio) + 16.5 : N2 }}"));
        Assert.Equal("Estado: PAGADO", Text("Estado: {{ $.cliente.saldo == 0 ? 'PAGADO' : 'PENDIENTE' }}"));
        Assert.Equal("Hola MARVIN (3 líneas)", Text("Hola {{ upper($.cliente.nombre) }} ({{ count($.lineas[*]) }} líneas)"));
    }

    [Fact]
    public void TemplateString_FormatUsesReportCulture()
    {
        var es = new Ctx { Culture = CultureInfo.GetCultureInfo("es-ES") };
        Assert.Equal("12,50", Text("{{ $.descuento:N2 }}", es));
        Assert.Equal("25,00", Text("{{ $.descuento * 2 : N2 }}", es));
    }

    [Fact]
    public void TemplateString_PlainPath_KeepsRawJsonOutput() =>
        Assert.Equal("12.5", Text("{{ $.descuento }}"));

    [Fact]
    public void BindingFactory_EqualsPrefix_CompilesExpression()
    {
        var row = Data.GetProperty("lineas")[0];
        var computed = BindingFactory.Create("= $.cantidad * $.precio", Data);
        var plain = BindingFactory.Create("$.codigo", Data);
        Assert.Equal(21m, computed.Evaluate(row));
        Assert.Equal("A1", plain.Evaluate(row));
    }

    [Fact]
    public void CompileCondition_AcceptsBareOrBraced()
    {
        Assert.True(BindingFactory.CompileCondition("$.descuento > 10", Data).Evaluate(new Ctx()));
        Assert.False(BindingFactory.CompileCondition("{{ $.cliente.saldo > 0 }}", Data).Evaluate(new Ctx()));
    }
}
