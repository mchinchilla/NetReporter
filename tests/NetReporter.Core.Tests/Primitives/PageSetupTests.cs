using NetReporter.Core.Primitives;

namespace NetReporter.Core.Tests.Primitives;

public sealed class PageSetupTests
{
    [Fact]
    public void PredefinedSizes_HaveExpectedDimensions()
    {
        Assert.Equal(612, PageSetup.Letter.Size.Width);
        Assert.Equal(792, PageSetup.Letter.Size.Height);
        Assert.Equal(612, PageSetup.Legal.Size.Width);
        Assert.Equal(1008, PageSetup.Legal.Size.Height);
    }

    [Fact]
    public void Landscape_SwapsDimensions()
    {
        var l = PageSetup.Letter.Landscape();
        Assert.Equal(792, l.Size.Width);
        Assert.Equal(612, l.Size.Height);
        Assert.Equal(PageOrientation.Landscape, l.Orientation);
    }

    [Fact]
    public void Landscape_Idempotent()
    {
        var once = PageSetup.Letter.Landscape();
        var twice = once.Landscape();
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Portrait_RevertsLandscape()
    {
        var land = PageSetup.A4.Landscape();
        var back = land.Portrait();
        Assert.Equal(PageSetup.A4.Size, back.Size);
        Assert.Equal(PageOrientation.Portrait, back.Orientation);
    }

    [Fact]
    public void WithMargins_Uniform()
    {
        var p = PageSetup.Letter.WithMargins(20);
        Assert.Equal(20, p.Margins.Left);
        Assert.Equal(20, p.Margins.Top);
        Assert.Equal(20, p.Margins.Right);
        Assert.Equal(20, p.Margins.Bottom);
    }

    [Fact]
    public void WithMargins_Thickness()
    {
        var t = new Thickness(10, 20);    // horizontal=10, vertical=20
        var p = PageSetup.Letter.WithMargins(t);
        Assert.Equal(10, p.Margins.Left);
        Assert.Equal(10, p.Margins.Right);
        Assert.Equal(20, p.Margins.Top);
        Assert.Equal(20, p.Margins.Bottom);
    }

    [Fact]
    public void ContentWidth_ExcludesHorizontalMargins()
    {
        var p = PageSetup.Letter.WithMargins(new Thickness(10, 20));
        Assert.Equal(612 - 20, p.ContentWidth);
        Assert.Equal(792 - 40, p.ContentHeight);
    }

    [Fact]
    public void Custom_BuildsPageSetup()
    {
        var p = PageSetup.Custom(300, 500);
        Assert.Equal(300, p.Size.Width);
        Assert.Equal(500, p.Size.Height);
    }

    [Fact]
    public void ContentOrigin_EqualsTopLeftMargins()
    {
        var p = PageSetup.Letter.WithMargins(new Thickness(15, 25));
        Assert.Equal(15, p.ContentOrigin.X);
        Assert.Equal(25, p.ContentOrigin.Y);
    }
}
