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
        if (value is null)
        {
            cell.Value = string.Empty;
            return;
        }

        switch (value)
        {
            case bool b:
                cell.Value = b;
                break;

            case DateTime dt:
                cell.Value = dt;
                cell.Style.NumberFormat.Format = MapDateFormat(netFormat) ?? "yyyy-mm-dd";
                break;

            case DateTimeOffset dto:
                cell.Value = dto.LocalDateTime;
                cell.Style.NumberFormat.Format = MapDateFormat(netFormat) ?? "yyyy-mm-dd";
                break;

            case decimal m:
                cell.Value = (double)m;
                ApplyNumberFormat(cell, netFormat);
                break;

            case double d:
                cell.Value = d;
                ApplyNumberFormat(cell, netFormat);
                break;

            case float f:
                cell.Value = f;
                ApplyNumberFormat(cell, netFormat);
                break;

            case int i:
                cell.Value = i;
                ApplyNumberFormat(cell, netFormat);
                break;

            case long l:
                cell.Value = l;
                ApplyNumberFormat(cell, netFormat);
                break;

            case short sh:
                cell.Value = sh;
                ApplyNumberFormat(cell, netFormat);
                break;

            case string s:
                // Si el string parsea como número y hay un format numérico, escribir como número.
                if (netFormat is not null && IsNumericFormat(netFormat) &&
                    double.TryParse(s, NumberStyles.Any, culture, out var parsed))
                {
                    cell.Value = parsed;
                    ApplyNumberFormat(cell, netFormat);
                }
                else
                {
                    cell.Value = s;
                }
                break;

            default:
                cell.Value = value.ToString() ?? string.Empty;
                break;
        }
    }

    private static void ApplyNumberFormat(IXLCell cell, string? netFormat)
    {
        var mapped = MapNumberFormat(netFormat);
        if (mapped is not null)
            cell.Style.NumberFormat.Format = mapped;
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
