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
        ArgumentNullException.ThrowIfNull(report);
        return new LayoutRun(report, _textMeasurer, _barcodeGenerator).Execute();
    }

    /// <summary>
    /// Estado de una corrida de layout. Mantener la página en curso, el cursor y el límite inferior
    /// como campos (en vez de parámetros <c>ref</c>) permite que cualquier punto del layout abra una
    /// página nueva — base de KeepTogether en DetailBand — y hacer "mediciones en seco" de una banda
    /// (emitirla en una página temporal sin límite para conocer su altura real).
    /// </summary>
    private sealed class LayoutRun : ITableVisitor<bool>
    {
        private readonly ReportDefinition _report;
        private readonly ITextMeasurer _measurer;
        private readonly IBarcodeMatrixGenerator _barcodes;
        private readonly LayoutContext _ctx;
        private readonly List<RenderPage> _pages = new();

        private readonly PageHeaderBand? _pageHeader;
        private readonly PageFooterBand? _pageFooter;
        private readonly Point _origin;
        private readonly double _pageHeaderH;
        private readonly double _pageFooterH;
        private readonly double _usableHeight;

        // Página en curso: siempre la MISMA instancia; NewPage() la vuelca a _pages y la reinicia.
        private PageBuilder _page = new(1);
        private double _cursorY;
        private double _maxY;
        private bool _measuring;

        public LayoutRun(ReportDefinition report, ITextMeasurer measurer, IBarcodeMatrixGenerator barcodes)
        {
            _report = report;
            _measurer = measurer;
            _barcodes = barcodes;
            _ctx = new LayoutContext { Culture = report.Culture, PageNumber = 1 };

            _pageHeader = report.Bands.OfType<PageHeaderBand>().FirstOrDefault();
            _pageFooter = report.Bands.OfType<PageFooterBand>().FirstOrDefault();
            _pageHeaderH = _pageHeader?.Height ?? 0;
            _pageFooterH = _pageFooter?.Height ?? 0;
            _origin = report.Page.ContentOrigin;
            _usableHeight = report.Page.ContentHeight - _pageHeaderH - _pageFooterH;
            _cursorY = TopY;
            _maxY = TopY + _usableHeight;
        }

        private double TopY => _origin.Y + _pageHeaderH;
        private bool PageIsEmpty => _cursorY <= TopY + 0.01;

        public RlRenderList Execute()
        {
            var reportHeader = _report.Bands.OfType<ReportHeaderBand>().FirstOrDefault();
            var reportFooter = _report.Bands.OfType<ReportFooterBand>().FirstOrDefault();

            // PASADA 1: contenido + paginación.
            if (reportHeader is not null && IsVisible(reportHeader))
            {
                // Pre-emit: con KeepTogether, si la banda no cabe completa, salto antes de emitir.
                if (reportHeader.KeepTogether && !PageIsEmpty)
                {
                    var h = BandHeight(reportHeader);
                    if (h > 0 && _cursorY + h > _maxY) NewPage();
                }
                EmitBand(reportHeader, _page, ref _cursorY);
                // Post-emit: si sobrepasa (sin KeepTogether), abrimos página igual.
                if (_cursorY > _maxY) NewPage();
            }

            foreach (var detail in _report.Bands.OfType<DetailBand>())
                LayoutDetailBand(detail);

            // ReportFooter — siempre se mueve a página nueva si no cabe completo.
            if (reportFooter is not null && IsVisible(reportFooter))
            {
                if (_cursorY + BandHeight(reportFooter) > _maxY && !PageIsEmpty) NewPage();
                EmitBand(reportFooter, _page, ref _cursorY);
            }

            _pages.Add(_page.Build());

            // PASADA 2: con el total de páginas conocido, page header/footer definitivos
            // ({{ pageNumber }} / {{ totalPages }} resueltos en ambos).
            return new RlRenderList(_report.Page, EmitPageChrome());
        }

        // === Paginación ===

        private void NewPage()
        {
            if (_measuring) return;
            _pages.Add(_page.Build());
            _page.Commands.Clear();
            _page.PageNumber = _pages.Count + 1;
            _ctx.PageNumber = _page.PageNumber;
            _cursorY = TopY;
        }

        /// <summary>
        /// Mide una emisión "en seco": la ejecuta sobre una página temporal, sin límite inferior y sin
        /// abrir páginas, y devuelve cuánto avanzó el cursor. Todo el estado se restaura al terminar.
        /// </summary>
        private double Measure(Action emit)
        {
            var (page, cursor, max, measuring) = (_page, _cursorY, _maxY, _measuring);
            _page = new PageBuilder(page.PageNumber);
            _cursorY = 0;
            _maxY = double.MaxValue;
            _measuring = true;
            try
            {
                emit();
                return _cursorY;
            }
            finally
            {
                (_page, _cursorY, _maxY, _measuring) = (page, cursor, max, measuring);
            }
        }

        /// <summary>Altura efectiva de una banda no-detail: la declarada, o la medida si es auto-height.</summary>
        private double BandHeight(Band band) =>
            band.AutoHeight
                ? Measure(() => EmitBand(band, _page, ref _cursorY))
                : band.Height;

        private List<RenderPage> EmitPageChrome()
        {
            if (_pageHeader is null && _pageFooter is null) return _pages;

            var total = _pages.Count;
            var result = new List<RenderPage>(total);
            var ctx = new LayoutContext { Culture = _report.Culture, TotalPages = total };

            foreach (var page in _pages)
            {
                ctx.PageNumber = page.PageNumber;
                var isFirst = page.PageNumber == 1;
                var isLast = page.PageNumber == total;
                var commands = new List<RenderCommand>();

                if (_pageHeader is not null && PrintsOn(_pageHeader, isFirst, isLast) && IsVisible(_pageHeader, ctx))
                {
                    var tmp = new PageBuilder(page.PageNumber);
                    var y = _origin.Y;
                    EmitBand(_pageHeader, tmp, ref y, ctx);
                    commands.AddRange(tmp.Commands);
                }

                commands.AddRange(page.Commands);

                if (_pageFooter is not null && PrintsOn(_pageFooter, isFirst, isLast) && IsVisible(_pageFooter, ctx))
                {
                    var tmp = new PageBuilder(page.PageNumber);
                    var y = _origin.Y + _report.Page.ContentHeight - _pageFooterH;
                    EmitBand(_pageFooter, tmp, ref y, ctx);
                    commands.AddRange(tmp.Commands);
                }

                result.Add(new RenderPage(page.PageNumber, commands));
            }

            return result;
        }

        private static bool PrintsOn(Band band, bool isFirst, bool isLast) =>
            (!isFirst || band.PrintOnFirstPage) && (!isLast || band.PrintOnLastPage || isFirst);

        private bool IsVisible(Band band, LayoutContext? ctx = null) =>
            band.Visible is not { } v || v.Evaluate(ctx ?? _ctx);

        // === Detail bands ===

        private void LayoutDetailBand(DetailBand band)
        {
            if (!IsVisible(band)) return;

            // Una banda sin tablas no puede partirse (sus elementos se posicionan relativos a su tope),
            // así que si no cabe se mueve entera a la página siguiente. Con tablas, la tabla pagina por
            // fila; KeepTogether fuerza igualmente a moverla entera si cabe en una página vacía.
            var hasTable = band.Elements.Any(e => e is ITableElement);
            if (!_measuring && !PageIsEmpty && (band.KeepTogether || !hasTable))
            {
                var height = Measure(() => EmitDetailBand(band));
                if (_cursorY + height > _maxY && height <= _usableHeight + 0.01)
                    NewPage();
            }

            EmitDetailBand(band);
        }

        private void EmitDetailBand(DetailBand band)
        {
            var bandTop = _cursorY;
            var naturalBottom = bandTop;

            foreach (var element in band.Elements)
            {
                if (!IsVisible(element)) continue;

                if (element is ITableElement table)
                {
                    // Las tablas mueven el cursor directamente (y abren páginas si hace falta).
                    var cursorBeforeTable = _cursorY;
                    // Bounds.Y es un desplazamiento desde la posición actual del flujo (la banda o la tabla
                    // anterior): permite poner un título arriba de la tabla dentro de la misma banda.
                    if (element.Bounds.Y > 0) _cursorY += element.Bounds.Y;
                    var pagesBefore = _pages.Count;
                    table.Accept(this);
                    if (_pages.Count != pagesBefore)
                    {
                        // La tabla abrió páginas: el resto de la banda se mide desde el tope de la actual.
                        bandTop = TopY;
                        naturalBottom = _cursorY;
                        cursorBeforeTable = TopY;
                    }
                    if (_cursorY > naturalBottom) naturalBottom = _cursorY;
                    // Side-by-side tables: a suppressed table draws but does not advance the cursor, so the
                    // next table starts at the same Y. The band still grows via naturalBottom (set above).
                    if (table.SuppressAdvance) _cursorY = cursorBeforeTable;
                }
                else
                {
                    var elemBottom = EmitElement(element, _page, _origin.X, _cursorY, _ctx);
                    if (elemBottom > naturalBottom) naturalBottom = elemBottom;
                }
            }

            if (band.AutoHeight)
            {
                _cursorY = bandTop + Math.Max(band.Height, naturalBottom - bandTop);
            }
            else
            {
                var declaredEnd = bandTop + band.Height;
                if (declaredEnd > _cursorY) _cursorY = declaredEnd;
            }
        }

        // === Tablas ===

        bool ITableVisitor<bool>.Visit<TRow>(TableElement<TRow> table)
        {
            RenderTable(table);
            return true;
        }

        private void RenderTable<TRow>(TableElement<TRow> table)
        {
            var report = _report;
            var ctx = _ctx;
            var sourcePath = table.SourcePath;

            var headerStyle = report.Styles.Resolve(table.HeaderStyle);
            var rowStyle = report.Styles.Resolve(table.RowStyle);
            var altRowStyle = table.AlternateRowStyle is { } alt
                ? report.Styles.Resolve(alt) : rowStyle;

            var tableX = _origin.X + table.Bounds.X;
            var tableWidth = table.Bounds.Width;
            var segmentTop = _cursorY;

            void Add(RenderCommand cmd) => _page.Commands.Add(cmd with { SourcePath = sourcePath });

            void DrawOuterBorder(double top, double bottom)
            {
                if (table.OuterBorder is { } ob && bottom > top)
                    Add(new DrawRectangleCommand(new Rect(tableX, top, tableWidth, bottom - top), null, ob, table.CornerRadius));
            }

            void DrawHeader(double y)
            {
                var colX = tableX;
                foreach (var col in table.Columns)
                {
                    Add(new DrawRectangleCommand(
                        new Rect(colX, y, col.Width, table.HeaderHeight),
                        headerStyle.Background == Color.Transparent ? null : headerStyle.Background,
                        headerStyle.Border?.Bottom));
                    // Header text follows the column's alignment so numeric headers sit over their right-aligned values.
                    var cellStyle = headerStyle with { TextAlign = col.Align };
                    Add(new DrawTextCommand(
                        new Rect(colX + cellStyle.Padding.Left, y + cellStyle.Padding.Top,
                                 col.Width - cellStyle.Padding.Horizontal,
                                 table.HeaderHeight - cellStyle.Padding.Vertical),
                        col.Header, cellStyle));
                    colX += col.Width;
                }

                // Optional single full-width rule under the header (no per-cell box / vertical lines).
                if (table.HeaderRule is { } hr)
                {
                    var ruleY = y + table.HeaderHeight;
                    Add(new DrawLineCommand(new Point(tableX, ruleY), new Point(tableX + tableWidth, ruleY), hr.Thickness, hr.Color));
                }
            }

            DrawHeader(_cursorY);
            _cursorY += table.HeaderHeight;

            // Cache de estilos de columna resueltos (una vez por tabla).
            var columnStyleCache = new Dictionary<string, ResolvedStyle>(StringComparer.Ordinal);

            // Aplica el estilo de columna (si lo hay) SOBRE el estilo de fila: la columna aporta tipografía
            // (p.ej. mono para código/importe); el fondo y el peso de la banda siempre vienen de la fila, y el
            // color de texto de la columna solo gana en filas SIN banda (fondo transparente).
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

            // Resuelve el estilo de una fila tipada: evalúa el selector, lo busca en el mapa, y usa ese
            // estilo si existe; en cualquier otro caso cae al estilo normal/alternado.
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

            void EmitRow(TRow row, int rowIndex)
            {
                var effectiveStyle = ResolveRowStyle(row, rowIndex);
                var colX = tableX;

                // Full-row background: una sola banda de ancho completo (sin costuras entre celdas).
                if (table.FullRowBackground && effectiveStyle.Background != Color.Transparent)
                    Add(new DrawRectangleCommand(new Rect(tableX, _cursorY, tableWidth, table.RowHeight), effectiveStyle.Background, null));

                // Full-width top rule from the row style's Border.Top ("total over a rule").
                if (effectiveStyle.Border?.Top is { } topRule)
                    Add(new DrawLineCommand(new Point(tableX, _cursorY), new Point(tableX + tableWidth, _cursorY), topRule.Thickness, topRule.Color));

                ctx.CurrentRow = row;
                ctx.RowIndex = rowIndex;
                foreach (var col in table.Columns)
                {
                    var value = col.Binding.Evaluate(row);
                    var text = FormatValue(value, col.Format, report.Culture);

                    if (!table.FullRowBackground && effectiveStyle.Background != Color.Transparent)
                        Add(new DrawRectangleCommand(new Rect(colX, _cursorY, col.Width, table.RowHeight), effectiveStyle.Background, null));

                    var cellStyle = MergeColumnStyle(effectiveStyle, col) with { TextAlign = col.Align };
                    Add(new DrawTextCommand(
                        new Rect(colX + cellStyle.Padding.Left, _cursorY + cellStyle.Padding.Top,
                                 col.Width - cellStyle.Padding.Horizontal,
                                 table.RowHeight - cellStyle.Padding.Vertical),
                        text, cellStyle));

                    colX += col.Width;
                }

                // Full-width bottom rule from the row style's Border.Bottom (continuous, not per-cell).
                if (effectiveStyle.Border?.Bottom is { } bottomRule)
                {
                    var ruleY = _cursorY + table.RowHeight;
                    Add(new DrawLineCommand(new Point(tableX, ruleY), new Point(tableX + tableWidth, ruleY), bottomRule.Thickness, bottomRule.Color));
                }

                // Optional full-width hairline at the bottom of the row (lined-ledger look).
                if (table.RowSeparator is { } sep)
                {
                    var sepY = _cursorY + table.RowHeight;
                    Add(new DrawLineCommand(new Point(tableX, sepY), new Point(tableX + tableWidth, sepY), sep.Thickness, sep.Color));
                }
                _cursorY += table.RowHeight;
            }

            // Page-break check + repeat header si aplica.
            void EnsureSpace(double height)
            {
                if (_cursorY + height <= _maxY) return;
                DrawOuterBorder(segmentTop, _cursorY);
                NewPage();
                segmentTop = _cursorY;
                if (table.HeaderMode == TableHeaderMode.RepeatOnPageBreak)
                {
                    DrawHeader(_cursorY);
                    _cursorY += table.HeaderHeight;
                }
            }

            void EmitGroupHeader(GroupHeader header)
            {
                // Keep-with-next: un encabezado de grupo nunca queda huérfano al pie de la página.
                EnsureSpace(header.Height + table.RowHeight);
                var ghStyle = report.Styles.Resolve(header.Style);
                if (ghStyle.Background != Color.Transparent)
                    Add(new DrawRectangleCommand(new Rect(tableX, _cursorY, tableWidth, header.Height), ghStyle.Background, ghStyle.Border?.Bottom));
                var content = header.Content.Evaluate(ctx);
                Add(new DrawTextCommand(
                    new Rect(tableX + ghStyle.Padding.Left, _cursorY + ghStyle.Padding.Top,
                             tableWidth - ghStyle.Padding.Horizontal,
                             header.Height - ghStyle.Padding.Vertical),
                    content, ghStyle));
                _cursorY += header.Height;
            }

            // Fila de agregados: pie de grupo o resumen de la tabla. Celdas paralelas a las columnas.
            void EmitAggregateRow(GroupFooter footer, IReadOnlyList<TRow> rows)
            {
                EnsureSpace(footer.Height);
                var gfStyle = report.Styles.Resolve(footer.Style);
                var colX = tableX;

                if (gfStyle.Border?.Top is { } topRule)
                    Add(new DrawLineCommand(new Point(tableX, _cursorY), new Point(tableX + tableWidth, _cursorY), topRule.Thickness, topRule.Color));

                for (var i = 0; i < table.Columns.Count; i++)
                {
                    var col = table.Columns[i];
                    var cell = i < footer.Cells.Count ? footer.Cells[i] : null;

                    if (gfStyle.Background != Color.Transparent)
                        Add(new DrawRectangleCommand(new Rect(colX, _cursorY, col.Width, footer.Height), gfStyle.Background, null));

                    var text = "";
                    if (cell is not null)
                    {
                        if (cell.Aggregate is { } kind)
                        {
                            var num = TableGrouping.Aggregate(kind, rows.Select(r => col.Binding.Evaluate(r)));
                            text = FormatValue(num, cell.Format ?? col.Format, report.Culture);
                        }
                        else if (cell.Content is not null)
                        {
                            text = cell.Content.Evaluate(ctx);
                        }
                    }

                    var alignedStyle = gfStyle with { TextAlign = cell?.Align ?? col.Align };
                    Add(new DrawTextCommand(
                        new Rect(colX + alignedStyle.Padding.Left, _cursorY + alignedStyle.Padding.Top,
                                 col.Width - alignedStyle.Padding.Horizontal,
                                 footer.Height - alignedStyle.Padding.Vertical),
                        text, alignedStyle));

                    colX += col.Width;
                }

                if (gfStyle.Border?.Bottom is { } bottomRule)
                {
                    var ruleY = _cursorY + footer.Height;
                    Add(new DrawLineCommand(new Point(tableX, ruleY), new Point(tableX + tableWidth, ruleY), bottomRule.Thickness, bottomRule.Color));
                }
                _cursorY += footer.Height;
            }

            var levels = table.EffectiveGroups;
            var globalRowIndex = 0;

            void SetGroupContext(object? key, int count, int depth)
            {
                ctx.GroupKey = key;
                ctx.GroupRowCount = count;
                while (ctx.GroupKeyStack.Count > depth) ctx.GroupKeyStack.RemoveAt(ctx.GroupKeyStack.Count - 1);
                ctx.GroupKeyStack.Add(key);
            }

            // Recorre los niveles: en cada uno parte las filas del grupo padre en sub-grupos consecutivos.
            void EmitLevel(IReadOnlyList<TRow> rows, int level)
            {
                if (level == levels.Count)
                {
                    foreach (var row in rows)
                    {
                        EnsureSpace(table.RowHeight);
                        EmitRow(row, globalRowIndex++);
                    }
                    return;
                }

                var group = levels[level];
                foreach (var (key, groupRows) in TableGrouping.Partition(rows, group.By))
                {
                    SetGroupContext(key, groupRows.Count, level);
                    if (group.Header is not null) EmitGroupHeader(group.Header);

                    EmitLevel(groupRows, level + 1);

                    // Los niveles internos cambiaron el contexto: se restaura antes del pie de este grupo.
                    SetGroupContext(key, groupRows.Count, level);
                    if (group.Footer is not null) EmitAggregateRow(group.Footer, groupRows);
                }
            }

            EmitLevel(table.Rows, 0);

            ctx.GroupKey = null;
            ctx.GroupRowCount = table.Rows.Count;
            ctx.GroupKeyStack.Clear();
            if (table.Summary is not null) EmitAggregateRow(table.Summary, table.Rows);
            ctx.GroupRowCount = 0;

            DrawOuterBorder(segmentTop, _cursorY);
        }

        // === Bandas y elementos ===

        private void EmitBand(Band band, PageBuilder page, ref double cursorY, LayoutContext? ctx = null)
        {
            ctx ??= _ctx;
            var bandTop = cursorY;
            var naturalBottom = bandTop;     // bottom Y máximo alcanzado por algún elemento
            foreach (var element in band.Elements)
            {
                if (!IsVisible(element, ctx)) continue;
                var elemBottom = EmitElement(element, page, _origin.X, bandTop, ctx);
                if (elemBottom > naturalBottom) naturalBottom = elemBottom;
            }

            // Sin AutoHeight: el cursor avanza por la altura declarada (truncando si los elementos exceden).
            // Con AutoHeight: el cursor avanza por la altura efectiva (max declarado vs natural).
            var advance = band.AutoHeight
                ? Math.Max(band.Height, naturalBottom - bandTop)
                : band.Height;
            cursorY += advance;
        }

        private bool IsVisible(ReportElement element, LayoutContext? ctx = null) =>
            element.Visible is not { } v || v.Evaluate(ctx ?? _ctx);

        private ResolvedStyle ResolveStyle(ReportElement element, LayoutContext ctx)
        {
            if (element.StyleSelector is { } selector)
            {
                var name = selector.Evaluate(ctx);
                if (!string.IsNullOrWhiteSpace(name) && _report.Styles.Contains(name.Trim()))
                    return _report.Styles.Resolve(new StyleRef(name.Trim()));
            }
            return _report.Styles.Resolve(element.Style);
        }

        private double EmitElement(ReportElement element, PageBuilder page, double originX, double bandTopY, LayoutContext ctx)
        {
            var style = ResolveStyle(element, ctx);
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
                        ? (new Point(absBounds.X, absBounds.Y), new Point(absBounds.Right, absBounds.Y))
                        : (new Point(absBounds.X, absBounds.Y), new Point(absBounds.X, absBounds.Bottom));
                    page.Commands.Add(new DrawLineCommand(from, to, line.Thickness, line.Color) { SourcePath = path });
                    return absBounds.Bottom;

                case RectangleElement rect:
                    page.Commands.Add(new DrawRectangleCommand(
                        absBounds, rect.Fill, rect.BorderLine, rect.CornerRadius, rect.Corners) { SourcePath = path });
                    return absBounds.Bottom;

                case ImageElement image:
                    page.Commands.Add(new DrawImageCommand(absBounds, image.Data, image.MimeType, image.Fit) { SourcePath = path });
                    return absBounds.Bottom;

                case BarcodeElement barcode:
                    EmitBarcode(barcode, absBounds, path, ctx, page);
                    return absBounds.Bottom;

                case ChartElement chart:
                    ChartLayout.Emit(chart, absBounds, style, _measurer, _report.Culture, page.Commands);
                    return absBounds.Bottom;

                default:
                    // Tablas se manejan en RenderTable (solo dentro de DetailBand).
                    return absBounds.Bottom;
            }
        }

        /// <summary>
        /// Convierte un <see cref="BarcodeElement"/> en N <see cref="DrawRectangleCommand"/>
        /// (uno por módulo) — el código resulta vectorial.
        /// </summary>
        private void EmitBarcode(BarcodeElement barcode, Rect absBounds, string? sourcePath, LayoutContext ctx, PageBuilder page)
        {
            var value = barcode.Value.Evaluate(ctx) ?? string.Empty;
            if (absBounds.Width <= 0 || absBounds.Height <= 0) return;

            // El generator devuelve el matrix natural (un píxel por módulo); se escala al rect destino.
            var matrix = _barcodes.Generate(value, barcode.Format);

            var moduleW = absBounds.Width  / matrix.GetLength(0);
            var moduleH = absBounds.Height / matrix.GetLength(1);

            // Fondo opcional (un solo rect en vez de pintar módulos claros).
            if (barcode.Background.A > 0)
                page.Commands.Add(new DrawRectangleCommand(absBounds, barcode.Background, null) { SourcePath = sourcePath });

            for (var x = 0; x < matrix.GetLength(0); x++)
            {
                for (var y = 0; y < matrix.GetLength(1); y++)
                {
                    if (!matrix[x, y]) continue;
                    var modRect = new Rect(absBounds.X + x * moduleW, absBounds.Y + y * moduleH, moduleW, moduleH);
                    page.Commands.Add(new DrawRectangleCommand(modRect, barcode.Foreground, null) { SourcePath = sourcePath });
                }
            }
        }

        /// <summary>
        /// Emite uno o varios <see cref="DrawTextCommand"/> según wrap. Devuelve el bottom Y absoluto
        /// efectivo (último renglón + lineHeight), que el caller usa para AutoHeight de la banda.
        /// </summary>
        private double EmitText(TextElement text, Rect absBounds, ResolvedStyle style, string? sourcePath,
            LayoutContext ctx, PageBuilder page)
        {
            var content = text.Content.Evaluate(ctx) ?? string.Empty;

            // Si el wrap está deshabilitado o no hay ancho útil, emitir un solo command (clipping en renderer).
            if (!text.WordWrap || absBounds.Width <= 0)
            {
                page.Commands.Add(new DrawTextCommand(absBounds, content, style) { SourcePath = sourcePath });
                return absBounds.Bottom;
            }

            var lineHeight = _measurer.LineHeight(style);
            var lines = _measurer.WrapLines(content, style, absBounds.Width);
            if (lines.Count == 0) return absBounds.Y;

            // Auto-height: la altura efectiva del bloque es lineCount * lineHeight (override del Bounds.Height).
            // Sin auto-height: respeta el Bounds.Height — líneas que excedan se recortan.
            var maxLines = text.AutoHeight
                ? lines.Count
                : Math.Max(1, (int)Math.Floor(absBounds.Height / lineHeight));

            var renderable = lines.Count <= maxLines ? lines : lines.Take(maxLines).ToArray();

            for (var i = 0; i < renderable.Count; i++)
            {
                var lineRect = new Rect(absBounds.X, absBounds.Y + i * lineHeight, absBounds.Width, lineHeight);
                page.Commands.Add(new DrawTextCommand(lineRect, renderable[i], style) { SourcePath = sourcePath });
            }

            return absBounds.Y + renderable.Count * lineHeight;
        }

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
    }
}
