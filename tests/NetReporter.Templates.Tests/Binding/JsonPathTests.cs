using System.Text.Json;
using NetReporter.Templates.Binding;

namespace NetReporter.Templates.Tests.Binding;

public sealed class JsonPathTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Select_Root_ReturnsRoot()
    {
        var root = Parse("""{"a":1}""");
        var result = JsonPath.Select(root, "$");
        Assert.NotNull(result);
        Assert.Equal(JsonValueKind.Object, result.Value.ValueKind);
    }

    [Fact]
    public void Select_SimpleProperty()
    {
        var root = Parse("""{"name":"Marvin"}""");
        var result = JsonPath.Select(root, "$.name");
        Assert.Equal("Marvin", result?.GetString());
    }

    [Fact]
    public void Select_NestedPath()
    {
        var root = Parse("""{"user":{"profile":{"name":"M"}}}""");
        var result = JsonPath.Select(root, "$.user.profile.name");
        Assert.Equal("M", result?.GetString());
    }

    [Fact]
    public void Select_ArrayIndex()
    {
        var root = Parse("""{"items":["a","b","c"]}""");
        var result = JsonPath.Select(root, "$.items[1]");
        Assert.Equal("b", result?.GetString());
    }

    [Fact]
    public void Select_MissingProperty_ReturnsNull()
    {
        var root = Parse("""{"a":1}""");
        Assert.Null(JsonPath.Select(root, "$.missing"));
    }

    [Fact]
    public void Select_ArrayIndexOutOfBounds_ReturnsNull()
    {
        var root = Parse("""{"items":["a"]}""");
        Assert.Null(JsonPath.Select(root, "$.items[99]"));
    }

    [Fact]
    public void Select_NonArrayIndexing_ReturnsNull()
    {
        var root = Parse("""{"a":1}""");
        Assert.Null(JsonPath.Select(root, "$.a[0]"));
    }

    [Fact]
    public void Select_NoDollarPrefix_StillWorks()
    {
        var root = Parse("""{"a":42}""");
        var result = JsonPath.Select(root, "$.a");
        Assert.Equal(42, result?.GetInt32());
    }

    [Theory]
    [InlineData("[*]")]
    [InlineData("$.items[*]foo")]
    public void Select_InvalidWildcard_Throws(string path)
    {
        var root = Parse("""{"items":[]}""");
        Assert.ThrowsAny<ArgumentException>(() => JsonPath.Select(root, path));
    }

    [Fact]
    public void SelectMany_Wildcard_EnumeratesArray()
    {
        var root = Parse("""{"items":[{"id":1},{"id":2},{"id":3}]}""");
        var result = JsonPath.SelectMany(root, "$.items[*]");
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].GetProperty("id").GetInt32());
        Assert.Equal(3, result[2].GetProperty("id").GetInt32());
    }

    [Fact]
    public void SelectMany_WithoutWildcard_OnArray_EnumeratesItems()
    {
        var root = Parse("""{"items":[1,2,3]}""");
        var result = JsonPath.SelectMany(root, "$.items");
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void SelectMany_OnSingleValue_WrapsAsList()
    {
        var root = Parse("""{"x":42}""");
        var result = JsonPath.SelectMany(root, "$.x");
        Assert.Single(result);
        Assert.Equal(42, result[0].GetInt32());
    }

    [Fact]
    public void SelectMany_MissingArray_ReturnsEmpty()
    {
        var root = Parse("""{"a":1}""");
        var result = JsonPath.SelectMany(root, "$.missing[*]");
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("\"hello\"", "hello")]
    [InlineData("42", "42")]
    [InlineData("3.14", "3.14")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("null", "")]
    public void ToDisplayString_ConvertsScalarKinds(string json, string expected)
    {
        var el = Parse(json);
        Assert.Equal(expected, JsonPath.ToDisplayString(el));
    }

    [Fact]
    public void ToDisplayString_NullInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, JsonPath.ToDisplayString(null));
    }

    [Fact]
    public void ToBoxedValue_IntegerReturnsLong()
    {
        var result = JsonPath.ToBoxedValue(Parse("42"));
        Assert.IsType<long>(result);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void ToBoxedValue_DecimalReturnsDecimalOrDouble()
    {
        var result = JsonPath.ToBoxedValue(Parse("3.14"));
        // Puede ser decimal o double, ambos válidos para IFormattable.
        Assert.True(result is decimal or double, $"Got {result?.GetType().Name}");
    }

    [Fact]
    public void ToBoxedValue_StringReturnsString()
    {
        var result = JsonPath.ToBoxedValue(Parse("\"hola\""));
        Assert.Equal("hola", result);
    }

    [Fact]
    public void ToBoxedValue_BooleanReturnsBool()
    {
        Assert.Equal(true, JsonPath.ToBoxedValue(Parse("true")));
        Assert.Equal(false, JsonPath.ToBoxedValue(Parse("false")));
    }

    [Fact]
    public void ToBoxedValue_NullReturnsNull()
    {
        Assert.Null(JsonPath.ToBoxedValue(Parse("null")));
    }
}
