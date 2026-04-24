using Microsoft.AspNetCore.Mvc;
using NetReporter.Core.Layout;
using NetReporter.Core.RenderList;
using NetReporter.Designer.Models;
using NetReporter.Designer.Services;
using NetReporter.Html;
using NetReporter.Pdf;
using NetReporter.Svg;
using NetReporter.Templates;
using NetReporter.Templates.Schema;

namespace NetReporter.Designer.Controllers;

public sealed class HomeController : Controller
{
    private readonly SvgRenderer _svg = new();
    private readonly TemplateStore _store;

    public HomeController(TemplateStore store)
    {
        _store = store;
    }

    public IActionResult Index()
    {
        return View(new DesignerViewModel
        {
            Yaml = DefaultContent.Yaml,
            Json = DefaultContent.Json
        });
    }

    [HttpPost]
    public IActionResult Preview([FromForm] PreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PartialView("_Preview", BuildPreview(request.Yaml, request.Json));
    }

    [HttpPost]
    public async Task<IActionResult> Move([FromForm] MoveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ApplyChange(
            request.Json,
            () => YamlReportRewriter.MoveElement(
                request.Yaml ?? string.Empty,
                request.Path ?? string.Empty,
                request.DeltaX,
                request.DeltaY));
    }

    [HttpPost]
    public async Task<IActionResult> Update([FromForm] UpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var patch = new ElementPatch
        {
            X = request.X, Y = request.Y, Width = request.Width, Height = request.Height,
            Style = request.Style, Content = request.Content,
            Color = request.Color, Thickness = request.Thickness, Fill = request.Fill,
            Rows = request.Rows,
            HeaderMode = request.HeaderMode,
            HeaderHeight = request.HeaderHeight,
            RowHeight = request.RowHeight,
            HeaderStyle = request.HeaderStyle,
            RowStyle = request.RowStyle,
            AlternateRowStyle = request.AlternateRowStyle
        };
        return await ApplyChange(
            request.Json,
            () => YamlReportRewriter.ApplyPatch(
                request.Yaml ?? string.Empty,
                request.Path ?? string.Empty,
                patch));
    }

    [HttpPost]
    public async Task<IActionResult> Delete([FromForm] DeleteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ApplyChange(
            request.Json,
            () => YamlReportRewriter.DeleteElement(
                request.Yaml ?? string.Empty,
                request.Path ?? string.Empty));
    }

    [HttpPost]
    public async Task<IActionResult> UpdateColumns([FromForm] UpdateColumnsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<TableColumnYaml> cols;
        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<TableColumnPayload>>(
                request.ColumnsJson ?? "[]",
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<TableColumnPayload>();
            cols = parsed.Select(p => new TableColumnYaml
            {
                Header = p.Header,
                Binding = p.Binding,
                Width = p.Width,
                Format = string.IsNullOrWhiteSpace(p.Format) ? null : p.Format,
                Align = string.IsNullOrWhiteSpace(p.Align) ? "left" : p.Align
            }).ToList();
        }
        catch (Exception ex)
        {
            return Json(new { error = $"Columns JSON inválido: {ex.Message}" });
        }

        return await ApplyChange(
            request.Json,
            () => YamlReportRewriter.UpdateColumns(
                request.Yaml ?? string.Empty,
                request.Path ?? string.Empty,
                cols));
    }

    [HttpPost]
    public async Task<IActionResult> Duplicate([FromForm] DuplicateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string newYaml;
        string newPath;
        try
        {
            (newYaml, newPath) = YamlReportRewriter.DuplicateElement(
                request.Yaml ?? string.Empty,
                request.Path ?? string.Empty);
        }
        catch (Exception ex)
        {
            return Json(new { error = $"{ex.GetType().Name}: {ex.Message}" });
        }

        var preview = BuildPreview(newYaml, request.Json ?? "{}");
        var previewHtml = await RenderPartialToString("_Preview", preview);
        return Json(new { yaml = newYaml, previewHtml, newPath });
    }

