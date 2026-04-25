using ClosedXML.Excel;
using NetReporter.Core.Bands;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Styles;

namespace NetReporter.Xlsx;

/// <summary>
/// Renderer XLSX semántico. Consume <see cref="ReportDefinition"/> directamente
/// (NO el RenderList): los <see cref="TextElement"/>s se vuelven celdas y los
/// <see cref="TableElement{TRow}"/> se vuelven rangos de Excel reales — editables,
/// filtrables, sortables. Elementos puramente decorativos (Line/Rectangle/Barcode)
/// se omiten ya que XLSX no es un formato de layout absoluto.
/// </summary>
public sealed class XlsxRenderer
{
    public byte[] Render(ReportDefinition report, XlsxRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        options ??= new XlsxRenderOptions();

        using var workbook = new XLWorkbook();
        var sheetName = SanitizeSheetName(options.WorksheetName ?? report.Name);
        var ws = workbook.Worksheets.Add(sheetName);

        var ctx = new XlsxEvaluationContext { Culture = report.Culture };
        var cursor = new RowCursor();

        foreach (var band in report.Bands)
        {
            if (band.Visible is { } v && !v.Evaluate(ctx)) continue;
            EmitBand(band, ws, report, ctx, cursor, options);
        }

        ApplyPageSetup(ws, report);

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void EmitBand(
        Band band,
        IXLWorksheet ws,
        ReportDefinition report,
        XlsxEvaluationContext ctx,
        RowCursor cursor,
        XlsxRenderOptions options)
    {
        foreach (var element in band.Elements)
        {
            if (element.Visible is { } v && !v.Evaluate(ctx)) continue;
            EmitElement(element, ws, report, ctx, cursor, options);
        }
    }

    private static void EmitElement(
        ReportElement element,
        IXLWorksheet ws,
        ReportDefinition report,
        XlsxEvaluationContext ctx,
        RowCursor cursor,
        XlsxRenderOptions options)
    {
        if (element is TextElement text)
        {
            EmitText(text, ws, report, ctx, cursor);
            return;
        }

        if (XlsxTableEmitter.IsTable(element))
        {
            var startRow = cursor.AdvanceRow(); // header row
            var lastRow = XlsxTableEmitter.EmitAt(element, startRow, ws, report, ctx, options);
            cursor.JumpTo(lastRow);
            cursor.BlankRow();
            return;
        }

        if (element is ImageElement image)
        {
            EmitImage(image, ws, cursor);
            return;
        }

        // Line / Rectangle / Barcode: omitidos — son ornamentos visuales sin equivalente semántico
        // en una hoja de cálculo. (Un código de barras como rectángulos individuales no tendría
        // utilidad en Excel.)
    }

    private static void EmitImage(ImageElement image, IXLWorksheet ws, RowCursor cursor)
    {
        using var stream = new MemoryStream(image.Data);
        var picture = ws.AddPicture(stream);

        var anchorRow = cursor.AdvanceRow();
        picture.MoveTo(ws.Cell(anchorRow, 1));
        // Move (no MoveAndSize) permite cambiar el tamaño después del MoveTo.
        picture.Placement = ClosedXML.Excel.Drawings.XLPicturePlacement.Move;

        // Bounds están en puntos. ClosedXML usa pixels @ 96dpi: 1pt = 96/72 px.
        const double PtToPx = 96.0 / 72.0;
        var widthPx  = (int)Math.Round(image.Bounds.Width  * PtToPx);
        var heightPx = (int)Math.Round(image.Bounds.Height * PtToPx);
        if (widthPx > 0 && heightPx > 0)
            picture.WithSize(widthPx, heightPx);

        // Reservar filas para que el cursor no escriba encima de la imagen.
        // Default row height en Excel ≈ 15 pt → ceil(image.height / 15) filas.
        var rowsNeeded = Math.Max(1, (int)Math.Ceiling(image.Bounds.Height / 15.0));
        cursor.AdvanceRows(rowsNeeded - 1); // ya tomamos 1 con AdvanceRow.
    }

    private static void EmitText(
        TextElement text,
        IXLWorksheet ws,
        ReportDefinition report,
        XlsxEvaluationContext ctx,
        RowCursor cursor)
    {
        var content = text.Content.Evaluate(ctx);
        var style = report.Styles.Resolve(text.Style);

        var row = cursor.AdvanceRow();
        var cell = ws.Cell(row, 1);
        cell.Value = content;
        XlsxStyleApplier.Apply(cell.Style, style);
    }

    private static void ApplyPageSetup(IXLWorksheet ws, ReportDefinition report)
    {
        var page = report.Page;

        ws.PageSetup.PaperSize = MapPaperSize(page);
        ws.PageSetup.PageOrientation = page.Size.Width > page.Size.Height
            ? XLPageOrientation.Landscape
            : XLPageOrientation.Portrait;

        // Convertir puntos a pulgadas (1 in = 72 pt) — ClosedXML usa pulgadas.
        ws.PageSetup.Margins.Top    = page.Margins.Top    / 72.0;
        ws.PageSetup.Margins.Bottom = page.Margins.Bottom / 72.0;
        ws.PageSetup.Margins.Left   = page.Margins.Left   / 72.0;
        ws.PageSetup.Margins.Right  = page.Margins.Right  / 72.0;
    }

    private static XLPaperSize MapPaperSize(NetReporter.Core.Primitives.PageSetup page)
    {
        // Tolerancia de 1pt para floating-point al matchear.
        var w = page.Size.Width;
        var h = page.Size.Height;
        bool Matches(double a, double b) => (Math.Abs(w - a) < 1 && Math.Abs(h - b) < 1)
                                         || (Math.Abs(w - b) < 1 && Math.Abs(h - a) < 1);

        if (Matches(612, 792))    return XLPaperSize.LetterPaper;
        if (Matches(612, 1008))   return XLPaperSize.LegalPaper;
        if (Matches(792, 1224))   return XLPaperSize.TabloidPaper;
        if (Matches(522, 756))    return XLPaperSize.ExecutivePaper;
        if (Matches(396, 612))    return XLPaperSize.StatementPaper;
        if (Matches(842, 1191))   return XLPaperSize.A3Paper;
        if (Matches(595, 842))    return XLPaperSize.A4Paper;
        if (Matches(420, 595))    return XLPaperSize.A5Paper;
        if (Matches(708, 1000))   return XLPaperSize.B4Paper;
        if (Matches(499, 708))    return XLPaperSize.B5Paper;
        return XLPaperSize.LetterPaper;
    }

    /// <summary>
    /// Sanitiza el nombre de la worksheet — Excel exige ≤31 chars y prohibe <c>: / \ ? * [ ]</c>.
    /// </summary>
    private static string SanitizeSheetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Report";
        var clean = new string(name.Select(c => c switch
        {
            ':' or '/' or '\\' or '?' or '*' or '[' or ']' => '_',
            _ => c
        }).ToArray()).Trim();
        if (clean.Length > 31) clean = clean[..31];
        return string.IsNullOrEmpty(clean) ? "Report" : clean;
    }

    /// <summary>Tracker de la fila actual de escritura. Avanza linealmente conforme se emiten elementos.</summary>
    private sealed class RowCursor
    {
        private int _row;

        /// <summary>Reserva una fila y devuelve su índice 1-based.</summary>
        public int AdvanceRow() => ++_row;

        /// <summary>Reserva <paramref name="count"/> filas y devuelve la primera 1-based.</summary>
        public int AdvanceRows(int count)
        {
            var start = _row + 1;
            _row += count;
            return start;
        }

        /// <summary>Inserta una fila en blanco como separador.</summary>
        public void BlankRow() => _row++;

        /// <summary>Sincroniza el cursor a una fila específica (1-based).</summary>
        public void JumpTo(int row) { if (row > _row) _row = row; }
    }
}
