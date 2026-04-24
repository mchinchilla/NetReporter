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
            EmitBand(reportHeader, workingPage, origin.X, ref cursorY, report, ctx);
            // Si sobrepasa, abrimos página (poco común para ReportHeader, pero robusto)
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
                pageHeaderH, contentWidth, pages, ctx, ref maxY);
        }

        // ReportFooter
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
        ref double maxY)
    {
        // Para el prototipo, un DetailBand tiene elementos, y uno de esos puede ser una
        // TableElement<T>. Otros elementos se dibujan en sus Bounds relativos a la banda.
        foreach (var element in band.Elements)
        {
            if (element is TableElement<object> genericTable)
            {
                // Nunca llega aquí por varianza, pero cubrimos el caso.
                continue;
            }

            // Chequeo de si es una tabla genérica: usamos duck typing por tipo
            var elemType = element.GetType();
            if (elemType.IsGenericType && elemType.GetGenericTypeDefinition() == typeof(TableElement<>))
            {
                // Tablas se manejan con un helper genérico
                RenderTableElement(element, band, report, workingPage, origin,
                    ref cursorY, usableHeight, pageHeaderH, contentWidth, pages, ctx, ref maxY);
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

                EmitElement(element, workingPage, origin.X, cursorY, report, ctx);
            }
        }

        cursorY += band.Height;
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
        ref double maxY)
    {
        var rowType = element.GetType().GetGenericArguments()[0];
        var method = s_renderTableMethod.MakeGenericMethod(rowType);

        var args = new object?[] { element, report, workingPage, origin,
            cursorY, usableHeight, pageHeaderH, contentWidth, pages, ctx };

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
        LayoutContext ctx)
    {
        var currentPage = workingPage;
        var maxY = origin.Y + pageHeaderH + usableHeight;

        var headerStyle = report.Styles.Resolve(table.HeaderStyle);
        var rowStyle = report.Styles.Resolve(table.RowStyle);
        var altRowStyle = table.AlternateRowStyle is { } alt
            ? report.Styles.Resolve(alt) : rowStyle;

        var tableX = origin.X + table.Bounds.X;
        var tableWidth = table.Bounds.Width;

        // Dibuja el header
        void DrawHeader(double y)
        {
            var colX = tableX;
            foreach (var col in table.Columns)
            {
                var cellStyle = headerStyle;
                currentPage.Commands.Add(new DrawRectangleCommand(
                    new Rect(colX, y, col.Width, table.HeaderHeight),
                    cellStyle.Background == Color.Transparent ? null : cellStyle.Background,
                    cellStyle.Border?.Bottom));
                currentPage.Commands.Add(new DrawTextCommand(
                    new Rect(colX + cellStyle.Padding.Left, y + cellStyle.Padding.Top,
                             col.Width - cellStyle.Padding.Horizontal,
                             table.HeaderHeight - cellStyle.Padding.Vertical),
                    col.Header,
                    cellStyle));
                colX += col.Width;
            }
        }

        DrawHeader(cursorY);
        cursorY += table.HeaderHeight;

        // Filas
        var rowIndex = 0;
        foreach (var row in table.Rows)
        {
            // Si no cabe esta fila en la página, abrimos nueva
            if (cursorY + table.RowHeight > maxY)
            {
                // Cerrar página actual
                pages.Add(currentPage.Build());
                currentPage = new PageBuilder(pages.Count + 1);
                cursorY = origin.Y + pageHeaderH;

                if (table.HeaderMode == TableHeaderMode.RepeatOnPageBreak)
                {
                    DrawHeader(cursorY);
                    cursorY += table.HeaderHeight;
                }
            }

            var effectiveStyle = (rowIndex % 2 == 1) ? altRowStyle : rowStyle;
            var colX = tableX;

            foreach (var col in table.Columns)
            {
                ctx.CurrentRow = row;
                ctx.RowIndex = rowIndex;

                var value = col.Binding.Evaluate(row);
                var text = FormatValue(value, col.Format, report.Culture);

                // Fondo de celda (si el estilo define background)
                if (effectiveStyle.Background != Color.Transparent)
                {
                    currentPage.Commands.Add(new DrawRectangleCommand(
                        new Rect(colX, cursorY, col.Width, table.RowHeight),
                        effectiveStyle.Background, null));
                }

                // Aplicar alineación específica de la columna (override)
                var cellStyle = effectiveStyle with { TextAlign = col.Align };

                currentPage.Commands.Add(new DrawTextCommand(
                    new Rect(colX + cellStyle.Padding.Left, cursorY + cellStyle.Padding.Top,
                             col.Width - cellStyle.Padding.Horizontal,
                             table.RowHeight - cellStyle.Padding.Vertical),
                    text,
                    cellStyle));

                colX += col.Width;
            }

            cursorY += table.RowHeight;
            rowIndex++;
        }

        // Importante: transferir el estado final al workingPage original
        // Si abrimos nuevas páginas, el workingPage original ya fue cerrado
        // y currentPage puede ser uno nuevo. Sincronizamos comandos:
        if (!ReferenceEquals(currentPage, workingPage))
        {
            workingPage.Commands.Clear();
            workingPage.Commands.AddRange(currentPage.Commands);
            // Y el número de página del workingPage se vuelve el de currentPage
            // (pero PageBuilder es inmutable en su PageNumber — aceptamos que
            // quien nos llame use pages.Count + 1 al crear el siguiente).
        }

        return cursorY;
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

    private void EmitBand(
        Band band,
        PageBuilder page,
        double originX,
        ref double cursorY,
        ReportDefinition report,
        LayoutContext ctx)
    {
        foreach (var element in band.Elements)
            EmitElement(element, page, originX, cursorY, report, ctx);

        cursorY += band.Height;
    }

    private void EmitElement(
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
                page.Commands.Add(new DrawTextCommand(
                    absBounds, text.Content.Evaluate(ctx), style) { SourcePath = path });
                break;

            case LineElement line:
                var (from, to) = line.Orientation == LineOrientation.Horizontal
                    ? (new Point(absBounds.X, absBounds.Y),
                       new Point(absBounds.Right, absBounds.Y))
                    : (new Point(absBounds.X, absBounds.Y),
                       new Point(absBounds.X, absBounds.Bottom));
                page.Commands.Add(new DrawLineCommand(from, to, line.Thickness, line.Color)
                    { SourcePath = path });
                break;

            case RectangleElement rect:
                page.Commands.Add(new DrawRectangleCommand(
                    absBounds, rect.Fill, rect.BorderLine) { SourcePath = path });
                break;

            default:
                // Tablas se manejan en RenderTableElement.
                break;
        }
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