    [HttpPost]
    public async Task<IActionResult> AddBand([FromForm] AddBandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ApplyChange(
            request.Json,
            () => YamlReportRewriter.AddBand(
                request.Yaml ?? string.Empty,
                request.Kind ?? "Detail",
                request.Height).Yaml);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteBand([FromForm] BandIndexRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ApplyChange(
            request.Json,
            () => YamlReportRewriter.DeleteBand(
                request.Yaml ?? string.Empty,
                request.BandIndex));
    }

    [HttpPost]
    public async Task<IActionResult> MoveBand([FromForm] MoveBandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ApplyChange(
            request.Json,
            () => YamlReportRewriter.MoveBand(
                request.Yaml ?? string.Empty,
                request.FromIndex,
                request.ToIndex));
    }

    [HttpPost]
    public IActionResult ExportPdf([FromForm] PreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var template = YamlReportLoader.Parse(request.Yaml ?? string.Empty);
            var report = template.Bind(request.Json ?? "{}");
            var layout = new LayoutEngine().Layout(report);
            var bytes = new PdfRenderer().Render(layout);
            return File(bytes, "application/pdf", "report.pdf");
        }
        catch (Exception ex)
        {
            return Content($"Error al exportar PDF: {ex.GetType().Name}: {ex.Message}",
                           "text/plain", System.Text.Encoding.UTF8);
        }
    }

    [HttpPost]
    public IActionResult ExportHtml([FromForm] PreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var template = YamlReportLoader.Parse(request.Yaml ?? string.Empty);
            var report = template.Bind(request.Json ?? "{}");
            var layout = new LayoutEngine().Layout(report);
            var html = new HtmlRenderer().Render(layout,
                new HtmlRenderOptions { Title = report.Title ?? report.Name });
            return Content(html, "text/html", System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            return Content($"<!doctype html><body><pre>Error: {System.Net.WebUtility.HtmlEncode(ex.Message)}</pre></body>",
                           "text/html", System.Text.Encoding.UTF8);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromForm] AddRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string newYaml;
        string newPath;
        try
        {
            (newYaml, newPath) = YamlReportRewriter.AddElement(
                request.Yaml ?? string.Empty,
                request.BandIndex,
                request.Kind ?? "text",
                request.X,
                request.Y);
        }
        catch (Exception ex)
        {
            return Json(new { error = $"{ex.GetType().Name}: {ex.Message}" });
        }

        var preview = BuildPreview(newYaml, request.Json ?? "{}");
        var previewHtml = await RenderPartialToString("_Preview", preview);
        return Json(new { yaml = newYaml, previewHtml, newPath });
    }

    // === Template persistence ===

    [HttpGet]
    public IActionResult ListTemplates()
    {
        try { return Json(_store.List()); }
        catch (Exception ex) { return Json(new { error = $"{ex.GetType().Name}: {ex.Message}" }); }
    }

    [HttpGet]
    public IActionResult LoadTemplate(string name)
    {
        try
        {
            var (yaml, json) = _store.Load(name ?? string.Empty);
            return Json(new { yaml, json });
        }
        catch (Exception ex) { return Json(new { error = $"{ex.GetType().Name}: {ex.Message}" }); }
    }

