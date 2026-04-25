using NetReporter.Core.Layout;
using NetReporter.Pdf;
using NetReporter.Svg;
using NetReporter.Templates;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var baseDir = AppContext.BaseDirectory;

// Usar SkiaTextMeasurer para medición precisa de texto (mismo motor que el renderer).
// Sin él, el LayoutEngine usa estimación lineal aproximada.
var measurer = SkiaTextMeasurer.Instance;

// === Demo original: reporte de clientes ===
RenderToPdf(
    Path.Combine(baseDir, "report.yaml"),
    Path.Combine(baseDir, "data.json"),
    "clientes.pdf");

// === Factura laser (Letter) ===
RenderToPdf(
    Path.Combine(baseDir, "invoice-laser.yaml"),
    Path.Combine(baseDir, "invoice-data.json"),
    "invoice-laser.pdf");

// === Factura paperoll (80mm) ===
RenderToPdf(
    Path.Combine(baseDir, "invoice-paperoll.yaml"),
    Path.Combine(baseDir, "invoice-data.json"),
    "invoice-paperoll.pdf");

// === Demo de word wrap + auto-height ===
RenderToPdf(
    Path.Combine(baseDir, "wrap-demo.yaml"),
    Path.Combine(baseDir, "wrap-data.json"),
    "wrap-demo.pdf");


void RenderToPdf(string templatePath, string dataPath, string outputName)
{
    var template = YamlReportLoader.Load(templatePath);
    var json = File.ReadAllText(dataPath);
    var report = template.Bind(json);
    var layout = new LayoutEngine(measurer).Layout(report);
    var pdfBytes = new PdfRenderer().Render(layout);

    var outputPath = Path.Combine(baseDir, outputName);
    File.WriteAllBytes(outputPath, pdfBytes);

    Console.WriteLine($"{outputName,-28}  {layout.Pages.Count} pag  ({layout.Pages.Sum(p => p.Commands.Count)} cmds)");
}
