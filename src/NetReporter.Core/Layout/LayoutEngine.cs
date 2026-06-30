using System.Globalization;
using NetReporter.Core.Bands;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;
using RlRenderList = NetReporter.Core.RenderList.RenderList;

namespace NetReporter.Core.Layout;

public sealed class LayoutEngine
{
    private readonly ITextMeasurer _textMeasurer;
    private readonly IBarcodeMatrixGenerator _barcodeGenerator;

    public LayoutEngine() : this(EstimateTextMeasurer.Instance, ThrowingBarcodeGenerator.Instance) { }

    public LayoutEngine(ITextMeasurer textMeasurer)
        : this(textMeasurer, ThrowingBarcodeGenerator.Instance) { }

    /// <summary>
    /// Constructor con measurer + barcode generator. Usa <c>ZXingBarcodeGenerator.Instance</c>
    /// del proyecto NetReporter.Barcodes para soporte de QR/Code128/etc.
    /// </summary>
    public LayoutEngine(ITextMeasurer textMeasurer, IBarcodeMatrixGenerator barcodeGenerator)
    {
        _textMeasurer = textMeasurer ?? throw new ArgumentNullException(nameof(textMeasurer));
        _barcodeGenerator = barcodeGenerator ?? throw new ArgumentNullException(nameof(barcodeGenerator));
    }

    public RlRenderList Layout(ReportDefinition report)
    {
        var ctx = new LayoutContext { Culture = report.Culture };
        var pages = new List<RenderPage>();

        // Clasificar bandas
        var pageHeader   = report.Bands.OfType<PageHeaderBand>().FirstOrDefault();
        var pageFooter   = report.Bands.OfType<PageFooterBand>().FirstOrDefault();
        var reportHeader = report.Bands.OfType<ReportHeaderBand>().FirstOrDefault();
        var reportFooter = report.Bands.OfType<ReportFooterBand>().FirstOrDefault();
        var details      = report.Bands.OfType<DetailBand>().ToList();

        var pageHeaderH = pageHeader?.Height ?? 0;
        var pageFooterH = pageFooter?.Height ?? 0;

        var origin = report.Page.ContentOrigin;
        var usableHeight = report.Page.ContentHeight - pageHeaderH - pageFooterH;
        var contentWidth = report.Page.ContentWidth;

        // PASADA 1: emit bands con paginación preliminar. PageNumber real aún no final
        // porque no sabemos totalPages. Primera pasada asigna PageNumber provisional.
        var workingPage = new PageBuilder(1);
        double cursorY = origin.Y + pageHeaderH;
        var maxY = origin.Y + pageHeaderH + usableHeight;

        // ReportHeader
        if (reportHeader is not null)
        {
            // Pre-emit: si KeepTogether y la banda no cabe completa, page break antes de emitir.
            if (NeedsBreakForKeepTogether(reportHeader, cursorY, maxY))
            {
                FinishPage(workingPage, pageHeader, pages, origin, contentWidth, report, ctx);
                workingPage = new PageBuilder(pages.Count + 1);
                cursorY = origin.Y + pageHeaderH;
            }
            EmitBand(reportHeader, workingPage, origin.X, ref cursorY, report, ctx);
            // Post-emit: si sobrepasa (sin KeepTogether), abrimos página igual.
            if (cursorY > maxY)
            {
                FinishPage(workingPage, pageHeader, pages, origin, contentWidth, report, ctx);
                workingPage = new PageBuilder(pages.Count + 1);
                cursorY = origin.Y + pageHeaderH;
            }
        }

        // Detail bands
        foreach (var detail in details)
        {
            LayoutDetailBand(detail, report, workingPage, origin, ref cursorY, usableHeight,
                pageHeaderH, contentWidth, pages, ctx, ref maxY, pageHeader);
        }

        // ReportFooter — siempre se mueve a página nueva si su Height declarado no cabe
        // (comportamiento histórico). KeepTogether explícito lo refuerza pero no lo cambia.
        if (reportFooter is not null)
        {
            if (cursorY + reportFooter.Height > maxY)
            {
                FinishPage(workingPage, pageHeader, pages, origin, contentWidth, report, ctx);
                workingPage = new PageBuilder(pages.Count + 1);
                cursorY = origin.Y + pageHeaderH;
                maxY = origin.Y + pageHeaderH + usableHeight;
            }
            EmitBand(reportFooter, workingPage, origin.X, ref cursorY, report, ctx);
        }

        // Cerrar última página
        FinishPage(workingPage, pageHeader, pages, origin, contentWidth, report, ctx);

        // PASADA 2: ahora que conocemos totalPages, emitir page headers/footers definitivos.
        // En el prototipo los page headers/footers NO usan PageNumber en el texto aún
        // (sería trivial agregarlo con una segunda pasada que reemplace los DrawTextCommand
        // que contengan expresiones contextuales). Aquí dejamos texto estático + "Página N".
        var finalPages = RewritePageFooters(pages, pageFooter, pageHeader, report, origin,
            pageHeaderH, pageFooterH, contentWidth);

        return new RlRenderList(report.Page, finalPages);
    }

