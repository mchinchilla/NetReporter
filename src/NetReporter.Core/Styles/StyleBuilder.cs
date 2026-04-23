using NetReporter.Core.Primitives;

namespace NetReporter.Core.Styles;

public sealed class StyleBuilder
{
    private Style _style = new();

    public StyleBuilder BasedOn(string name)        { _style = _style with { BasedOn = name };  return this; }
    public StyleBuilder FontFamily(string f)        { _style = _style with { FontFamily = f };  return this; }
    public StyleBuilder FontSize(double s)          { _style = _style with { FontSize = s };    return this; }
    public StyleBuilder Bold()                      { _style = _style with { Weight = FontWeight.Bold };       return this; }
    public StyleBuilder Italic()                    { _style = _style with { Italic = FontStyleKind.Italic };  return this; }
    public StyleBuilder Foreground(Color c)         { _style = _style with { Foreground = c };  return this; }
    public StyleBuilder Background(Color c)         { _style = _style with { Background = c };  return this; }
    public StyleBuilder Padding(Thickness t)        { _style = _style with { Padding = t };     return this; }
    public StyleBuilder Padding(double uniform)     { _style = _style with { Padding = new Thickness(uniform) }; return this; }
    public StyleBuilder Border(BorderSet b)         { _style = _style with { Border = b };      return this; }
    public StyleBuilder Align(TextAlignment a)      { _style = _style with { TextAlign = a };   return this; }
    public StyleBuilder VAlign(VerticalAlignment a) { _style = _style with { VerticalAlign = a }; return this; }
    public StyleBuilder Format(string fmt)          { _style = _style with { NumberFormat = fmt }; return this; }

    internal Style Build() => _style;
}

public sealed class StyleSheetBuilder
{
    private readonly Dictionary<string, Style> _styles = new(StringComparer.Ordinal);

    public StyleSheetBuilder Add(string name, Action<StyleBuilder> configure)
    {
        var sb = new StyleBuilder();
        configure(sb);
        _styles[name] = sb.Build();
        return this;
    }

    public StyleSheet Build() => new(_styles);
}
