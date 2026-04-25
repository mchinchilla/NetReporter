using System.Reflection;
using ClosedXML.Excel;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;

namespace NetReporter.Xlsx;

/// <summary>
/// Emite un <see cref="TableElement{TRow}"/> como un rango semántico de Excel:
/// header row + data rows con tipos preservados. Opcionalmente lo envuelve como
/// Excel Table nativa para tener filtros/sorting/banded rows.
///
/// Patrón: usamos reflection UNA VEZ por tipo de fila (igual que LayoutEngine) para
/// invocar <see cref="EmitAtGeneric{TRow}"/>. NO es reflection en hot path.
/// </summary>
internal static class XlsxTableEmitter
{
    private static readonly MethodInfo s_emitAtGenericMethod =
        typeof(XlsxTableEmitter).GetMethod(nameof(EmitAtGeneric),
            BindingFlags.Static | BindingFlags.NonPublic)!;

    /// <summary>
    /// Detecta si <paramref name="element"/> es <see cref="TableElement{TRow}"/> por reflection.
    /// </summary>
    public static bool IsTable(ReportElement element)
    {
        var t = element.GetType();
        return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(TableElement<>);
    }

    /// <summary>
    /// Emite la tabla empezando en <paramref name="startRow"/> (1-based) y devuelve la última fila usada.
    /// </summary>
    public static int EmitAt(
        ReportElement element,
        int startRow,
        IXLWorksheet ws,
        ReportDefinition report,
        XlsxEvaluationContext ctx,
        XlsxRenderOptions options)
    {
        var rowType = element.GetType().GetGenericArguments()[0];
        var method = s_emitAtGenericMethod.MakeGenericMethod(rowType);
        return (int)method.Invoke(null, new object?[] { element, startRow, ws, report, ctx, options })!;
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

        if (table.GroupBy is null)
        {
            for (int i = 0; i < table.Rows.Count; i++)
            {
                ctx.RowIndex = i;
                ctx.CurrentRow = table.Rows[i];
                var style = (i % 2 == 1) ? altStyle : rowStyle;
                EmitDataRow(table, table.Rows[i], currentRow, style, ws, report, ctx);
                currentRow++;
            }
        }
        else
        {
            currentRow = EmitGrouped(table, currentRow, ws, report, ctx, rowStyle, altStyle);
        }

        // Crear Excel Table nativa si hay filas y la opción está activa.
        var dataEnd = currentRow - 1;
        if (options.EmitNativeTables && dataEnd >= dataStart && table.Rows.Count > 0 && table.GroupBy is null)
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
        int startDataRow,
        IXLWorksheet ws,
        ReportDefinition report,
        XlsxEvaluationContext ctx,
        NetReporter.Core.Styles.ResolvedStyle rowStyle,
        NetReporter.Core.Styles.ResolvedStyle altStyle)
    {
        var groupBy = table.GroupBy!;
        var groupHeader = table.GroupHeader;
        var groupFooter = table.GroupFooter;

        var groupHeaderStyle = groupHeader is not null ? report.Styles.Resolve(groupHeader.Style) : rowStyle;
        var groupFooterStyle = groupFooter is not null ? report.Styles.Resolve(groupFooter.Style) : rowStyle;

        var currentRow = startDataRow;
        var globalRowIndex = 0;
        var buffer = new List<TRow>();
        object? currentKey = null;

        void EmitGroup()
        {
            if (buffer.Count == 0) return;

            ctx.GroupKey = currentKey;
            ctx.GroupRowCount = buffer.Count;

            // Group header: se mergea sobre todas las columnas de la tabla.
            if (groupHeader is not null)
            {
                var content = groupHeader.Content.Evaluate(ctx);
                var headerCell = ws.Cell(currentRow, 1);
                headerCell.Value = content;
                XlsxStyleApplier.Apply(headerCell.Style, groupHeaderStyle);
                if (table.Columns.Count > 1)
                    ws.Range(currentRow, 1, currentRow, table.Columns.Count).Merge();
                currentRow++;
            }

            // Filas del grupo.
            foreach (var row in buffer)
            {
                ctx.CurrentRow = row;
                ctx.RowIndex = globalRowIndex;
                var style = (globalRowIndex % 2 == 1) ? altStyle : rowStyle;
                EmitDataRow(table, row, currentRow, style, ws, report, ctx);
                currentRow++;
                globalRowIndex++;
            }

            // Group footer: una celda por columna, paralela a la tabla.
            if (groupFooter is not null)
            {
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    var col = table.Columns[c];
                    var cell = c < groupFooter.Cells.Count ? groupFooter.Cells[c] : null;
                    var footerCell = ws.Cell(currentRow, c + 1);
                    XlsxStyleApplier.Apply(footerCell.Style, groupFooterStyle);

                    if (cell is null)
                    {
                        footerCell.Value = string.Empty;
                        continue;
                    }

                    if (cell.Aggregate is { } kind)
                    {
                        var values = buffer.Select(r => col.Binding.Evaluate(r));
                        var num = ComputeAggregate(kind, values);
                        XlsxValueWriter.Write(footerCell, num, cell.Format ?? col.Format, report.Culture);
                    }
                    else if (cell.Content is not null)
                    {
                        footerCell.Value = cell.Content.Evaluate(ctx);
                    }

                    if (cell.Align is { } al)
                        footerCell.Style.Alignment.Horizontal = al switch
                        {
                            NetReporter.Core.Styles.TextAlignment.Left    => XLAlignmentHorizontalValues.Left,
                            NetReporter.Core.Styles.TextAlignment.Center  => XLAlignmentHorizontalValues.Center,
                            NetReporter.Core.Styles.TextAlignment.Right   => XLAlignmentHorizontalValues.Right,
                            NetReporter.Core.Styles.TextAlignment.Justify => XLAlignmentHorizontalValues.Justify,
                            _ => XLAlignmentHorizontalValues.Left
                        };
                }
                currentRow++;
            }
        }

        foreach (var row in table.Rows)
        {
            var key = groupBy.Evaluate(row);
            if (buffer.Count == 0)
            {
                currentKey = key;
                buffer.Add(row);
            }
            else if (Equals(currentKey, key))
            {
                buffer.Add(row);
            }
            else
            {
                EmitGroup();
                buffer = new List<TRow> { row };
                currentKey = key;
            }
        }

        EmitGroup();

        ctx.GroupKey = null;
        ctx.GroupRowCount = 0;

        return currentRow;
    }

    private static void EmitDataRow<TRow>(
        TableElement<TRow> table,
        TRow row,
        int rowNumber,
        NetReporter.Core.Styles.ResolvedStyle rowStyle,
        IXLWorksheet ws,
        ReportDefinition report,
        XlsxEvaluationContext ctx)
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

    private static double ComputeAggregate(AggregateKind kind, IEnumerable<object?> values)
    {
        if (kind == AggregateKind.Count)
            return values.Count(v => v is not null);

        var nums = values.Select(ToDouble).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return kind switch
        {
            AggregateKind.Sum => nums.Sum(),
            AggregateKind.Avg => nums.Count == 0 ? 0 : nums.Average(),
            _ => 0
        };
    }

    private static double? ToDouble(object? v) => v switch
    {
        null      => null,
        double d  => d,
        float f   => f,
        int i     => i,
        long l    => l,
        decimal m => (double)m,
        string s  => double.TryParse(s, System.Globalization.NumberStyles.Any,
                          System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null,
        _ => null
    };

}
