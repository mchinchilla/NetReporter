using System.Collections.Concurrent;
using System.Text;
using NetReporter.Core.Styles;
using SkiaSharp;

namespace NetReporter.Svg;

/// <summary>Tramo de texto que se dibuja (y se mide) con una sola tipografía.</summary>
public readonly record struct SkiaTextRun(string Text, SKTypeface Typeface);

/// <summary>
/// Resolución de tipografías compartida por el medidor (<see cref="SkiaTextMeasurer"/>) y los
/// renderers SVG/PDF, para que lo que se mide y lo que se pinta sea siempre la misma fuente.
/// Si la fuente pedida no tiene un carácter (p. ej. "▸" en Helvetica), ese tramo usa la fuente
/// de respaldo que el sistema proponga para él.
/// </summary>
public static class SkiaFonts
{
    private static readonly ConcurrentDictionary<(string Family, FontWeight Weight, FontStyleKind Italic), SKTypeface>
        Typefaces = new();

    // Una instancia por fuente física: dos familias pedidas que el sistema resuelve a la misma
    // fuente (p. ej. "Helvetica" y "Arial" → Liberation Sans en Linux) comparten SKTypeface, y el
    // PdfRenderer las embebe una sola vez.
    private static readonly ConcurrentDictionary<string, SKTypeface> Canonical = new();

    private static readonly ConcurrentDictionary<(SKTypeface Primary, int CodePoint), SKTypeface?> Fallbacks = new();

    private static readonly ConcurrentDictionary<(SKTypeface Typeface, float Size), GlyphAdvances> AdvanceTables = new();

    public static SKTypeface Resolve(ResolvedStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return Resolve(style.FontFamily, style.Weight, style.Italic);
    }

    /// <summary>
    /// SKFont para medir y dibujar. Sin hinting y con métricas lineales: en Linux FreeType redondea
    /// por defecto el avance de cada glifo a puntos enteros (una línea a 7 pt sale ~5 % más ancha y
    /// con espaciado irregular), algo pensado para pantallas, no para PDF/SVG vectoriales.
    /// En macOS no cambia nada: CoreText ya entrega avances lineales.
    /// </summary>
    public static SKFont CreateFont(SKTypeface typeface, float size) =>
        new(typeface, size) { Hinting = SKFontHinting.None, LinearMetrics = true, Subpixel = true };

