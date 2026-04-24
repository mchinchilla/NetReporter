using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Tests.Styles;

public sealed class StyleSheetTests
{
    [Fact]
    public void Resolve_Default_UsesBuiltInFallbacks()
    {
        var sheet = new StyleSheetBuilder().Add("Default", _ => { }).Build();
        var r = sheet.Resolve(StyleRef.Default);
        Assert.Equal("Helvetica", r.FontFamily);
        Assert.Equal(10, r.FontSize);
        Assert.Equal(FontWeight.Normal, r.Weight);
        Assert.Equal(Color.Black, r.Foreground);
    }

    [Fact]
    public void Resolve_BasedOnChain_AppliesOverrides()
    {
        var sheet = new StyleSheetBuilder()
            .Add("Default", s => s.FontFamily("Helvetica").FontSize(10).Foreground(Color.Black))
            .Add("Title",   s => s.BasedOn("Default").FontSize(18).Bold())
            .Build();

        var r = sheet.Resolve(new StyleRef("Title"));
        Assert.Equal("Helvetica", r.FontFamily);       // heredado
        Assert.Equal(18, r.FontSize);                  // override
        Assert.Equal(FontWeight.Bold, r.Weight);       // override
    }

    [Fact]
    public void Resolve_Caches_SameInstance()
    {
        var sheet = new StyleSheetBuilder()
            .Add("Default", s => s.FontFamily("Arial"))
            .Build();
        var a = sheet.Resolve(StyleRef.Default);
        var b = sheet.Resolve(StyleRef.Default);
        Assert.Same(a, b);
    }

    [Fact]
    public void Resolve_UnknownStyle_ReturnsBuiltInDefault()
    {
        var sheet = StyleSheet.Minimal;
        var r = sheet.Resolve(new StyleRef("does-not-exist"));
        Assert.Equal("Helvetica", r.FontFamily);
    }

    [Fact]
    public void Resolve_DetectsCyclicInheritance()
    {
        var sheet = new StyleSheetBuilder()
            .Add("A", s => s.BasedOn("B"))
            .Add("B", s => s.BasedOn("C"))
            .Add("C", s => s.BasedOn("A"))      // ciclo
            .Build();

        Assert.ThrowsAny<Exception>(() => sheet.Resolve(new StyleRef("A")));
    }

    [Fact]
    public void StyleRef_Empty_ResolvesAsDefault()
    {
        Assert.True(default(StyleRef).IsEmpty);
        Assert.False(StyleRef.Default.IsEmpty);
    }

    [Fact]
    public void BasedOn_PropagatesBackgroundAndPadding()
    {
        var sheet = new StyleSheetBuilder()
            .Add("Default",     s => s.FontSize(10))
            .Add("TableHeader", s => s.BasedOn("Default")
                                       .Background(Color.FromHex("#F1F5F9"))
                                       .Padding(new Thickness(4, 3))
                                       .Bold())
            .Build();

        var r = sheet.Resolve(new StyleRef("TableHeader"));
        Assert.Equal(Color.FromHex("#F1F5F9"), r.Background);
        Assert.Equal(4, r.Padding.Left);
        Assert.Equal(3, r.Padding.Top);
        Assert.Equal(FontWeight.Bold, r.Weight);
    }
}