    private void LayoutDetailBand(
        DetailBand band,
        ReportDefinition report,
        PageBuilder workingPage,
        Point origin,
        ref double cursorY,
        double usableHeight,
        double pageHeaderH,
        double contentWidth,
        List<RenderPage> pages,
        LayoutContext ctx,
        ref double maxY,
        PageHeaderBand? pageHeader)
    {
        // Nota: KeepTogether en DetailBand no se aplica aquí por la limitación de ref locals
        // (no podemos reasignar workingPage en este scope). Para bandas de totales o resumen
        // que no deben partirse, usa ReportFooter (que ya hace el check) o ReportHeader con
        // KeepTogether=true. Las tablas grandes manejan su propia paginación por fila.

        // Para el prototipo, un DetailBand tiene elementos, y uno de esos puede ser una
        // TableElement<T>. Otros elementos se dibujan en sus Bounds relativos a la banda.
        var bandTop = cursorY;
        double naturalBottom = bandTop;

        foreach (var element in band.Elements)
        {
            if (element is TableElement<object>)
            {
                // Nunca llega aquí por varianza, pero cubrimos el caso.
                continue;
            }

            // Chequeo de si es una tabla genérica: usamos duck typing por tipo
            var elemType = element.GetType();
            if (elemType.IsGenericType && elemType.GetGenericTypeDefinition() == typeof(TableElement<>))
            {
                // Tablas se manejan con un helper genérico — modifica cursorY directamente.
                RenderTableElement(element, band, report, workingPage, origin,
                    ref cursorY, usableHeight, pageHeaderH, contentWidth, pages, ctx, ref maxY, pageHeader);
                if (cursorY > naturalBottom) naturalBottom = cursorY;
            }
            else
            {
                // Elementos no-tabla: verifica que quepan, si no, salto de página.
                var relBottom = cursorY + element.Bounds.Bottom;
                if (relBottom > maxY)
                {
                    FinishPage(workingPage, null, pages, origin, contentWidth, report, ctx);
                    var newPage = new PageBuilder(pages.Count + 1);
                    // transferir referencia: no podemos reasignar parámetro ref local aquí
                    // así que se hace inline en LayoutReport. Simplificamos el prototipo:
                    // no soportamos elementos no-tabla que crucen páginas.
                    workingPage.Commands.AddRange(newPage.Commands);
                }

                var elemBottom = EmitElement(element, workingPage, origin.X, cursorY, report, ctx);
                if (elemBottom > naturalBottom) naturalBottom = elemBottom;
            }
        }

        // AutoHeight: la banda crece para contener todo lo emitido (tabla incluida).
        // Sin AutoHeight: cursor avanza por la altura declarada (típicamente 0 en Detail con tabla,
        // ya que la tabla mueve cursorY directamente).
        if (band.AutoHeight)
        {
            cursorY = bandTop + Math.Max(band.Height, naturalBottom - bandTop);
        }
        else
        {
            // El cursor pudo haber avanzado por la tabla; respeta lo que esté más abajo.
            var declaredEnd = bandTop + band.Height;
            if (declaredEnd > cursorY) cursorY = declaredEnd;
        }
    }