    [HttpPost]
    public IActionResult SaveTemplate([FromForm] SaveTemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            _store.Save(request.Name ?? string.Empty, request.Yaml ?? string.Empty, request.Json ?? "{}");
            return Json(new { saved = true });
        }
        catch (Exception ex) { return Json(new { error = $"{ex.GetType().Name}: {ex.Message}" }); }
    }

    [HttpPost]
    public IActionResult DeleteTemplate([FromForm] DeleteTemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            _store.Delete(request.Name ?? string.Empty);
            return Json(new { deleted = true });
        }
        catch (Exception ex) { return Json(new { error = $"{ex.GetType().Name}: {ex.Message}" }); }
    }

    // === Helpers ===

    private async Task<IActionResult> ApplyChange(string? json, Func<string> rewrite)
    {
        string newYaml;
        try { newYaml = rewrite(); }
        catch (Exception ex) { return Json(new { error = $"{ex.GetType().Name}: {ex.Message}" }); }

        var preview = BuildPreview(newYaml, json ?? "{}");
        var previewHtml = await RenderPartialToString("_Preview", preview);
        return Json(new { yaml = newYaml, previewHtml });
    }

    // === Helpers ===

    private PreviewViewModel BuildPreview(string? yaml, string? json)
    {
        try
        {
            var template = YamlReportLoader.Parse(yaml ?? string.Empty);
            var yamlModel = YamlReportLoader.ParseModel(yaml ?? string.Empty);
            var report = template.Bind(json ?? "{}");
            var layout = new LayoutEngine().Layout(report);
            var svgPages = _svg.Render(layout);

            // Mapa path → ElementYaml para enriquecer InteractiveElement con props actuales.
            var elementByPath = BuildElementIndex(yamlModel);

            var bandKinds = (yamlModel.Bands ?? new List<BandYaml>())
                .Select(b => b.Kind ?? "Detail")
                .ToArray();

            var pages = new List<PreviewPage>(svgPages.Count);
            for (int i = 0; i < svgPages.Count; i++)
            {
                var interactive = ExtractInteractive(layout.Pages[i], elementByPath);
                var bands = ExtractBandRanges(layout.Pages[i], bandKinds, report.Page.Margins.Top);
                pages.Add(new PreviewPage(i + 1, svgPages[i], interactive, bands));
            }

            var styleNames = yamlModel.Styles?.Keys.ToArray() ?? Array.Empty<string>();

            return new PreviewViewModel
            {
                Pages = pages,
                StyleNames = styleNames,
                PageWidthPt = report.Page.Size.Width,
                PageHeightPt = report.Page.Size.Height,
                MarginLeftPt = report.Page.Margins.Left,
                MarginTopPt = report.Page.Margins.Top
            };
        }
        catch (Exception ex)
        {
            return new PreviewViewModel { Error = $"{ex.GetType().Name}: {ex.Message}" };
        }
    }

    private static Dictionary<string, ElementYaml> BuildElementIndex(ReportYaml model)
    {
        var dict = new Dictionary<string, ElementYaml>(StringComparer.Ordinal);
        var bands = model.Bands ?? new List<BandYaml>();
        for (int i = 0; i < bands.Count; i++)
        {
            var elements = bands[i].Elements ?? new List<ElementYaml>();
            for (int j = 0; j < elements.Count; j++)
                dict[$"bands.{i}.elements.{j}"] = elements[j];
        }
        return dict;
    }

    private static IReadOnlyList<BandRange> ExtractBandRanges(
        RenderPage page,
        IReadOnlyList<string> bandKinds,
        double marginTop)
    {
        // Agrupa comandos por bandIndex extraído del SourcePath ("bands.N.elements.M").
        var byBand = new Dictionary<int, (double MinY, double MaxBottom)>();
        foreach (var cmd in page.Commands)
        {
            if (cmd.SourcePath is null) continue;
            var parts = cmd.SourcePath.Split('.');
            if (parts.Length < 2 || !int.TryParse(parts[1], out var idx)) continue;

            if (byBand.TryGetValue(idx, out var existing))
            {
                byBand[idx] = (
                    Math.Min(existing.MinY, cmd.Bounds.Y),
                    Math.Max(existing.MaxBottom, cmd.Bounds.Bottom));
            }
            else
            {
                byBand[idx] = (cmd.Bounds.Y, cmd.Bounds.Bottom);
            }
        }

        // Emite un rango por cada banda declarada en el YAML. Si no hay commands trazables
        // (banda vacía), genera un rango sintético de 32pt debajo del cursor acumulado.
        const double SyntheticHeight = 32;
        var result = new List<BandRange>(bandKinds.Count);
        var cursor = marginTop;

        for (int i = 0; i < bandKinds.Count; i++)
        {
            if (byBand.TryGetValue(i, out var range))
            {
                result.Add(new BandRange(i, bandKinds[i], range.MinY, range.MaxBottom - range.MinY));
                cursor = range.MaxBottom;
            }
            else
            {
                result.Add(new BandRange(i, bandKinds[i], cursor, SyntheticHeight));
                cursor += SyntheticHeight;
            }
        }

        return result;
    }

    private static IReadOnlyList<InteractiveElement> ExtractInteractive(
        RenderPage page,
        IReadOnlyDictionary<string, ElementYaml> elementByPath)
    {
        // Agrupa todos los commands por SourcePath y calcula el bounding box.
        // Para text/line/rectangle hay un solo command → bbox = el mismo rect.
        // Para tablas, múltiples commands (header + cells) → bbox = unión.
        var byPath = new Dictionary<string, (double MinX, double MinY, double MaxRight, double MaxBottom, RenderCommand First)>(
            StringComparer.Ordinal);

        foreach (var cmd in page.Commands)
        {
            if (cmd.SourcePath is null) continue;
            if (byPath.TryGetValue(cmd.SourcePath, out var agg))
            {
                byPath[cmd.SourcePath] = (
                    Math.Min(agg.MinX, cmd.Bounds.X),
                    Math.Min(agg.MinY, cmd.Bounds.Y),
                    Math.Max(agg.MaxRight, cmd.Bounds.Right),
                    Math.Max(agg.MaxBottom, cmd.Bounds.Bottom),
                    agg.First);
            }
            else
            {
                byPath[cmd.SourcePath] = (cmd.Bounds.X, cmd.Bounds.Y, cmd.Bounds.Right, cmd.Bounds.Bottom, cmd);
            }
        }

        var result = new List<InteractiveElement>(byPath.Count);
        foreach (var (path, agg) in byPath)
        {
            elementByPath.TryGetValue(path, out var source);

            // Preferimos Type del YAML si está disponible (detecta 'table'); fallback al command.
            var kind = source?.Type?.ToLowerInvariant() switch
            {
                "table"     => "table",
                "text"      => "text",
                "line"      => "line",
                "rectangle" => "rectangle",
                _ => agg.First switch
                {
                    DrawTextCommand      => "text",
                    DrawLineCommand      => "line",
                    DrawRectangleCommand => "rectangle",
                    _ => "unknown"
                }
            };

            var columns = source?.Columns?.Select(c => new TableColumnView(
                Header: c.Header ?? "",
                Binding: c.Binding ?? "",
                Width: c.Width,
                Format: c.Format,
                Align: c.Align ?? "left")).ToArray();

            result.Add(new InteractiveElement(
                path, kind,
                agg.MinX, agg.MinY,
                agg.MaxRight - agg.MinX, agg.MaxBottom - agg.MinY,
                Style: source?.Style,
                Content: source?.Content,
                Color: source?.Color,
                Thickness: source?.Thickness,
                Fill: source?.Fill,
                Rows: source?.Rows,
                HeaderMode: source?.HeaderMode,
                HeaderHeight: source?.HeaderHeight,
                RowHeight: source?.RowHeight,
                HeaderStyle: source?.HeaderStyle,
                RowStyle: source?.RowStyle,
                AlternateRowStyle: source?.AlternateRowStyle,
                ColumnCount: source?.Columns?.Count,
                Columns: columns));
        }

        return result;
    }

    private async Task<string> RenderPartialToString(string viewName, object model)
    {
        ViewData.Model = model;
        await using var writer = new StringWriter();

        var engine = HttpContext.RequestServices
            .GetRequiredService<Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine>();
        var viewResult = engine.FindView(ControllerContext, viewName, isMainPage: false);
        if (!viewResult.Success)
            throw new InvalidOperationException($"View '{viewName}' no encontrada.");

        var viewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext(
            ControllerContext, viewResult.View, ViewData, TempData, writer,
            new Microsoft.AspNetCore.Mvc.ViewFeatures.HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }
}

