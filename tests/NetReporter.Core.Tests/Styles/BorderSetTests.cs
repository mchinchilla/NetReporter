using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Tests.Styles;

public sealed class BorderSetTests
{
    [Fact]
    public void All_SetsFourSides()
    {
        var b = BorderSet.All(1.5, Color.Black);
        Assert.NotNull(b.Left); Assert.NotNull(b.Top); Assert.NotNull(b.Right); Assert.NotNull(b.Bottom);
        Assert.Equal(1.5, b.Left!.Value.Thickness);
    }

    [Fact]
    public void OnlyTop_SetsOnlyTop()
    {
        var b = BorderSet.OnlyTop(1, Color.Black);
        Assert.NotNull(b.Top);
        Assert.Null(b.Bottom);
        Assert.Null(b.Left);
        Assert.Null(b.Right);
    }

    [Fact]
    public void OnlyBottom_SetsOnlyBottom()
    {
        var b = BorderSet.OnlyBottom(0.5, Color.Black);
        Assert.NotNull(b.Bottom);
        Assert.Null(b.Top);
    }

    [Fact]
    public void Horizontal_SetsTopAndBottom()
    {
        var b = BorderSet.Horizontal(0.75, Color.Black);
        Assert.NotNull(b.Top); Assert.NotNull(b.Bottom);
        Assert.Null(b.Left); Assert.Null(b.Right);
        Assert.Equal(0.75, b.Top!.Value.Thickness);
    }
}
