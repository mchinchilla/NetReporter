using ClosedXML.Excel;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Xlsx;

/// <summary>
/// Mapea un <see cref="ResolvedStyle"/> de NetReporter a las APIs de estilo de ClosedXML.
/// </summary>
internal static class XlsxStyleApplier
{
    public static void Apply(IXLStyle target, ResolvedStyle style)
    {
        target.Font.FontName = style.FontFamily;
        target.Font.FontSize = style.FontSize;
        target.Font.Bold = style.Weight == FontWeight.Bold;
        target.Font.Italic = style.Italic == FontStyleKind.Italic;
        target.Font.FontColor = ToXLColor(style.Foreground);

        if (style.Background.A > 0 && style.Background != Color.Transparent)
        {
            target.Fill.BackgroundColor = ToXLColor(style.Background);
            target.Fill.PatternType = XLFillPatternValues.Solid;
        }

        target.Alignment.Horizontal = style.TextAlign switch
        {
            TextAlignment.Left    => XLAlignmentHorizontalValues.Left,
            TextAlignment.Center  => XLAlignmentHorizontalValues.Center,
            TextAlignment.Right   => XLAlignmentHorizontalValues.Right,
            TextAlignment.Justify => XLAlignmentHorizontalValues.Justify,
            _ => XLAlignmentHorizontalValues.Left
        };
        target.Alignment.Vertical = style.VerticalAlign switch
        {
            VerticalAlignment.Top    => XLAlignmentVerticalValues.Top,
            VerticalAlignment.Center => XLAlignmentVerticalValues.Center,
            VerticalAlignment.Bottom => XLAlignmentVerticalValues.Bottom,
            _ => XLAlignmentVerticalValues.Top
        };

        if (style.NumberFormat is { } nf)
            target.NumberFormat.Format = nf;
    }

    public static XLColor ToXLColor(Color c) =>
        XLColor.FromArgb(c.A, c.R, c.G, c.B);
}
