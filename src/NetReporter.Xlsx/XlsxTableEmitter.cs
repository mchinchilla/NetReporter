using ClosedXML.Excel;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;

namespace NetReporter.Xlsx;

/// <summary>
/// Emite un <see cref="TableElement{TRow}"/> como un rango semántico de Excel:
/// header row + data rows con tipos preservados. Opcionalmente lo envuelve como
/// Excel Table nativa para tener filtros/sorting/banded rows. Las tablas agrupadas usan
/// el "outline" de Excel (filas agrupables/colapsables por nivel) en vez de una Table nativa.
///
/// El tipo de fila se recupera con <see cref="ITableVisitor{TResult}"/> — sin reflection.
/// </summary>
internal static class XlsxTableEmitter
{
    public static bool IsTable(ReportElement element) => element is ITableElement;

    /// <summary>
    /// Emite la tabla empezando en <paramref name="startRow"/> (1-based) y devuelve la última fila usada.
    /// </summary>
    public static int EmitAt(
        ReportElement element,
        int startRow,
        IXLWorksheet ws,
        ReportDefinition report,
        XlsxEvaluationContext ctx,
        XlsxRenderOptions options) =>
        ((ITableElement)element).Accept(new Emitter(startRow, ws, report, ctx, options));

    private sealed class Emitter(
        int startRow, IXLWorksheet ws, ReportDefinition report, XlsxEvaluationContext ctx, XlsxRenderOptions options)
        : ITableVisitor<int>
    {
        public int Visit<TRow>(TableElement<TRow> table) => EmitAtGeneric(table, startRow, ws, report, ctx, options);
    }

    private static int EmitAtGeneric<TRow>(
        TableElement<TRow> table,
        int startRow,
        IXLWorksheet ws,
        ReportDefinition report,
        XlsxEvaluationContext ctx,
        XlsxRenderOptions options)
    {
        var headerStyle = report.Styles.Resolve(table.HeaderStyle);
        var rowStyle = report.Styles.Resolve(table.RowStyle);
        var altStyle = table.AlternateRowStyle is { } a
            ? report.Styles.Resolve(a) : rowStyle;

        // === Header ===
        for (int c = 0; c < table.Columns.Count; c++)
        {
            var cell = ws.Cell(startRow, c + 1);
            cell.Value = table.Columns[c].Header;
            XlsxStyleApplier.Apply(cell.Style, headerStyle);
        }

        var dataStart = startRow + 1;
        var currentRow = dataStart;
        var levels = table.EffectiveGroups;

        if (levels.Count == 0)
        {
            for (int i = 0; i < table.Rows.Count; i++)
            {
                ctx.RowIndex = i;
                ctx.CurrentRow = table.Rows[i];
                var style = (i % 2 == 1) ? altStyle : rowStyle;
                EmitDataRow(table, table.Rows[i], currentRow, style, ws, report);
                currentRow++;
            }
        }
        else
        {
            currentRow = EmitGrouped(table, levels, currentRow, ws, report, ctx, rowStyle, altStyle);
        }

        var dataEnd = currentRow - 1;

        // Resumen (total general) debajo de los datos: fuera de la Excel Table para no mezclarlo con filtros.
        if (table.Summary is not null && table.Rows.Count > 0)
        {
            ctx.GroupKey = null;
            ctx.GroupRowCount = table.Rows.Count;
            EmitAggregateRow(table, table.Summary, table.Rows, currentRow, ws, report, ctx);
            currentRow++;
            ctx.GroupRowCount = 0;
        }

        // Crear Excel Table nativa si hay filas, la opción está activa y la tabla no está agrupada.
        if (options.EmitNativeTables && dataEnd >= dataStart && table.Rows.Count > 0 && levels.Count == 0)
        {
            var range = ws.Range(startRow, 1, dataEnd, table.Columns.Count);
            // Nombres de tabla deben ser únicos por sheet — usamos un prefijo + startRow.
            var tableName = $"T_{startRow}";
            range.CreateTable(tableName);
        }

        if (options.FreezeTableHeaders && currentRow > dataStart)
        {
            ws.SheetView.FreezeRows(startRow);
        }

        return currentRow - 1; // última fila usada (1-based)
    }

