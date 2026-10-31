namespace NetReporter.EscPos.Tests;

/// <summary>Lee el flujo ESC/POS del renderer: reconstruye el bitmap de los bloques <c>GS v 0</c> y anota los demás comandos.</summary>
internal sealed class EscPosDecoder
{
    public bool Initialized { get; private set; }
    public List<int> FeedLines { get; } = new();
    public List<byte> Cuts { get; } = new();
    public List<byte> DrawerPins { get; } = new();
    public List<int> BlockRows { get; } = new();
    public int BytesPerRow { get; private set; }
    public List<byte[]> Rows { get; } = new();
    public int Width => BytesPerRow * 8;
    public int Height => Rows.Count;

    public bool IsBlack(int x, int y) => (Rows[y][x >> 3] & (0x80 >> (x & 7))) != 0;

    public int CountBlack(int x0, int y0, int x1, int y1)
    {
        var n = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
                if (IsBlack(x, y)) n++;
        return n;
    }

    public static EscPosDecoder Decode(ReadOnlySpan<byte> data)
    {
        var d = new EscPosDecoder();
        var i = 0;
        while (i < data.Length)
        {
            if (data[i] == 0x1B && i + 1 < data.Length && data[i + 1] == 0x40) { d.Initialized = true; i += 2; continue; }
            if (data[i] == 0x1B && i + 2 < data.Length && data[i + 1] == 0x64) { d.FeedLines.Add(data[i + 2]); i += 3; continue; }
            if (data[i] == 0x1B && i + 4 < data.Length && data[i + 1] == 0x70) { d.DrawerPins.Add(data[i + 2]); i += 5; continue; }
            if (data[i] == 0x1D && i + 2 < data.Length && data[i + 1] == 0x56) { d.Cuts.Add(data[i + 2]); i += 3; continue; }
            if (data[i] == 0x1D && i + 7 < data.Length && data[i + 1] == 0x76 && data[i + 2] == 0x30)
            {
                var bytesPerRow = data[i + 4] | (data[i + 5] << 8);
                var rows = data[i + 6] | (data[i + 7] << 8);
                if (d.BytesPerRow == 0) d.BytesPerRow = bytesPerRow;
                else if (d.BytesPerRow != bytesPerRow) throw new InvalidDataException("Ancho de fila inconsistente entre bloques.");
                d.BlockRows.Add(rows);
                i += 8;
                for (var r = 0; r < rows; r++)
                {
                    d.Rows.Add(data.Slice(i, bytesPerRow).ToArray());
                    i += bytesPerRow;
                }
                continue;
            }
            throw new InvalidDataException($"Byte inesperado 0x{data[i]:X2} en la posición {i}.");
        }
        return d;
    }
}