    // Workaround para la limitación de ref locales + tipos genéricos:
    // usamos reflection UNA VEZ para obtener el método genérico y lo cacheamos.
    // Esto NO es reflection en el hot path — es una vez por tipo de tabla por reporte.
    private static readonly System.Reflection.MethodInfo s_renderTableMethod =
        typeof(LayoutEngine).GetMethod(nameof(RenderTableElementGeneric),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    private void RenderTableElement(
        ReportElement element,
        DetailBand band,
        ReportDefinition report,
        PageBuilder workingPage,
        Point origin,
        ref double cursorY,
        double usableHeight,
        double pageHeaderH,
        double contentWidth,
        List<RenderPage> pages,
        LayoutContext ctx,
        ref double maxY,
        PageHeaderBand? pageHeader)
    {
        var rowType = element.GetType().GetGenericArguments()[0];
        var method = s_renderTableMethod.MakeGenericMethod(rowType);

        var args = new object?[] { element, report, workingPage, origin,
            cursorY, usableHeight, pageHeaderH, contentWidth, pages, ctx, pageHeader };

        var newCursorY = (double)method.Invoke(this, args)!;
        cursorY = newCursorY;
    }

    private double RenderTableElementGeneric<TRow>(
        TableElement<TRow> table,
        ReportDefinition report,
        PageBuilder workingPage,
        Point origin,
        double cursorY,
        double usableHeight,
        double pageHeaderH,
        double contentWidth,
        List<RenderPage> pages,
        LayoutContext ctx,
        PageHeaderBand? pageHeader)
    {
        var currentPage = workingPage;
        var maxY = origin.Y + pageHeaderH + usableHeight;
        var sourcePath = table.SourcePath;

        var headerStyle = report.Styles.Resolve(table.HeaderStyle);
        var rowStyle = report.Styles.Resolve(table.RowStyle);
        var altRowStyle = table.AlternateRowStyle is { } alt
            ? report.Styles.Resolve(alt) : rowStyle;

        var tableX = origin.X + table.Bounds.X;
        var tableWidth = table.Bounds.Width;

        double segmentTop = cursorY;

        void DrawOuterBorder(double top, double bottom)
        {
            if (table.OuterBorder is { } ob && bottom > top)
                currentPage.Commands.Add(new DrawRectangleCommand(
                    new Rect(tableX, top, tableWidth, bottom - top),
                    null, ob, table.CornerRadius) { SourcePath = sourcePath });
        }

        // Dibuja el header
        void DrawHeader(double y)
        {
            var colX = tableX;
            foreach (var col in table.Columns)
            {
                currentPage.Commands.Add(new DrawRectangleCommand(
                    new Rect(colX, y, col.Width, table.HeaderHeight),
                    headerStyle.Background == Color.Transparent ? null : headerStyle.Background,
                    headerStyle.Border?.Bottom) { SourcePath = sourcePath });
                // Header text follows the column's alignment so numeric headers sit over their right-aligned values.
                var cellStyle = headerStyle with { TextAlign = col.Align };
                currentPage.Commands.Add(new DrawTextCommand(
                    new Rect(colX + cellStyle.Padding.Left, y + cellStyle.Padding.Top,
                             col.Width - cellStyle.Padding.Horizontal,
                             table.HeaderHeight - cellStyle.Padding.Vertical),
                    col.Header,
                    cellStyle) { SourcePath = sourcePath });
                colX += col.Width;
            }

            // Optional single full-width rule under the header (no per-cell box / vertical lines).
            if (table.HeaderRule is { } hr)
            {
                var ruleY = y + table.HeaderHeight;
                currentPage.Commands.Add(new DrawLineCommand(
                    new Point(tableX, ruleY), new Point(tableX + tableWidth, ruleY),
                    hr.Thickness, hr.Color) { SourcePath = sourcePath });
            }
        }

        DrawHeader(cursorY);
        cursorY += table.HeaderHeight;

        // Cache de estilos de columna resueltos (una vez por tabla).
        var columnStyleCache = new Dictionary<string, ResolvedStyle>(StringComparer.Ordinal);

        // Aplica el estilo de columna (si lo hay) SOBRE el estilo de fila: la columna aporta tipografía
        // (p.ej. mono para código/importe); el fondo y el peso de la banda siempre vienen de la fila, y el
        // color de texto de la columna solo gana en filas SIN banda (fondo transparente) — en una banda de
        // total el importe usa el color de la banda, no el color atenuado de la columna.
        ResolvedStyle MergeColumnStyle(ResolvedStyle rowStyleResolved, TableColumn<TRow> col)
        {
            if (col.Style is not { } styleRef) return rowStyleResolved;
            if (!columnStyleCache.TryGetValue(styleRef.Name, out var colStyle))
            {
                colStyle = report.Styles.Resolve(styleRef);
                columnStyleCache[styleRef.Name] = colStyle;
            }

            var banded = rowStyleResolved.Background != Color.Transparent;
            return rowStyleResolved with
            {
                FontFamily = colStyle.FontFamily,
                Foreground = banded ? rowStyleResolved.Foreground : colStyle.Foreground,
            };
        }

        // Resuelve el estilo de una fila tipada (financial statements): evalúa el selector, lo busca en el
        // mapa, y usa ese estilo si existe; en cualquier otro caso cae al estilo normal/alternado.
        ResolvedStyle ResolveRowStyle(TRow row, int rowIndex)
        {
            var fallback = (rowIndex % 2 == 1) ? altRowStyle : rowStyle;
            if (table.RowStyleSelector is null || table.RowStyleMap is null) return fallback;

            var key = table.RowStyleSelector.Evaluate(row)?.ToString();
            if (key is not null && table.RowStyleMap.TryGetValue(key, out var styleRef)
                && report.Styles.Contains(styleRef.Name))
                return report.Styles.Resolve(styleRef);
            return fallback;
        }

        // Helper: emite una fila normal de datos.
        void EmitRow(TRow row, int rowIndex)
        {
            var effectiveStyle = ResolveRowStyle(row, rowIndex);
            var colX = tableX;

            // Full-row background: una sola banda de ancho completo (sin costuras entre celdas).
            if (table.FullRowBackground && effectiveStyle.Background != Color.Transparent)
            {
                currentPage.Commands.Add(new DrawRectangleCommand(
                    new Rect(tableX, cursorY, tableWidth, table.RowHeight),
                    effectiveStyle.Background, null) { SourcePath = sourcePath });
            }

            // Full-width top rule from the row style's Border.Top — the monochrome "total over a rule"
            // look (a typed total row draws a line above itself spanning the whole table).
            if (effectiveStyle.Border?.Top is { } topRule)
            {
                currentPage.Commands.Add(new DrawLineCommand(
                    new Point(tableX, cursorY), new Point(tableX + tableWidth, cursorY),
                    topRule.Thickness, topRule.Color) { SourcePath = sourcePath });
            }

            foreach (var col in table.Columns)
            {
                ctx.CurrentRow = row;
                ctx.RowIndex = rowIndex;

                var value = col.Binding.Evaluate(row);
                var text = FormatValue(value, col.Format, report.Culture);

                if (!table.FullRowBackground && effectiveStyle.Background != Color.Transparent)
                {
                    currentPage.Commands.Add(new DrawRectangleCommand(
                        new Rect(colX, cursorY, col.Width, table.RowHeight),
                        effectiveStyle.Background, null) { SourcePath = sourcePath });
                }

                var cellStyle = MergeColumnStyle(effectiveStyle, col) with { TextAlign = col.Align };
                currentPage.Commands.Add(new DrawTextCommand(
                    new Rect(colX + cellStyle.Padding.Left, cursorY + cellStyle.Padding.Top,
                             col.Width - cellStyle.Padding.Horizontal,
                             table.RowHeight - cellStyle.Padding.Vertical),
                    text,
                    cellStyle) { SourcePath = sourcePath });

                colX += col.Width;
            }

            // Optional full-width hairline at the bottom of the row (lined-ledger look).
            if (table.RowSeparator is { } sep)
            {
                var sepY = cursorY + table.RowHeight;
                currentPage.Commands.Add(new DrawLineCommand(
                    new Point(tableX, sepY), new Point(tableX + tableWidth, sepY),
                    sep.Thickness, sep.Color) { SourcePath = sourcePath });
            }
            cursorY += table.RowHeight;
        }

        // Helper: page-break check + repeat header si aplica.
        void EnsureSpace(double rowHeight)
        {
            if (cursorY + rowHeight > maxY)
            {
                DrawOuterBorder(segmentTop, cursorY);
                // Insert the repeating page header into the page we're about to close, so multi-page
                // tables keep the masthead on every page (not just the first).
                InsertPageHeader(currentPage);
                pages.Add(currentPage.Build());
                currentPage = new PageBuilder(pages.Count + 1);
                cursorY = origin.Y + pageHeaderH;
                segmentTop = cursorY;
                if (table.HeaderMode == TableHeaderMode.RepeatOnPageBreak)
                {
                    DrawHeader(cursorY);
                    cursorY += table.HeaderHeight;
                }
            }
        }

        // Inserts the report's PageHeader band at the top of a page (mirrors FinishPage), so a page the
        // table itself closes carries the masthead. No-op when there's no page header.
        void InsertPageHeader(PageBuilder page)
        {
            if (pageHeader is null) return;
            var tmp = new PageBuilder(page.PageNumber);
            double y = origin.Y;
            EmitBand(pageHeader, tmp, origin.X, ref y, report, ctx);
            page.Commands.InsertRange(0, tmp.Commands);
        }

        // === Sin agrupación: loop simple ===
        if (table.GroupBy is null)
        {
            var rowIndex = 0;
            foreach (var row in table.Rows)
            {
                EnsureSpace(table.RowHeight);
                EmitRow(row, rowIndex);
                rowIndex++;
            }
        }
        else
        {
            // === Con agrupación: filas consecutivas con mismo key forman un grupo ===
            // Para que GroupHeader pueda usar {{ #count }} con el conteo total del grupo,
            // bufferear el grupo entero antes de emitir.
            var groupBuffer = new List<TRow>();
            object? currentKey = null;
            int globalRowIndex = 0;

            void EmitCurrentGroup()
            {
                if (groupBuffer.Count == 0) return;

                // === Group header ===
                if (table.GroupHeader is not null)
                {
                    EnsureSpace(table.GroupHeader.Height);
                    ctx.GroupKey = currentKey;
                    ctx.GroupRowCount = groupBuffer.Count;
                    var ghStyle = report.Styles.Resolve(table.GroupHeader.Style);
                    if (ghStyle.Background != Color.Transparent)
                    {
                        currentPage.Commands.Add(new DrawRectangleCommand(
                            new Rect(tableX, cursorY, tableWidth, table.GroupHeader.Height),
                            ghStyle.Background, ghStyle.Border?.Bottom) { SourcePath = sourcePath });
                    }
                    var content = table.GroupHeader.Content.Evaluate(ctx);
                    currentPage.Commands.Add(new DrawTextCommand(
                        new Rect(tableX + ghStyle.Padding.Left, cursorY + ghStyle.Padding.Top,
                                 tableWidth - ghStyle.Padding.Horizontal,
                                 table.GroupHeader.Height - ghStyle.Padding.Vertical),
                        content, ghStyle) { SourcePath = sourcePath });
                    cursorY += table.GroupHeader.Height;
                }

                // === Filas del grupo ===
                foreach (var row in groupBuffer)
                {
                    EnsureSpace(table.RowHeight);
                    EmitRow(row, globalRowIndex++);
                }

                // === Group footer ===
                if (table.GroupFooter is not null)
                {
                    EnsureSpace(table.GroupFooter.Height);
                    ctx.GroupKey = currentKey;
                    ctx.GroupRowCount = groupBuffer.Count;
                    var gfStyle = report.Styles.Resolve(table.GroupFooter.Style);
                    var colX = tableX;

                    for (int i = 0; i < table.Columns.Count; i++)
                    {
                        var col = table.Columns[i];
                        var cell = i < table.GroupFooter.Cells.Count ? table.GroupFooter.Cells[i] : null;

                        if (gfStyle.Background != Color.Transparent)
                        {
                            currentPage.Commands.Add(new DrawRectangleCommand(
                                new Rect(colX, cursorY, col.Width, table.GroupFooter.Height),
                                gfStyle.Background, null) { SourcePath = sourcePath });
                        }

                        string text = "";
                        if (cell is not null)
                        {
                            if (cell.Aggregate is { } kind)
                            {
                                var values = groupBuffer.Select(r => col.Binding.Evaluate(r));
                                var num = ComputeAggregate(kind, values);
                                text = FormatValue(num, cell.Format ?? col.Format, report.Culture);
                            }
                            else if (cell.Content is not null)
                            {
                                text = cell.Content.Evaluate(ctx);
                            }
                        }

                        var alignedStyle = gfStyle with { TextAlign = cell?.Align ?? col.Align };
                        currentPage.Commands.Add(new DrawTextCommand(
                            new Rect(colX + alignedStyle.Padding.Left, cursorY + alignedStyle.Padding.Top,
                                     col.Width - alignedStyle.Padding.Horizontal,
                                     table.GroupFooter.Height - alignedStyle.Padding.Vertical),
                            text, alignedStyle) { SourcePath = sourcePath });

                        colX += col.Width;
                    }
                    cursorY += table.GroupFooter.Height;
                }
            }

            foreach (var row in table.Rows)
            {
                var key = table.GroupBy.Evaluate(row);
                if (groupBuffer.Count == 0)
                {
                    currentKey = key;
                    groupBuffer.Add(row);
                }
                else if (ObjectsEqual(currentKey, key))
                {
                    groupBuffer.Add(row);
                }
                else
                {
                    // Cambia de grupo: emitir actual, empezar nuevo.
                    EmitCurrentGroup();
                    groupBuffer = new List<TRow> { row };
                    currentKey = key;
                }
            }

            // Último grupo
            EmitCurrentGroup();

            // Reset group state en el contexto.
            ctx.GroupKey = null;
            ctx.GroupRowCount = 0;
        }

        DrawOuterBorder(segmentTop, cursorY);

        // Si la tabla abrió páginas nuevas, currentPage tiene el número CORRECTO (pages.Count+1) pero el
        // caller seguirá usando workingPage para el ReportFooter y para cerrar la última página. Copiamos
        // el contenido Y el número de página a workingPage para que la última página (con su footer) quede
        // bien numerada — antes el número quedaba en 1 y se duplicaba la página.
        if (!ReferenceEquals(currentPage, workingPage))
        {
            workingPage.Commands.Clear();
            workingPage.Commands.AddRange(currentPage.Commands);
            workingPage.PageNumber = currentPage.PageNumber;
        }

        return cursorY;
    }

    private static bool ObjectsEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.Equals(b);
    }