    private static int EmitGrouped<TRow>(
        TableElement<TRow> table,
        IReadOnlyList<TableGroupLevel<TRow>> levels,
        int startDataRow,
        IXLWorksheet ws,
        ReportDefinition report,
        XlsxEvaluationContext ctx,
        NetReporter.Core.Styles.ResolvedStyle rowStyle,
        NetReporter.Core.Styles.ResolvedStyle altStyle)
    {
        var currentRow = startDataRow;
        var globalRowIndex = 0;

        void SetGroupContext(object? key, int count, int depth)
        {
            ctx.GroupKey = key;
            ctx.GroupRowCount = count;
            while (ctx.GroupKeyStack.Count > depth) ctx.GroupKeyStack.RemoveAt(ctx.GroupKeyStack.Count - 1);
            ctx.GroupKeyStack.Add(key);
        }

        void EmitLevel(IReadOnlyList<TRow> rows, int level)
        {
            if (level == levels.Count)
            {
                foreach (var row in rows)
                {
                    ctx.CurrentRow = row;
                    ctx.RowIndex = globalRowIndex;
                    var style = (globalRowIndex % 2 == 1) ? altStyle : rowStyle;
                    EmitDataRow(table, row, currentRow, style, ws, report);
                    currentRow++;
                    globalRowIndex++;
                }
                return;
            }

            var group = levels[level];
            var headerStyle = group.Header is not null ? report.Styles.Resolve(group.Header.Style) : rowStyle;

            foreach (var (key, groupRows) in TableGrouping.Partition(rows, group.By))
            {
                SetGroupContext(key, groupRows.Count, level);

                // Group header: se mergea sobre todas las columnas de la tabla.
                if (group.Header is not null)
                {
                    var headerCell = ws.Cell(currentRow, 1);
                    headerCell.Value = group.Header.Content.Evaluate(ctx);
                    XlsxStyleApplier.Apply(headerCell.Style, headerStyle);
                    if (table.Columns.Count > 1)
                        ws.Range(currentRow, 1, currentRow, table.Columns.Count).Merge();
                    currentRow++;
                }

                // Las filas de detalle del grupo se agrupan en el outline de Excel (colapsables).
                var detailStart = currentRow;
                EmitLevel(groupRows, level + 1);
                if (currentRow > detailStart)
                    ws.Rows(detailStart, currentRow - 1).Group();

                SetGroupContext(key, groupRows.Count, level);
                if (group.Footer is not null)
                {
                    EmitAggregateRow(table, group.Footer, groupRows, currentRow, ws, report, ctx);
                    currentRow++;
                }
            }
        }

        EmitLevel(table.Rows, 0);

        ctx.GroupKey = null;
        ctx.GroupRowCount = 0;
        ctx.GroupKeyStack.Clear();

        return currentRow;
    }

    /// <summary>Pie de grupo o resumen: una celda por columna, paralela a la tabla.</summary>
    private static void EmitAggregateRow<TRow>(
        TableElement<TRow> table,
        GroupFooter footer,
        IReadOnlyList<TRow> rows,
        int rowNumber,
        IXLWorksheet ws,
        ReportDefinition report,
        XlsxEvaluationContext ctx)
    {
        var footerStyle = report.Styles.Resolve(footer.Style);
        for (int c = 0; c < table.Columns.Count; c++)
        {
            var col = table.Columns[c];
            var cell = c < footer.Cells.Count ? footer.Cells[c] : null;
            var footerCell = ws.Cell(rowNumber, c + 1);
            XlsxStyleApplier.Apply(footerCell.Style, footerStyle);

            if (cell is null)
            {
                footerCell.Value = string.Empty;
                continue;
            }

            if (cell.Aggregate is { } kind)
            {
                var num = TableGrouping.Aggregate(kind, rows.Select(r => col.Binding.Evaluate(r)));
                XlsxValueWriter.Write(footerCell, num, cell.Format ?? col.Format, report.Culture);
            }
            else if (cell.Content is not null)
            {
                footerCell.Value = cell.Content.Evaluate(ctx);
            }

            if (cell.Align is { } al)
                footerCell.Style.Alignment.Horizontal = al switch
                {
                    NetReporter.Core.Styles.TextAlignment.Center  => XLAlignmentHorizontalValues.Center,
                    NetReporter.Core.Styles.TextAlignment.Right   => XLAlignmentHorizontalValues.Right,
                    NetReporter.Core.Styles.TextAlignment.Justify => XLAlignmentHorizontalValues.Justify,
                    _ => XLAlignmentHorizontalValues.Left
                };
        }
    }

    private static void EmitDataRow<TRow>(
        TableElement<TRow> table,
        TRow row,
        int rowNumber,
        NetReporter.Core.Styles.ResolvedStyle rowStyle,
        IXLWorksheet ws,
        ReportDefinition report)
    {
        for (int c = 0; c < table.Columns.Count; c++)
        {
            var col = table.Columns[c];
            var cell = ws.Cell(rowNumber, c + 1);
            var value = col.Binding.Evaluate(row);

            // Estilo: row + per-column alignment.
            var styleWithAlign = rowStyle with { TextAlign = col.Align };
            XlsxStyleApplier.Apply(cell.Style, styleWithAlign);

            // Valor con tipo preservado.
            XlsxValueWriter.Write(cell, value, col.Format, report.Culture);
        }
    }
}
