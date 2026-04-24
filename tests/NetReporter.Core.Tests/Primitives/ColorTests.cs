using NetReporter.Core.Primitives;

namespace NetReporter.Core.Tests.Primitives;

public sealed class ColorTests
{
    [Theory]
    [InlineData("#ABC",      0xAA, 0xBB, 0xCC, 255)]
    [InlineData("ABC",       0xAA, 0xBB, 0xCC, 255)]
    [InlineData("#FF00AA",   255, 0, 170, 255)]
    [InlineData("#12345678", 0x12, 0x34, 0x56, 0x78)]
    [InlineData("#fF00aA",   255, 0, 170, 255)]
    public void FromHex_ParsesValidFormats(string hex, int r, int g, int b, int a)
    {
        var c = Color.FromHex(hex);
        Assert.Equal((byte)r, c.R);
        Assert.Equal((byte)g, c.G);
        Assert.Equal((byte)b, c.B);
        Assert.Equal((byte)a, c.A);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#GGHHII")]
    [InlineData("123456789")]
    public void FromHex_RejectsInvalid(string hex)
    {
        Assert.Throws<ArgumentException>(() => Color.FromHex(hex));
    }

    [Fact]
    public void FromHex_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Color.FromHex(null!));
    }

    [Fact]
    public void ToHexString_Round_Trips()
    {
        var c = Color.FromHex("#0F172A");
        Assert.Equal("#0F172A", c.ToHexString());
    }

    [Fact]
    public void PredefinedColors()
    {
        Assert.Equal(0, Color.Black.R);
        Assert.Equal(255, Color.White.R);
        Assert.Equal(0, Color.Transparent.A);
    }
}
