namespace NetReporter.Core.Primitives;

public readonly record struct Color(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Color Black       = new(0, 0, 0);
    public static readonly Color White       = new(255, 255, 255);
    public static readonly Color Transparent = new(0, 0, 0, 0);

    public static Color FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var span = hex.AsSpan().TrimStart('#');

        return span.Length switch
        {
            3 => new Color(Hex(span[0], span[0]), Hex(span[1], span[1]), Hex(span[2], span[2])),
            6 => new Color(Hex(span[0], span[1]), Hex(span[2], span[3]), Hex(span[4], span[5])),
            8 => new Color(Hex(span[0], span[1]), Hex(span[2], span[3]),
                           Hex(span[4], span[5]), Hex(span[6], span[7])),
            _ => throw new ArgumentException($"Hex color inválido: '{hex}'.", nameof(hex))
        };

        static byte Hex(char hi, char lo) => (byte)((HexDigit(hi) << 4) | HexDigit(lo));

        static int HexDigit(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => throw new ArgumentException($"Dígito hex inválido: '{c}'.")
        };
    }

    public string ToHexString() => $"#{R:X2}{G:X2}{B:X2}";
}
