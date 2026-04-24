using Microsoft.AspNetCore.Mvc;
using NetReporter.Core.Layout;
using NetReporter.Core.RenderList;
using NetReporter.Designer.Models;
using NetReporter.Svg;
using NetReporter.Templates;
using NetReporter.Templates.Schema;

namespace NetReporter.Designer.Controllers;

public sealed class HomeController : Controller
{
    private readonly SvgRenderer _svg = new();

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
            Color = request.Color, Thickness = request.Thickness, Fill = request.Fill
        };
        return await ApplyChange(
            request.Json,
            () => YamlReportRewriter.ApplyPatch(
                request.Yaml ?? string.Empty,
                request.Path ?? string.Empty,
                patch));
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
        // Tomamos el primer comando por SourcePath. Para text/line/rectangle hay uno solo.
        // Filtramos tabla (por ahora no movible).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<InteractiveElement>();

        foreach (var cmd in page.Commands)
        {
            if (cmd.SourcePath is null) continue;
            if (!seen.Add(cmd.SourcePath)) continue;

            var kind = cmd switch
            {
                DrawTextCommand      => "text",
                DrawLineCommand      => "line",
                DrawRectangleCommand => "rectangle",
                _ => "unknown"
            };

            elementByPath.TryGetValue(cmd.SourcePath, out var source);

            result.Add(new InteractiveElement(
                cmd.SourcePath, kind,
                cmd.Bounds.X, cmd.Bounds.Y,
                cmd.Bounds.Width, cmd.Bounds.Height,
                Style: source?.Style,
                Content: source?.Content,
                Color: source?.Color,
                Thickness: source?.Thickness,
                Fill: source?.Fill));
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