    /// <summary>Aplica Sum/Count/Avg sobre una secuencia de valores (con conversión segura a double).</summary>
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
        null     => null,
        double d => d,
        float f  => f,
        int i    => i,
        long l   => l,
        decimal m => (double)m,
        string s => double.TryParse(s, System.Globalization.NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var d) ? d : null,
        _ => null
    };

    private static string FormatValue(object? value, string? format, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        if (format is null) return value.ToString() ?? string.Empty;

        return value switch
        {
            IFormattable f => f.ToString(format, culture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private void EmitBand(
        Band band,
        PageBuilder page,
        double originX,
        ref double cursorY,
        ReportDefinition report,
        LayoutContext ctx)
    {
        var bandTop = cursorY;
        double naturalBottom = bandTop;     // bottom Y máximo alcanzado por algún elemento
        foreach (var element in band.Elements)
        {
            var elemBottom = EmitElement(element, page, originX, bandTop, report, ctx);
            if (elemBottom > naturalBottom) naturalBottom = elemBottom;
        }

        // Sin AutoHeight: el cursor avanza por la altura declarada (truncando si los elementos exceden).
        // Con AutoHeight: el cursor avanza por la altura efectiva (max declarado vs natural).
        var advance = band.AutoHeight
            ? Math.Max(band.Height, naturalBottom - bandTop)
            : band.Height;
        cursorY += advance;
    }

    private double EmitElement(
        ReportElement element,
        PageBuilder page,
        double originX,
        double bandTopY,
        ReportDefinition report,
        LayoutContext ctx)
    {
        var style = report.Styles.Resolve(element.Style);
        var absBounds = new Rect(
            originX + element.Bounds.X,
            bandTopY + element.Bounds.Y,
            element.Bounds.Width,
            element.Bounds.Height);
        var path = element.SourcePath;

        switch (element)
        {
            case TextElement text:
                // Honor the style's padding the same way table cells do, so a text element styled with
                // the same named style as a table column lines up with that column's cells.
                var textBounds = new Rect(
                    absBounds.X + style.Padding.Left,
                    absBounds.Y + style.Padding.Top,
                    absBounds.Width - style.Padding.Horizontal,
                    absBounds.Height - style.Padding.Vertical);
                return EmitText(text, textBounds, style, path, ctx, page) + style.Padding.Bottom;

            case LineElement line:
                var (from, to) = line.Orientation == LineOrientation.Horizontal
                    ? (new Point(absBounds.X, absBounds.Y),
                       new Point(absBounds.Right, absBounds.Y))
                    : (new Point(absBounds.X, absBounds.Y),
                       new Point(absBounds.X, absBounds.Bottom));
                page.Commands.Add(new DrawLineCommand(from, to, line.Thickness, line.Color)
                    { SourcePath = path });
                return absBounds.Bottom;

            case RectangleElement rect:
                page.Commands.Add(new DrawRectangleCommand(
                    absBounds, rect.Fill, rect.BorderLine, rect.CornerRadius) { SourcePath = path });
                return absBounds.Bottom;

            case ImageElement image:
                page.Commands.Add(new DrawImageCommand(
                    absBounds, image.Data, image.MimeType, image.Fit) { SourcePath = path });
                return absBounds.Bottom;

            case BarcodeElement barcode:
                EmitBarcode(barcode, absBounds, path, ctx, page);
                return absBounds.Bottom;

            default:
                // Tablas se manejan en RenderTableElement.
                return absBounds.Bottom;
        }
    }

    /// <summary>
    /// Convierte un <see cref="BarcodeElement"/> en N <see cref="DrawRectangleCommand"/>
    /// (uno por módulo) — el código resulta vectorial.
    /// </summary>
    private void EmitBarcode(
        BarcodeElement barcode, Rect absBounds, string? sourcePath,
        LayoutContext ctx, PageBuilder page)
    {
        var value = barcode.Value.Evaluate(ctx) ?? string.Empty;
        if (absBounds.Width <= 0 || absBounds.Height <= 0) return;

        // El generator devuelve el matrix natural (un píxel por módulo).
        // El LayoutEngine escala los módulos al rect destino.
        var matrix = _barcodeGenerator.Generate(value, barcode.Format);

        var moduleW = absBounds.Width  / matrix.GetLength(0);
        var moduleH = absBounds.Height / matrix.GetLength(1);

        // Fondo opcional (un solo rect en vez de pintar módulos claros).
        if (barcode.Background.A > 0)
        {
            page.Commands.Add(new DrawRectangleCommand(
                absBounds, barcode.Background, null) { SourcePath = sourcePath });
        }

        // Pintar módulos oscuros como rectángulos. Coalescing horizontal opcional para
        // reducir comandos — por ahora simple: un rect por módulo. Ok para tamaños típicos.
        for (int x = 0; x < matrix.GetLength(0); x++)
        {
            for (int y = 0; y < matrix.GetLength(1); y++)
            {
                if (!matrix[x, y]) continue;
                var modRect = new Rect(
                    absBounds.X + x * moduleW,
                    absBounds.Y + y * moduleH,
                    moduleW, moduleH);
                page.Commands.Add(new DrawRectangleCommand(
                    modRect, barcode.Foreground, null) { SourcePath = sourcePath });
            }
        }
    }

    /// <summary>
    /// Devuelve <c>true</c> si la banda tiene KeepTogether y su altura declarada no cabe
    /// en lo que queda de la página actual.
    /// </summary>
    private static bool NeedsBreakForKeepTogether(Band band, double cursorY, double maxY)
    {
        if (!band.KeepTogether) return false;
        if (band.Height <= 0) return false;     // sin altura conocida no podemos decidir
        return cursorY + band.Height > maxY;
    }

    /// <summary>
    /// Emite uno o varios <see cref="DrawTextCommand"/> según wrap. Devuelve el bottom Y absoluto
    /// efectivo (último renglón + lineHeight), que el caller usa para AutoHeight de la banda.
    /// </summary>
    private double EmitText(
        TextElement text,
        Rect absBounds,
        ResolvedStyle style,
        string? sourcePath,
        LayoutContext ctx,
        PageBuilder page)
    {
        var content = text.Content.Evaluate(ctx) ?? string.Empty;

        // Si el wrap está deshabilitado o no hay ancho útil, emitir un solo command (clipping en renderer).
        if (!text.WordWrap || absBounds.Width <= 0)
        {
            page.Commands.Add(new DrawTextCommand(absBounds, content, style) { SourcePath = sourcePath });
            return absBounds.Bottom;
        }

        // Aplicar word wrap. Calcular altura de línea desde el measurer.
        var lineHeight = _textMeasurer.LineHeight(style);
        var lines = _textMeasurer.WrapLines(content, style, absBounds.Width);
        if (lines.Count == 0) return absBounds.Y;

        // Auto-height: la altura efectiva del bloque es lineCount * lineHeight (override del Bounds.Height).
        // Sin auto-height: respeta el Bounds.Height — líneas que excedan se recortan en el renderer.
        var maxLines = text.AutoHeight
            ? lines.Count
            : Math.Max(1, (int)Math.Floor(absBounds.Height / lineHeight));

        var renderable = lines.Count <= maxLines ? lines : lines.Take(maxLines).ToArray();

        for (int i = 0; i < renderable.Count; i++)
        {
            var lineRect = new Rect(absBounds.X, absBounds.Y + i * lineHeight,
                                     absBounds.Width, lineHeight);
            page.Commands.Add(new DrawTextCommand(lineRect, renderable[i], style)
            {
                SourcePath = sourcePath
            });
        }

        // Bottom Y real: arranca del top + N líneas × lineHeight.
        return absBounds.Y + renderable.Count * lineHeight;
    }

    private void FinishPage(
        PageBuilder page,
        PageHeaderBand? pageHeader,
        List<RenderPage> pages,
        Point origin,
        double contentWidth,
        ReportDefinition report,
        LayoutContext ctx)
    {
        // Emitir page header al inicio de la página (si corresponde)
        if (pageHeader is not null)
        {
            var headerCommands = new List<RenderCommand>();
            var tmp = new PageBuilder(page.PageNumber);
            double y = origin.Y;
            EmitBand(pageHeader, tmp, origin.X, ref y, report, ctx);
            // Insertar al principio
            page.Commands.InsertRange(0, tmp.Commands);
        }

        pages.Add(page.Build());
    }

    private List<RenderPage> RewritePageFooters(
        List<RenderPage> pages,
        PageFooterBand? pageFooter,
        PageHeaderBand? pageHeader,
        ReportDefinition report,
        Point origin,
        double pageHeaderH,
        double pageFooterH,
        double contentWidth)
    {
        if (pageFooter is null) return pages;

        var total = pages.Count;
        var result = new List<RenderPage>(total);
        var ctx = new LayoutContext { Culture = report.Culture, TotalPages = total };

        foreach (var page in pages)
        {
            ctx.PageNumber = page.PageNumber;
            var footerCommands = new List<RenderCommand>();
            var tmp = new PageBuilder(page.PageNumber);
            double y = origin.Y + pageHeaderH + report.Page.ContentHeight
                       - pageHeaderH - pageFooterH;
            EmitBand(pageFooter, tmp, origin.X, ref y, report, ctx);

            var combined = new List<RenderCommand>(page.Commands);
            combined.AddRange(tmp.Commands);
            result.Add(new RenderPage(page.PageNumber, combined));
        }

        return result;
    }
}