internal static class DefaultContent
{
    public const string Yaml = """
        name: ClientesReport
        title: Reporte de clientes

        page:
          size: Letter
          orientation: Portrait
          margins:
            horizontal: 1.5cm
            vertical: 1.8cm

        culture: es-HN

        styles:
          Default:
            fontFamily: Helvetica
            fontSize: 10
            foreground: "#0F172A"
          Title:
            basedOn: Default
            fontSize: 18
            bold: true
          Caption:
            basedOn: Default
            fontSize: 8
            foreground: "#64748B"
          CaptionCenter:
            basedOn: Caption
            align: center
          TableHeader:
            basedOn: Default
            bold: true
            background: "#F1F5F9"
            padding: { horizontal: 4, vertical: 3 }
            border:
              bottom: { thickness: 0.5, color: "#CBD5E1" }
          TableRow:
            basedOn: Default
            padding: { horizontal: 4, vertical: 3 }
          TableRowAlt:
            basedOn: TableRow
            background: "#F8FAFC"

        bands:
          - kind: ReportHeader
            height: 70
            elements:
              - type: text
                bounds: { x: 0, y: 0, width: 527, height: 24 }
                content: "{{ $.titulo }}"
                style: Title
              - type: text
                bounds: { x: 0, y: 30, width: 527, height: 12 }
                content: "Generado: {{ $.generadoEn }}"
                style: Caption
              - type: line
                bounds: { x: 0, y: 50, width: 527, height: 0 }
                color: "#CBD5E1"
                thickness: 0.5

          - kind: Detail
            height: 0
            elements:
              - type: table
                bounds: { x: 0, y: 0, width: 527, height: 0 }
                rows: "$.clientes"
                headerStyle: TableHeader
                rowStyle: TableRow
                alternateRowStyle: TableRowAlt
                headerMode: RepeatOnPageBreak
                headerHeight: 22
                rowHeight: 18
                columns:
                  - header: Código
                    binding: "$.codigo"
                    width: 70
                  - header: Nombre
                    binding: "$.nombre"
                    width: 200
                  - header: Email
                    binding: "$.email"
                    width: 180
                  - header: Crédito
                    binding: "$.credito"
                    width: 77
                    format: "N2"
                    align: right

          - kind: PageFooter
            height: 24
            elements:
              - type: line
                bounds: { x: 0, y: 0, width: 527, height: 0 }
                color: "#CBD5E1"
                thickness: 0.5
              - type: text
                bounds: { x: 0, y: 8, width: 527, height: 10 }
                content: "Página {{ pageNumber }} de {{ totalPages }}"
                style: CaptionCenter
        """;

    public const string Json = """
        {
          "titulo": "Reporte de clientes activos",
          "generadoEn": "2026-04-22 15:30",
          "clientes": [
            { "codigo": "C001", "nombre": "Distribuidora Ejemplo S. de R.L.", "email": "ventas@ejemplo.hn",       "credito":  50000.00 },
            { "codigo": "C002", "nombre": "Comercial Palmira",                  "email": "contacto@palmira.hn",     "credito":  125000.50 },
            { "codigo": "C003", "nombre": "Ferretería Central",                 "email": "admin@ferrecentral.hn",   "credito":  18500.00 },
            { "codigo": "C004", "nombre": "Tecnología Moderna S.A.",            "email": "info@tecmoderna.hn",      "credito":  275000.75 },
            { "codigo": "C005", "nombre": "Suministros Industriales",           "email": "ventas@sumind.hn",        "credito":  89200.00 }
          ]
        }
        """;
}
