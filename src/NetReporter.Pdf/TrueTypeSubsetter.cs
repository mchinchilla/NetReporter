using System.Buffers.Binary;

namespace NetReporter.Pdf;

/// <summary>
/// Recorta una fuente TrueType a los glifos que usa un documento. El SkiaSharp de NuGet se
/// compila sin subsetting y embebe cada fuente completa en el PDF (~200 KB por variante); si le
/// damos esta versión recortada, embebe solo lo que hay aquí.
/// <para>
/// Los IDs de glifo se conservan: los glifos no usados quedan vacíos en <c>glyf</c>/<c>loca</c> y
/// <c>cmap</c>, <c>hmtx</c> y <c>maxp</c> no se tocan. Así el texto se mapea a los mismos glifos con
/// los mismos avances que en la fuente original, que es con la que se midió el layout.
/// </para>
/// Devuelve <c>null</c> cuando no puede (o no debe) recortar: contornos CFF (.otf), fuentes
/// variables, glifos a color o bitmap, licencias que prohíben recortar o datos inesperados.
/// En esos casos se embebe la fuente completa, como hacía Skia por sí solo.
/// </summary>
internal static class TrueTypeSubsetter
{
    private const uint CollectionTag = 0x74746366;     // 'ttcf'
    private const uint TrueTypeVersion = 0x00010000;
    private const uint AppleTrueTypeVersion = 0x74727565; // 'true'
    private const uint HeadMagicNumber = 0x5F0F3CF5;

    private static readonly uint Head = Tag("head");
    private static readonly uint Maxp = Tag("maxp");
    private static readonly uint Loca = Tag("loca");
    private static readonly uint Glyf = Tag("glyf");
    private static readonly uint Post = Tag("post");
    private static readonly uint Os2 = Tag("OS/2");

    // Lo que necesita Skia para dibujar sin shaping y un visor PDF para mostrar la fuente.
    // Se descarta el resto: GSUB/GPOS/kern no intervienen, DSIG deja de ser válida y
    // hdmx/LTSH/VDMX son cachés de métricas opcionales.
    private static readonly HashSet<uint> KeptTables =
    [
        Head, Tag("hhea"), Tag("hmtx"), Maxp, Tag("cmap"), Os2, Post, Tag("name"),
        Tag("cvt "), Tag("fpgm"), Tag("prep"), Tag("gasp"), Glyf, Loca
    ];

    // Variaciones y glifos a color/bitmap: recortar glyf cambiaría cómo se ven.
    private static readonly uint[] UnsupportedTables =
        [Tag("fvar"), Tag("gvar"), Tag("COLR"), Tag("sbix"), Tag("CBDT"), Tag("EBDT"), Tag("SVG ")];

    // Flags de un componente de glifo compuesto (spec OpenType, tabla glyf).
    private const ushort ArgsAreWords = 0x0001;
    private const ushort HasScale = 0x0008;
    private const ushort MoreComponents = 0x0020;
    private const ushort HasXYScale = 0x0040;
    private const ushort HasTwoByTwo = 0x0080;

    /// <param name="font">Archivo de fuente (.ttf o colección .ttc).</param>
    /// <param name="faceIndex">Índice de la cara dentro de una colección; 0 para .ttf.</param>
    /// <param name="glyphs">IDs de glifo a conservar. El .notdef (0) y los componentes de los compuestos se añaden solos.</param>
    public static byte[]? Subset(ReadOnlySpan<byte> font, int faceIndex, IEnumerable<ushort> glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);

        var directory = 0;
        if (font.Length < 12) return null;
        if (U32(font, 0) == CollectionTag)
        {
            var numFonts = U32(font, 8);
            if (faceIndex < 0 || faceIndex >= numFonts || font.Length < 16 + 4 * faceIndex) return null;
            var faceOffset = U32(font, 12 + 4 * faceIndex);
            if (faceOffset > font.Length - 12) return null;
            directory = (int)faceOffset;
        }
        else if (faceIndex != 0)
        {
            return null;
        }

        var version = U32(font, directory);
        if (version != TrueTypeVersion && version != AppleTrueTypeVersion) return null;   // 'OTTO' = CFF

