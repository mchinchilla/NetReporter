using System.Diagnostics;
using ClosedXML.Excel;
using NetReporter.Core.Bands;
using NetReporter.Core.DataBinding;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;
using NetReporter.Xlsx;
using Xunit.Abstractions;

namespace NetReporter.Xlsx.Tests;

/// <summary>
/// A large table (the shape of a 90-day fiscal sales book: tens of thousands of rows, a dozen columns, alternate row
/// style, dates and number formats) renders with every cell's value, style and number format right — and the time it
/// takes is printed, because per-cell style assignment in ClosedXML was what made it slow.
/// </summary>
public sealed class XlsxLargeTableTests(ITestOutputHelper output)
{
    private sealed record Line(string Number, DateTime Date, string Customer, string Rtn, decimal Exempt, decimal Exonerated,
        decimal Taxable15, decimal Tax15, decimal Taxable18, decimal Tax18, decimal Total, string Status);

    private static ReportDefinition Report(IReadOnlyList<Line> rows) => new()
    {
        Name = "Libro",
        Page = PageSetup.Letter,
        Styles = new StyleSheetBuilder()
            .Add("Default", s => s.FontFamily("Helvetica").FontSize(9))
            .Add("TableHeader", s => s.BasedOn("Default").Bold())
            .Add("TableRow", s => s.BasedOn("Default"))
            .Add("TableRowAlt", s => s.BasedOn("Default").Background(Color.FromHex("#F3F4F6")))
            .Build(),
        Bands =
        [
            new DetailBand
            {
                Height = 0,
                Elements =
                [
                    new TableElement<Line>
                    {
                        Bounds = new Rect(0, 0, 700, 0),
                        Rows = rows,
                        HeaderStyle = new StyleRef("TableHeader"),
                        RowStyle = new StyleRef("TableRow"),
                        AlternateRowStyle = new StyleRef("TableRowAlt"),
                        Columns =
                        [
                            new TableColumn<Line>("Factura", Bind.From<Line, object?>(r => r.Number), 70),
                            new TableColumn<Line>("Fecha", Bind.From<Line, object?>(r => r.Date), 50, "yyyy-MM-dd"),
                            new TableColumn<Line>("Cliente", Bind.From<Line, object?>(r => r.Customer), 100),
                            new TableColumn<Line>("RTN", Bind.From<Line, object?>(r => r.Rtn), 60),
                            new TableColumn<Line>("Exento", Bind.From<Line, object?>(r => r.Exempt), 50, "N2", TextAlignment.Right),
                            new TableColumn<Line>("Exonerado", Bind.From<Line, object?>(r => r.Exonerated), 50, "N2", TextAlignment.Right),
                            new TableColumn<Line>("Gravado 15", Bind.From<Line, object?>(r => r.Taxable15), 50, "N2", TextAlignment.Right),
                            new TableColumn<Line>("ISV 15", Bind.From<Line, object?>(r => r.Tax15), 50, "N2", TextAlignment.Right),
                            new TableColumn<Line>("Gravado 18", Bind.From<Line, object?>(r => r.Taxable18), 50, "N2", TextAlignment.Right),
                            new TableColumn<Line>("ISV 18", Bind.From<Line, object?>(r => r.Tax18), 50, "N2", TextAlignment.Right),
                            new TableColumn<Line>("Total", Bind.From<Line, object?>(r => r.Total), 50, "N2", TextAlignment.Right),
                            new TableColumn<Line>("Estado", Bind.From<Line, object?>(r => r.Status), 40, null, TextAlignment.Center),
                        ]
                    }
                ]
            }
        ]
    };

    [Fact]
    public void Fifty_thousand_rows_keep_values_styles_and_formats()
    {
        const int n = 50_000;
        var rows = Enumerable.Range(0, n).Select(i => new Line($"000-001-01-{i:D8}", new DateTime(2026, 8, 1).AddDays(i % 90),
            $"Cliente {i % 997}", $"0801{i:D10}", i % 7, 0m, 100m + i % 50, (100m + i % 50) * 0.15m, 0m, 0m,
            (100m + i % 50) * 1.15m + i % 7, i % 13 == 0 ? "Anulada" : "Vigente")).ToList();

        var sw = Stopwatch.StartNew();
        var bytes = new XlsxRenderer().Render(Report(rows), new XlsxRenderOptions { EmitNativeTables = true, FreezeTableHeaders = true });
        output.WriteLine($"render {n} rows x 12 cols: {sw.ElapsedMilliseconds} ms, {bytes.Length / 1024} KB");

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);
        foreach (var i in new[] { 0, 1, 2, 12345, n - 2, n - 1 })
        {
            var r = i + 2; // header in row 1
            var line = rows[i];
            Assert.Equal(line.Number, ws.Cell(r, 1).GetString());
            Assert.Equal(line.Date, ws.Cell(r, 2).GetDateTime());
            Assert.Equal("yyyy-mm-dd", ws.Cell(r, 2).Style.NumberFormat.Format);
            Assert.Equal((double)line.Total, ws.Cell(r, 11).GetDouble(), 6);
            Assert.Equal("#,##0.00", ws.Cell(r, 11).Style.NumberFormat.Format);
            Assert.Equal(XLAlignmentHorizontalValues.Right, ws.Cell(r, 11).Style.Alignment.Horizontal);
            Assert.Equal(XLAlignmentHorizontalValues.Center, ws.Cell(r, 12).Style.Alignment.Horizontal);
            Assert.Equal(XLAlignmentHorizontalValues.Left, ws.Cell(r, 3).Style.Alignment.Horizontal);
            Assert.Equal("Helvetica", ws.Cell(r, 3).Style.Font.FontName);
            var alt = i % 2 == 1;
            Assert.Equal(alt ? XLFillPatternValues.Solid : XLFillPatternValues.None, ws.Cell(r, 4).Style.Fill.PatternType);
            if (alt) Assert.Equal(XLColor.FromArgb(255, 0xF3, 0xF4, 0xF6), ws.Cell(r, 4).Style.Fill.BackgroundColor);
        }
        Assert.True(ws.Cell(1, 1).Style.Font.Bold);
        Assert.False(ws.Cell(2, 1).Style.Font.Bold);
    }
}
