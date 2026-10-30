using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace NetReporter.Pdf;

/// <summary>
/// Fuentes recortadas compartidas por todos los documentos del proceso
/// (<see cref="PdfRendererOptions.ShareFontSubsets"/>). Por tipografía se mantiene un único recorte
/// con todos los glifos vistos hasta ahora, que crece cuando un documento trae uno nuevo: los
/// primeros documentos lo completan y después converge al repertorio real del proceso. No se
/// siembra con ningún rango: sembrar ASCII o Latin-1 inflaba la fuente embebida (sobre todo la
/// negrita, que usa pocos glifos) y lo que Skia comprime por documento, y restaba más de lo que
/// ahorraba. Reutilizar la misma <see cref="SKTypeface"/> recortada
/// evita recortar y crear una tipografía por documento y, sobre todo, mantiene caliente el strike
/// cache de Skia: con un recorte nuevo por documento, cada glifo que dibuja el backend PDF es un
/// fallo de cache que pasa por CoreText/FreeType bajo lock.
/// <para>
/// Un recorte sustituido no se libera explícitamente: los documentos en curso lo siguen
/// referenciando y el GC lo recoge cuando el último termina.
/// </para>
/// </summary>
internal static class SharedFontSubsets
{
    private static readonly ConcurrentDictionary<SKTypeface, Entry> Entries = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Recorte de <paramref name="original"/> que cubre <paramref name="glyphs"/>, o null si la fuente
    /// no se puede recortar (se embebe completa).
    /// </summary>
    public static SKTypeface? Get(SKTypeface original, IReadOnlyCollection<ushort> glyphs)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(glyphs);
        var entry = Entries.GetOrAdd(original, static typeface => new Entry(typeface));
        lock (entry)
        {
            if (entry.Unsubsettable) return null;
            if (entry.Subset is null || !entry.Covers(glyphs)) entry.Grow(glyphs);
            return entry.Subset;
        }
    }

    private sealed class Entry(SKTypeface original)
    {
        private readonly bool[] _covered = new bool[Math.Max(1, original.GlyphCount)];
        private byte[]? _fontData;
        private int _faceIndex;
        private bool _fontDataLoaded;

        public SKTypeface? Subset { get; private set; }
        public bool Unsubsettable { get; private set; }

        public bool Covers(IReadOnlyCollection<ushort> glyphs)
        {
            foreach (var glyph in glyphs)
                if (glyph < _covered.Length && !_covered[glyph]) return false;
            return true;
        }

        public void Grow(IReadOnlyCollection<ushort> glyphs)
        {
            _covered[0] = true;   // .notdef siempre va en el recorte: un carácter que la fuente no tiene no debe hacerlo crecer
            foreach (var glyph in glyphs)
                if (glyph < _covered.Length) _covered[glyph] = true;
            Rebuild();
        }

        private void Rebuild()
        {
            var keep = new List<ushort>();
            for (var glyph = 0; glyph < _covered.Length; glyph++)
                if (_covered[glyph]) keep.Add((ushort)glyph);

            byte[]? bytes;
            try
            {
                bytes = LoadFontData() ? TrueTypeSubsetter.Subset(_fontData, _faceIndex, keep) : null;
            }
            catch (Exception)
            {
                bytes = null;   // misma política que el recorte por documento: ante la duda, fuente completa
            }
            if (bytes is null) { GiveUp(); return; }

            var data = SKData.CreateCopy(bytes);
            var subset = SKTypeface.FromData(data);
            if (subset is null || !FontSubsetPlan.SameAdvances(original, subset, CollectionsMarshal.AsSpan(keep)))
            {
                subset?.Dispose();
                data.Dispose();
                GiveUp();
                return;
            }

            // La SKTypeface conserva su propia referencia a los datos; el recorte anterior queda para el GC.
            Subset = subset;
        }

        private void GiveUp()
        {
            Unsubsettable = true;
            Subset = null;
            _fontData = null;
        }

        private bool LoadFontData()
        {
            if (_fontDataLoaded) return _fontData is not null;
            _fontDataLoaded = true;
            using var stream = original.OpenStream(out _faceIndex);
            if (stream is null) return false;
            using var source = SKData.Create(stream);
            if (source is null) return false;
            _fontData = source.ToArray();
            return true;
        }
    }
}
