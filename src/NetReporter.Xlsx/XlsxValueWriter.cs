using System.Globalization;
using ClosedXML.Excel;

namespace NetReporter.Xlsx;

/// <summary>
/// Convierte valores .NET a <see cref="XLCellValue"/> preservando el tipo (número como número,
/// fecha como fecha) y mapea formatos .NET a máscaras Excel cuando es posible. Esto es lo que
/// permite que el Excel resultante sea sortable/filtrable/sumable — no celdas string.
/// </summary>
internal static class XlsxValueWriter
{
    public static void Write(IXLCell cell, object? value, string? netFormat, CultureInfo culture)
    {
        var format = WriteValue(cell, value, netFormat, culture);
        if (format is not null)
            cell.Style.NumberFormat.Format = format;
    }

    /// <summary>
    /// Writes the value with its type preserved and returns the Excel number format the cell needs (null = General)
    /// WITHOUT touching the cell's style, so a caller writing many cells can apply one cached style per distinct
    /// (style, format) instead of re-keying the style of every cell (see <see cref="XlsxTableEmitter"/>).
    /// </summary>
    public static string? WriteValue(IXLCell cell, object? value, string? netFormat, CultureInfo culture)
    {
        switch (value)
        {
            case null:
                cell.Value = string.Empty;
                return null;
            case bool b:
                cell.Value = b;
                return null;
            case DateTime dt:
                cell.Value = dt;
                return MapDateFormat(netFormat) ?? "yyyy-mm-dd";
            case DateTimeOffset dto:
                cell.Value = dto.LocalDateTime;
                return MapDateFormat(netFormat) ?? "yyyy-mm-dd";
            case decimal m:
                cell.Value = (double)m;
                return MapNumberFormat(netFormat);
            case double d:
                cell.Value = d;
                return MapNumberFormat(netFormat);
            case float f:
                cell.Value = f;
                return MapNumberFormat(netFormat);
            case int i:
                cell.Value = i;
                return MapNumberFormat(netFormat);
            case long l:
                cell.Value = l;
                return MapNumberFormat(netFormat);
            case short sh:
                cell.Value = sh;
                return MapNumberFormat(netFormat);
            case string s:
                // Si el string parsea como número y hay un format numérico, escribir como número.
                if (netFormat is not null && IsNumericFormat(netFormat) &&
                    double.TryParse(s, NumberStyles.Any, culture, out var parsed))
                {
                    cell.Value = parsed;
                    return MapNumberFormat(netFormat);
                }
                cell.Value = s;
                return null;
            default:
                cell.Value = value.ToString() ?? string.Empty;
                return null;
        }
    }

    /// <summary>
    /// Mapeo best-effort de formatos .NET (N2, F0, C, P, D) a máscaras Excel.
    /// No es exhaustivo — devuelve null si no reconoce el formato y Excel usa General.
    /// </summary>
    private static string? MapNumberFormat(string? net)
    {
        if (string.IsNullOrEmpty(net)) return null;

        var c = char.ToUpperInvariant(net[0]);
        int digits = 0;
        var hasDigits = net.Length > 1 && int.TryParse(net.AsSpan(1), out digits);
        var places = hasDigits ? digits : 2;

        return c switch
        {
            'N' => "#,##0" + Decimals(places),     // number con miles
            'F' => "0"     + Decimals(places),     // fixed point sin miles
            'C' => "$#,##0" + Decimals(places),    // currency genérico
            'P' => "0"     + Decimals(places) + "%", // percent
            'D' => "0",                             // decimal integer
            'E' => "0" + Decimals(places) + "E+00", // scientific
            'G' => null,
            _   => null
        };

        static string Decimals(int n) => n <= 0 ? string.Empty : "." + new string('0', n);
    }

    private static string? MapDateFormat(string? net)
    {
        if (string.IsNullOrEmpty(net)) return null;

        // Algunos comunes — el resto cae al default ISO.
        return net switch
        {
            "d"           => "m/d/yyyy",
            "D"           => "dddd, mmmm d, yyyy",
            "yyyy-MM-dd"  => "yyyy-mm-dd",
            "dd/MM/yyyy"  => "dd/mm/yyyy",
            "MM/dd/yyyy"  => "mm/dd/yyyy",
            "yyyy"        => "yyyy",
            _             => null
        };
    }

    private static bool IsNumericFormat(string fmt) =>
        fmt.Length > 0 && "NFCPDE".IndexOf(char.ToUpperInvariant(fmt[0])) >= 0;
}