        var tables = ReadTableDirectory(font, directory);
        if (tables is null) return null;
        foreach (var tag in UnsupportedTables)
            if (tables.ContainsKey(tag)) return null;

        if (!tables.TryGetValue(Head, out var head) || head.Length < 54 ||
            !tables.TryGetValue(Maxp, out var maxp) || maxp.Length < 6 ||
            !tables.TryGetValue(Loca, out var loca) ||
            !tables.TryGetValue(Glyf, out var glyf))
            return null;

        // OS/2.fsType: 0x0100 = la licencia no permite recortar; 0x0200 = solo se puede embeber como bitmap.
        if (tables.TryGetValue(Os2, out var os2) && os2.Length >= 10 &&
            (U16(font, os2.Offset + 8) & 0x0300) != 0)
            return null;

        var headData = font.Slice(head.Offset, head.Length);
        if (U32(headData, 12) != HeadMagicNumber || I16(headData, 52) != 0) return null;
        var longLoca = I16(headData, 50) == 1;

        int numGlyphs = U16(font, maxp.Offset + 4);
        var glyfData = font.Slice(glyf.Offset, glyf.Length);
        var offsets = ReadLoca(font.Slice(loca.Offset, loca.Length), numGlyphs, longLoca, glyfData.Length);
        if (offsets is null) return null;

        var keep = CollectGlyphs(glyfData, offsets, glyphs);
        if (keep is null) return null;

