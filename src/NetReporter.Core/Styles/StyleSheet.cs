using System.Collections.Concurrent;
using NetReporter.Core.Primitives;

namespace NetReporter.Core.Styles;

public sealed class StyleSheet
{
    // Default global: el "fallback definitivo" si nada más está especificado.
    private static readonly ResolvedStyle BuiltInDefault = new(
        FontFamily: "Helvetica",
        FontSize: 10,
        Weight: FontWeight.Normal,
        Italic: FontStyleKind.Normal,
        Foreground: Color.Black,
        Background: Color.Transparent,
        TextAlign: TextAlignment.Left,
        VerticalAlign: VerticalAlignment.Top,
        Padding: Thickness.Zero,
        Border: null,
        NumberFormat: null);

    private readonly IReadOnlyDictionary<string, Style> _styles;
    private readonly ConcurrentDictionary<string, ResolvedStyle> _cache = new(StringComparer.Ordinal);

    public StyleSheet(IReadOnlyDictionary<string, Style> styles)
    {
        _styles = styles ?? throw new ArgumentNullException(nameof(styles));
    }

    public ResolvedStyle Resolve(StyleRef @ref)
    {
        var name = @ref.IsEmpty ? "Default" : @ref.Name;
        return _cache.GetOrAdd(name, ResolveCore);
    }

    /// <summary>True if a style with this exact name is defined (not counting the built-in fallback).
    /// Lets callers (e.g. per-row typed styling) decide whether to use a mapped style or fall back.</summary>
    public bool Contains(string name) => _styles.ContainsKey(name);

    private ResolvedStyle ResolveCore(string name)
    {
        // Detección de ciclos explícita (HashSet.Add devuelve false si ya existe →
        // detectamos y lanzamos en lugar de salir silenciosamente). Max 16 niveles.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var chain = new Stack<Style>();
        var current = name;

        while (current is not null)
        {
            if (!visited.Add(current))
                throw new InvalidOperationException(
                    $"Ciclo de herencia detectado al resolver '{name}': '{current}' aparece dos veces en la cadena.");

            if (!_styles.TryGetValue(current, out var style)) break;
            chain.Push(style);
            current = style.BasedOn;

            if (visited.Count > 16)
                throw new InvalidOperationException(
                    $"Cadena de herencia excesiva (>16 niveles) en estilo '{name}'.");
        }

        // Aplicar de la base hacia el derivado (último gana).
        var r = BuiltInDefault;
        while (chain.Count > 0)
        {
            var s = chain.Pop();
            r = r with
            {
                FontFamily    = s.FontFamily    ?? r.FontFamily,
                FontSize      = s.FontSize      ?? r.FontSize,
                Weight        = s.Weight        ?? r.Weight,
                Italic        = s.Italic        ?? r.Italic,
                Foreground    = s.Foreground    ?? r.Foreground,
                Background    = s.Background    ?? r.Background,
                TextAlign     = s.TextAlign     ?? r.TextAlign,
                VerticalAlign = s.VerticalAlign ?? r.VerticalAlign,
                Padding       = s.Padding       ?? r.Padding,
                Border        = s.Border        ?? r.Border,
                NumberFormat  = s.NumberFormat  ?? r.NumberFormat
            };
        }

        return r;
    }

    public static StyleSheet Minimal { get; } = new StyleSheetBuilder()
        .Add("Default", _ => { })
        .Build();
}
