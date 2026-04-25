using NetReporter.Barcodes;
using NetReporter.Core.Layout;
using NetReporter.Pdf;
using NetReporter.Svg;
using NetReporter.Templates;
using NetReporter.Xlsx;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var baseDir = AppContext.BaseDirectory;
// Paths relativos en YAML (ej. logo.png) se resuelven contra el cwd; lo apuntamos
// al directorio donde están los assets copiados por CopyToOutputDirectory.
Directory.SetCurrentDirectory(baseDir);

// Usar SkiaTextMeasurer para medición precisa de texto (mismo motor que el renderer).
// Y ZXingBarcodeGenerator para QR/Code128 vectoriales.
var measurer = SkiaTextMeasurer.Instance;
var barcodeGen = ZXingBarcodeGenerator.Instance;

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

// === Demo de grupos + subtotales ===
RenderToPdf(
    Path.Combine(baseDir, "grouped-invoice.yaml"),
    Path.Combine(baseDir, "grouped-invoice-data.json"),
    "grouped-invoice.pdf");

// === Demo con logo PNG + QR del CAI (Fase 4) ===
RenderToPdf(
    Path.Combine(baseDir, "invoice-with-qr.yaml"),
    Path.Combine(baseDir, "invoice-data.json"),    // reusa data de la factura simple
    "invoice-with-qr.pdf");

// === Demo de los 4 formatos de barcode ===
RenderToPdf(
    Path.Combine(baseDir, "barcodes-demo.yaml"),
    Path.Combine(baseDir, "barcodes-demo-data.json"),
    "barcodes-demo.pdf");

// === Demo de KeepTogether ===
RenderToPdf(
    Path.Combine(baseDir, "keep-together-demo.yaml"),
    Path.Combine(baseDir, "keep-together-data.json"),
    "keep-together-demo.pdf");

// === Flagship: factura completa con TODAS las features de Fase 1-4 ===
RenderToPdf(
    Path.Combine(baseDir, "invoice-complete.yaml"),
    Path.Combine(baseDir, "invoice-complete-data.json"),
    "invoice-complete.pdf");


void RenderToPdf(string templatePath, string dataPath, string outputName)
{
    var template = YamlReportLoader.Load(templatePath);
    var json = File.ReadAllText(dataPath);
    var report = template.Bind(json);
    var layout = new LayoutEngine(measurer, barcodeGen).Layout(report);
    var pdfBytes = new PdfRenderer().Render(layout);

    var outputPath = Path.Combine(baseDir, outputName);
    File.WriteAllBytes(outputPath, pdfBytes);

    // Paralelo: emitir XLSX semántico desde el mismo IR. No usa LayoutEngine.
    var xlsxBytes = new XlsxRenderer().Render(report);
    var xlsxPath = Path.ChangeExtension(outputPath, ".xlsx");
    File.WriteAllBytes(xlsxPath, xlsxBytes);

    Console.WriteLine($"{outputName,-28}  {layout.Pages.Count} pag  ({layout.Pages.Sum(p => p.Commands.Count)} cmds)  +xlsx {xlsxBytes.Length,7} b");
}