        var output = new List<(uint Tag, byte[] Data)>();
        foreach (var (tag, range) in tables)
        {
            if (!KeptTables.Contains(tag) || tag == Glyf || tag == Loca) continue;
            byte[] data;
            if (tag == Head)
            {
                data = headData.ToArray();
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 0);   // checkSumAdjustment: se recalcula al final
                BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(50), 1);   // indexToLocFormat: loca larga
            }
            else if (tag == Post && range.Length >= 32)
            {
                // post 3.0: mismo encabezado sin la lista de nombres de glifo (que puede pesar decenas de KB).
                data = font.Slice(range.Offset, 32).ToArray();
                BinaryPrimitives.WriteUInt32BigEndian(data, 0x00030000);
            }
            else
            {
                data = font.Slice(range.Offset, range.Length).ToArray();
            }
            output.Add((tag, data));
        }

        var (newGlyf, newLoca) = BuildGlyfAndLoca(glyfData, offsets, keep);
        output.Add((Glyf, newGlyf));
        output.Add((Loca, newLoca));
        return WriteFont(version, output);
    }

    private static Dictionary<uint, (int Offset, int Length)>? ReadTableDirectory(ReadOnlySpan<byte> font, int directory)
    {
        int numTables = U16(font, directory + 4);
        if (directory + 12 + 16 * numTables > font.Length) return null;

        var tables = new Dictionary<uint, (int Offset, int Length)>(numTables);
        for (var i = 0; i < numTables; i++)
        {
            var record = directory + 12 + 16 * i;
            var offset = U32(font, record + 8);
            var length = U32(font, record + 12);
            if (offset > (uint)font.Length || length > (uint)font.Length - offset) return null;
            tables[U32(font, record)] = ((int)offset, (int)length);
        }
        return tables;
    }

    private static int[]? ReadLoca(ReadOnlySpan<byte> loca, int numGlyphs, bool longLoca, int glyfLength)
    {
        if (loca.Length < (numGlyphs + 1) * (longLoca ? 4 : 2)) return null;

        var offsets = new int[numGlyphs + 1];
        for (var i = 0; i <= numGlyphs; i++)
        {
            var offset = longLoca ? U32(loca, 4 * i) : U16(loca, 2 * i) * 2u;
            if (offset > (uint)glyfLength || (i > 0 && offset < offsets[i - 1])) return null;
            offsets[i] = (int)offset;
        }
        return offsets;
    }

    /// <summary>Glifos pedidos + .notdef + todos los componentes de los compuestos (p. ej. "Á" = "A" + "´").</summary>
    private static bool[]? CollectGlyphs(ReadOnlySpan<byte> glyf, int[] offsets, IEnumerable<ushort> requested)
    {
        var numGlyphs = offsets.Length - 1;
        var keep = new bool[numGlyphs];
        var pending = new Stack<int>();
        pending.Push(0);
        foreach (var glyph in requested)
            if (glyph < numGlyphs) pending.Push(glyph);

        while (pending.Count > 0)
        {
            var glyph = pending.Pop();
            if (keep[glyph]) continue;
            keep[glyph] = true;

            var start = offsets[glyph];
            var end = offsets[glyph + 1];
            if (end - start < 10 || I16(glyf, start) >= 0) continue;   // vacío o simple

            var p = start + 10;
            ushort flags;
            do
            {
                if (p + 4 > end) return null;
                flags = U16(glyf, p);
                var component = U16(glyf, p + 2);
                if (component >= numGlyphs) return null;
                pending.Push(component);

                p += 4 + ((flags & ArgsAreWords) != 0 ? 4 : 2);
                if ((flags & HasScale) != 0) p += 2;
                else if ((flags & HasXYScale) != 0) p += 4;
                else if ((flags & HasTwoByTwo) != 0) p += 8;
            }
            while ((flags & MoreComponents) != 0);
        }
        return keep;
    }

    private static (byte[] Glyf, byte[] Loca) BuildGlyfAndLoca(ReadOnlySpan<byte> glyf, int[] offsets, bool[] keep)
    {
        var numGlyphs = keep.Length;
        var size = 0;
        for (var i = 0; i < numGlyphs; i++)
            if (keep[i]) size += Align4(offsets[i + 1] - offsets[i]);

        var newGlyf = new byte[size];
        var newLoca = new byte[4 * (numGlyphs + 1)];
        var position = 0;
        for (var i = 0; i < numGlyphs; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(4 * i), (uint)position);
            if (!keep[i]) continue;
            var length = offsets[i + 1] - offsets[i];
            glyf.Slice(offsets[i], length).CopyTo(newGlyf.AsSpan(position));
            position += Align4(length);
        }
        BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(4 * numGlyphs), (uint)position);
        return (newGlyf, newLoca);
    }

    private static byte[] WriteFont(uint version, List<(uint Tag, byte[] Data)> tables)
    {
        tables.Sort((a, b) => a.Tag.CompareTo(b.Tag));
        var count = tables.Count;
        var headerSize = 12 + 16 * count;
        var size = headerSize;
        foreach (var table in tables) size += Align4(table.Data.Length);

        var font = new byte[size];
        var span = font.AsSpan();
        var power = 1;
        var log2 = 0;
        while (power * 2 <= count) { power *= 2; log2++; }
        BinaryPrimitives.WriteUInt32BigEndian(span, version);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], (ushort)(power * 16));
        BinaryPrimitives.WriteUInt16BigEndian(span[8..], (ushort)log2);
        BinaryPrimitives.WriteUInt16BigEndian(span[10..], (ushort)(count * 16 - power * 16));

        var offset = headerSize;
        var headOffset = -1;
        for (var i = 0; i < count; i++)
        {
            var (tag, data) = tables[i];
            var record = span.Slice(12 + 16 * i, 16);
            BinaryPrimitives.WriteUInt32BigEndian(record, tag);
            BinaryPrimitives.WriteUInt32BigEndian(record[4..], Checksum(data));
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)data.Length);
            data.CopyTo(span[offset..]);
            if (tag == Head) headOffset = offset;
            offset += Align4(data.Length);
        }

        if (headOffset >= 0)
            BinaryPrimitives.WriteUInt32BigEndian(span[(headOffset + 8)..], unchecked(0xB1B0AFBA - Checksum(font)));
        return font;
    }

    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        var i = 0;
        for (; i + 4 <= data.Length; i += 4)
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data[i..]));
        if (i < data.Length)
        {
            Span<byte> tail = stackalloc byte[4];
            tail.Clear();
            data[i..].CopyTo(tail);
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(tail));
        }
        return sum;
    }

    private static int Align4(int length) => (length + 3) & ~3;

    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
    private static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
    private static short I16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt16BigEndian(data[offset..]);

    private static uint Tag(string tag) => (uint)(tag[0] << 24 | tag[1] << 16 | tag[2] << 8 | tag[3]);
}