    /// <summary>
    /// Tabla de avances de glifo de esa tipografía a ese tamaño, compartida por todo el proceso.
    /// Se identifica por la instancia de <see cref="SKTypeface"/>: las que devuelve <see cref="Resolve(ResolvedStyle)"/>
    /// (y las de respaldo) son canónicas y viven lo que el proceso.
    /// </summary>
    public static GlyphAdvances Advances(SKTypeface typeface, float size)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        return AdvanceTables.TryGetValue((typeface, size), out var table)
            ? table
            : AdvanceTables.GetOrAdd((typeface, size), static (key, tf) => new GlyphAdvances(tf, key.Size), typeface);
    }

    public static SKTypeface Resolve(string family, FontWeight weight, FontStyleKind italic) =>
        Typefaces.GetOrAdd((family, weight, italic), static k => Canonicalize(Load(k.Family, k.Weight, k.Italic)));

    // === Fuentes registradas ===

    /// <summary>Fuente cargada por la aplicación, con la familia por la que se la pide.</summary>
    private sealed record RegisteredFont(string Family, SKTypeface Typeface);

    private static readonly Lock RegistrationLock = new();
    private static volatile RegisteredFont[] _registered = [];
    private static int _generation;

    /// <summary>
    /// Cambia cada vez que se registra una fuente: quien guarde tipografías resueltas por familia
    /// (<see cref="SkiaTextMeasurer"/>) descarta su cache al verlo cambiar.
    /// </summary>
    internal static int Generation => Volatile.Read(ref _generation);

    /// <summary>
    /// Registra una fuente TrueType/OpenType (<c>.ttf</c>, <c>.otf</c>; de un <c>.ttc</c> se registran
    /// todas sus caras) para que <see cref="Resolve(ResolvedStyle)"/> la use sin pasar por el
    /// administrador de fuentes del sistema. Es lo que hace falta con el native
    /// <c>SkiaSharp.NativeAssets.Linux.NoDependencies</c>, que no trae fontconfig y no ve ninguna
    /// fuente instalada.
    /// <para>
    /// Una familia registrada tiene prioridad sobre la del sistema con el mismo nombre, y las
    /// registradas también cubren los genéricos (<c>sans-serif</c> / <c>serif</c> / <c>monospace</c>)
    /// antes que el sistema: "Helvetica" cae en una sans registrada si el host no tiene ni Helvetica
    /// ni Arial. Registrar al arrancar, antes del primer render: un documento que se esté
    /// generando mientras se registra puede medir con una fuente y pintar con otra.
    /// </para>
    /// </summary>
    /// <param name="path">Archivo de la fuente.</param>
    /// <param name="family">
    /// Familia con la que se la pide en los estilos; por defecto la que declara la fuente
    /// (p. ej. "Liberation Sans"). El peso y la inclinación siempre se leen de la fuente.
    /// </param>
    /// <returns>Las caras registradas.</returns>
    public static IReadOnlyList<SKTypeface> RegisterFont(string path, string? family = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return RegisterFont(File.ReadAllBytes(path), family, path);
    }

    /// <summary>Igual que <see cref="RegisterFont(string, string?)"/>, leyendo la fuente de un stream.</summary>
    /// <param name="stream">Contenido de la fuente; se lee completo y no se cierra.</param>
    /// <param name="family">Familia con la que se la pide; por defecto la que declara la fuente.</param>
    public static IReadOnlyList<SKTypeface> RegisterFont(Stream stream, string? family = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return RegisterFont(buffer.ToArray(), family, "stream");
    }

    /// <summary>Igual que <see cref="RegisterFont(string, string?)"/>, con la fuente ya en memoria.</summary>
    /// <param name="data">Contenido de la fuente.</param>
    /// <param name="family">Familia con la que se la pide; por defecto la que declara la fuente.</param>
    public static IReadOnlyList<SKTypeface> RegisterFont(byte[] data, string? family = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return RegisterFont(data, family, "byte[]");
    }

    /// <summary>
    /// Registra todas las fuentes <c>.ttf</c> / <c>.otf</c> / <c>.ttc</c> de un directorio (p. ej.
    /// <c>/usr/share/fonts</c>) con la familia que declara cada una. Los archivos que no son una
    /// fuente válida se omiten.
    /// </summary>
    /// <returns>Las caras registradas.</returns>
    public static IReadOnlyList<SKTypeface> RegisterFontDirectory(string directory, bool recursive = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var result = new List<SKTypeface>();
        foreach (var file in Directory.EnumerateFiles(directory, "*", option).Order(StringComparer.Ordinal))
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension is not (".ttf" or ".otf" or ".ttc")) continue;
            try
            {
                result.AddRange(RegisterFont(file));
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // Un archivo ilegible no debe impedir registrar el resto del directorio.
            }
        }
        return result;
    }

    private static IReadOnlyList<SKTypeface> RegisterFont(byte[] bytes, string? family, string source)
    {
        // Un .ttc trae varias caras; un .ttf/.otf devuelve null a partir del índice 1.
        var faces = new List<SKTypeface>();
        using (var data = SKData.CreateCopy(bytes))
        {
            for (var index = 0; index < 256; index++)
            {
                var typeface = SKTypeface.FromData(data, index);
                if (typeface is null) break;
                if (typeface.GlyphCount > 0) faces.Add(typeface);
                else typeface.Dispose();
            }
        }
        if (faces.Count == 0)
            throw new ArgumentException($"'{source}' no contiene una fuente que SkiaSharp pueda leer.", nameof(bytes));

        lock (RegistrationLock)
        {
            var registered = new List<RegisteredFont>(_registered);
            foreach (var face in faces)
            {
                registered.Add(new RegisteredFont(family ?? face.FamilyName, face));
                // La registrada pasa a ser la instancia canónica aunque el sistema tenga la misma fuente.
                Canonical[CanonicalKey(face)] = face;
            }
            _registered = [.. registered];
            Typefaces.Clear();
            Fallbacks.Clear();
            Interlocked.Increment(ref _generation);
        }
        return faces;
    }

    /// <summary>Solo para tests: deja el registro como al arrancar el proceso.</summary>
    internal static void ResetRegisteredFonts()
    {
        lock (RegistrationLock)
        {
            foreach (var font in _registered)
                Canonical.TryRemove(new KeyValuePair<string, SKTypeface>(CanonicalKey(font.Typeface), font.Typeface));
            _registered = [];
            Typefaces.Clear();
            Fallbacks.Clear();
            Interlocked.Increment(ref _generation);
        }
    }

    /// <summary>La cara registrada de esa familia que mejor coincide con el peso y la inclinación.</summary>
    private static SKTypeface? FindRegistered(string family, SKFontStyle style) =>
        BestStyle(_registered.Where(f => string.Equals(f.Family, family, StringComparison.OrdinalIgnoreCase)), style);

    /// <summary>Una registrada del mismo genérico (sans-serif / serif / monospace) que la familia pedida.</summary>
    private static SKTypeface? FindRegisteredGeneric(string generic, SKFontStyle style) =>
        BestStyle(_registered.Where(f => GenericFamily(f.Family) == generic), style);

    private static SKTypeface? BestStyle(IEnumerable<RegisteredFont> candidates, SKFontStyle style)
    {
        var bold = style.Weight >= (int)SKFontStyleWeight.SemiBold;
        var italic = style.Slant != SKFontStyleSlant.Upright;
        SKTypeface? best = null;
        var bestScore = int.MaxValue;
        foreach (var candidate in candidates)   // en orden de registro: ante un empate gana la primera
        {
            var typeface = candidate.Typeface;
            var score = ((typeface.FontWeight >= (int)SKFontStyleWeight.SemiBold) == bold ? 0 : 2)
                        + ((typeface.FontSlant != SKFontStyleSlant.Upright) == italic ? 0 : 1);
            if (score >= bestScore) continue;
            (best, bestScore) = (typeface, score);
        }
        return best;
    }

    /// <summary>
    /// Null si Skia no tiene glifos para esa tipografía. Con el native sin fontconfig el administrador
    /// de fuentes está vacío y devuelve tipografías sin glifos en lugar de null.
    /// </summary>
    private static SKTypeface? Usable(SKTypeface? typeface) => typeface is { GlyphCount: > 0 } ? typeface : null;

    // Las tres familias estándar de PDF no suelen existir en Linux ni en Windows; se prueba su
    // equivalente métrico habitual, que fontconfig resuelve a Liberation si está instalada.
    private static readonly Dictionary<string, string> MetricEquivalents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Helvetica"] = "Arial",
        ["Courier"] = "Courier New",
        ["Times"] = "Times New Roman"
    };

    private static SKTypeface Load(string family, FontWeight weight, FontStyleKind italic)
    {
        var slant = italic == FontStyleKind.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var skWeight = weight == FontWeight.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var style = new SKFontStyle(skWeight, SKFontStyleWidth.Normal, slant);
        var fonts = SKFontManager.Default;

        // MatchFamily devuelve null cuando la familia no existe. FromFamilyName, en cambio, cae a la
        // fuente por defecto y en Linux la entrega en regular aunque se pida negrita, así que solo
        // se usa como último recurso. Cada paso conserva el peso y la inclinación pedidos.
        // Las fuentes registradas por la aplicación van antes que las del sistema en cada nivel.
        var equivalent = MetricEquivalents.GetValueOrDefault(family);
        var generic = GenericFamily(family);
        return FindRegistered(family, style)
               ?? Usable(fonts.MatchFamily(family, style))
               ?? (equivalent is null ? null : FindRegistered(equivalent, style) ?? Usable(fonts.MatchFamily(equivalent, style)))
               ?? FindRegisteredGeneric(generic, style)
               ?? Usable(fonts.MatchFamily(generic, style))                         // Linux: sans-serif / serif / monospace
               ?? Usable(fonts.MatchFamily(SKTypeface.Default.FamilyName, style))   // macOS/Windows no conocen los genéricos
               ?? Usable(SKTypeface.FromFamilyName(family, skWeight, SKFontStyleWidth.Normal, slant))
               ?? Usable(SKTypeface.Default)
               ?? BestStyle(_registered, style)
               // fontconfig con solo fonts-liberation: los genéricos y la fuente por defecto no
               // resuelven a nada, pero las familias instaladas sí. Mismo genérico primero.
               ?? InstalledFamily(fonts, style, f => GenericFamily(f) == generic)
               ?? InstalledFamily(fonts, style, static _ => true)
               // Sin esto el documento sale sin texto y sin ningún error (native sin fontconfig).
               ?? throw new InvalidOperationException(
                   $"No hay ninguna fuente disponible para '{family}': SkiaSharp no encontró fuentes en el sistema. " +
                   "Registre fuentes al arrancar (LiberationFonts.Register() del paquete NetReporter.Fonts.Liberation, " +
                   "o SkiaFonts.RegisterFontDirectory/RegisterFont), o use SkiaSharp.NativeAssets.Linux con " +
                   "libfontconfig1 y fonts-liberation instalados.");
    }

    private static SKTypeface? InstalledFamily(SKFontManager fonts, SKFontStyle style, Func<string, bool> accept)
    {
        foreach (var family in fonts.FontFamilies.Order(StringComparer.Ordinal))   // orden estable
            if (accept(family) && Usable(fonts.MatchFamily(family, style)) is { } typeface)
                return typeface;
        return null;
    }

    internal static string GenericFamily(string family)
    {
        var name = family.ToLowerInvariant();
        if (name.Contains("mono") || name.Contains("courier") || name.Contains("consolas") || name.Contains("menlo"))
            return "monospace";
        if ((name.Contains("serif") && !name.Contains("sans")) || name.Contains("times") || name.Contains("georgia"))
            return "serif";
        return "sans-serif";
    }

    /// <summary>
    /// Parte <paramref name="text"/> en tramos por tipografía. El caso común (la fuente principal
    /// tiene todos los caracteres) devuelve un único tramo sin recorrer el texto.
    /// </summary>
    public static IReadOnlyList<SkiaTextRun> SplitRuns(string text, SKTypeface primary)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(primary);
        using var font = new SKFont(primary);
        return SplitRuns(text, primary, font);
    }

    /// <summary>
    /// Igual que <see cref="SplitRuns(string, SKTypeface)"/> pero con un <see cref="SKFont"/> de la
    /// tipografía principal ya creado: el painter y el PdfRenderer reutilizan uno por documento en
    /// vez de crear uno por texto. La tipografía se pasa aparte de su SKFont porque
    /// <see cref="SKFont.Typeface"/> puede devolver otro envoltorio administrado de la misma fuente,
    /// y el PdfRenderer identifica las fuentes por referencia.
    /// </summary>
    public static IReadOnlyList<SkiaTextRun> SplitRuns(string text, SKTypeface primary, SKFont primaryFont)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(primaryFont);
        if (text.Length == 0 || primaryFont.ContainsGlyphs(text))
            return [new SkiaTextRun(text, primary)];

        var runs = new List<SkiaTextRun>();
        var current = new StringBuilder();
        var currentTypeface = primary;

        foreach (var rune in text.EnumerateRunes())
        {
            var typeface = TypefaceFor(rune, primary, primaryFont);
            if (!ReferenceEquals(typeface, currentTypeface) && current.Length > 0)
            {
                runs.Add(new SkiaTextRun(current.ToString(), currentTypeface));
                current.Clear();
            }
            currentTypeface = typeface;
            current.Append(rune.ToString());
        }

        if (current.Length > 0) runs.Add(new SkiaTextRun(current.ToString(), currentTypeface));
        return runs;
    }

    /// <summary>Ancho de <paramref name="text"/> con <paramref name="font"/>, sumando los tramos de respaldo.</summary>
    public static float MeasureText(string text, SKFont font)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        return text.Length == 0 ? 0 : MeasureText(text, font.Typeface, font);
    }

    /// <summary>
    /// Igual que <see cref="MeasureText(string, SKFont)"/> con la tipografía de la fuente ya a mano
    /// (<see cref="SKFont.Typeface"/> cuesta una búsqueda en el registro de objetos de SkiaSharp).
    /// Los anchos salen de <see cref="Advances"/> y no de <c>SKFont.MeasureText</c>:
    /// mismo resultado sin pasar por el lock global del strike cache de Skia.
    /// </summary>
    public static float MeasureText(string text, SKTypeface typeface, SKFont font)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(typeface);
        ArgumentNullException.ThrowIfNull(font);
        if (text.Length == 0) return 0;
        if (font.ContainsGlyphs(text)) return Advances(typeface, font.Size).Width(text);

        float width = 0;
        foreach (var run in SplitRuns(text, typeface, font))
            width += Advances(run.Typeface, font.Size).Width(run.Text);
        return width;
    }

    private static SKTypeface TypefaceFor(Rune rune, SKTypeface primary, SKFont primaryFont)
    {
        // Controles y espacios nunca justifican buscar otra fuente: van con la principal.
        if (Rune.IsControl(rune) || Rune.IsWhiteSpace(rune) || primaryFont.ContainsGlyph(rune.Value))
            return primary;

        return Fallbacks.GetOrAdd((primary, rune.Value), static key =>
            FindFallback(key.Primary, key.CodePoint) is { } fallback ? Canonicalize(fallback) : null) ?? primary;
    }

    private static readonly Lazy<string[]> SystemFamilies = new(() =>
        ["sans-serif", "serif", "monospace", .. SKFontManager.Default.FontFamilies.Order(StringComparer.Ordinal)]);

    private static SKTypeface? FindFallback(SKTypeface primary, int codePoint)
    {
        var fonts = SKFontManager.Default;
        var match = fonts.MatchCharacter(primary.FamilyName, primary.FontStyle, null, codePoint);
        if (Covers(match, codePoint)) return match;

        // Las registradas primero: son las que la aplicación eligió (y las únicas sin fontconfig).
        foreach (var registered in _registered)
            if (Covers(registered.Typeface, codePoint)) return registered.Typeface;

        // En Linux (fontconfig) MatchCharacter devuelve null aunque haya fuentes instaladas con el
        // carácter, así que se recorren las familias del sistema (genéricas primero, luego en orden
        // alfabético para que la elección sea estable) con el estilo de la fuente principal.
        foreach (var family in SystemFamilies.Value)
        {
            var candidate = fonts.MatchFamily(family, primary.FontStyle);
            if (Covers(candidate, codePoint)) return candidate;
        }
        return null;
    }

    private static bool Covers(SKTypeface? typeface, int codePoint)
    {
        if (typeface is null) return false;
        using var font = new SKFont(typeface);
        return font.ContainsGlyph(codePoint);
    }

    private static SKTypeface Canonicalize(SKTypeface typeface) => Canonical.GetOrAdd(CanonicalKey(typeface), typeface);

    private static string CanonicalKey(SKTypeface typeface) =>
        $"{typeface.FamilyName}|{typeface.FontWeight}|{typeface.FontWidth}|{(int)typeface.FontSlant}";
}
