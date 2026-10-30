using NetReporter.Svg;
using SkiaSharp;

namespace NetReporter.Fonts.Liberation;

/// <summary>
/// Fuentes Liberation 2.1.5 (Sans, Serif y Mono, cada una en regular, negrita, cursiva y negrita
/// cursiva) embebidas en el ensamblado. Son métricamente compatibles con Arial/Helvetica, Times New
/// Roman y Courier New, así que las plantillas que piden esas familias cortan las líneas igual que
/// en Windows/macOS.
/// <para>
/// Para hosts donde SkiaSharp no ve fuentes del sistema: el native
/// <c>SkiaSharp.NativeAssets.Linux.NoDependencies</c> (sin fontconfig) o contenedores sin fuentes.
/// Sin fuentes, el PdfRenderer no tendría con qué escribir el texto.
/// </para>
/// </summary>
public static class LiberationFonts
{
    private static readonly Lazy<IReadOnlyList<SKTypeface>> Registered = new(RegisterAll);

    /// <summary>
    /// Registra las 12 caras en <see cref="SkiaFonts"/>. Llamar una vez al arrancar, antes del primer
    /// render; las llamadas siguientes no hacen nada y devuelven las mismas caras.
    /// "Helvetica"/"Arial" resuelven a Liberation Sans, "Times"/"Times New Roman" a Liberation Serif
    /// y "Courier"/"Courier New" a Liberation Mono cuando el host no tiene la fuente pedida.
    /// </summary>
    public static IReadOnlyList<SKTypeface> Register() => Registered.Value;

    private static IReadOnlyList<SKTypeface> RegisterAll()
    {
        var assembly = typeof(LiberationFonts).Assembly;
        var result = new List<SKTypeface>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)
                               ?? throw new InvalidOperationException($"Falta el recurso '{name}'.");
            result.AddRange(SkiaFonts.RegisterFont(stream));
        }
        return result;
    }
}
