namespace NetReporter.EscPos;

/// <summary>Los pocos comandos ESC/POS que usa el renderer raster.</summary>
internal static class EscPosCommands
{
    /// <summary><c>ESC @</c>: estado por defecto.</summary>
    public static ReadOnlySpan<byte> Initialize => [0x1B, 0x40];

    /// <summary><c>ESC d n</c>: alimenta n líneas.</summary>
    public static void WriteFeedLines(Stream output, int lines)
    {
        if (lines <= 0) return;
        ReadOnlySpan<byte> cmd = [0x1B, 0x64, (byte)lines];
        output.Write(cmd);
    }

    /// <summary><c>GS V m</c>: corte total (0) o parcial (1).</summary>
    public static void WriteCut(Stream output, EscPosCut cut)
    {
        if (cut == EscPosCut.None) return;
        ReadOnlySpan<byte> cmd = [0x1D, 0x56, (byte)(cut == EscPosCut.Full ? 0 : 1)];
        output.Write(cmd);
    }

    /// <summary><c>ESC p m t1 t2</c>: pulso de 50 ms (t1 = 25 × 2 ms) al conector m de la gaveta.</summary>
    public static void WriteDrawerKick(Stream output, int pin)
    {
        ReadOnlySpan<byte> cmd = [0x1B, 0x70, (byte)pin, 25, 250];
        output.Write(cmd);
    }

    /// <summary><c>GS v 0 m xL xH yL yH d…</c>: bloque raster en modo normal; bit 1 = punto negro, MSB primero.</summary>
    public static void WriteRasterBlock(Stream output, int bytesPerRow, int rows, ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> header =
        [
            0x1D, 0x76, 0x30, 0x00,
            (byte)(bytesPerRow & 0xFF), (byte)((bytesPerRow >> 8) & 0xFF),
            (byte)(rows & 0xFF), (byte)((rows >> 8) & 0xFF),
        ];
        output.Write(header);
        output.Write(data);
    }
}
