using NetReporter.Core.Layout;
using NetReporter.Pdf;
using NetReporter.Templates;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Rutas relativas al directorio de salida (CopyToOutputDirectory en csproj).
var baseDir = AppContext.BaseDirectory;
var templatePath = Path.Combine(baseDir, "report.yaml");
var dataPath = Path.Combine(baseDir, "data.json");

// 1) Cargar template YAML.
var template = YamlReportLoader.Load(templatePath);

// 2) Enlazar con datos JSON.
var json = File.ReadAllText(dataPath);
var report = template.Bind(json);

// 3) Layout + render PDF.
var layout = new LayoutEngine().Layout(report);
var pdfBytes = new PdfRenderer().Render(layout);

var outputPath = Path.Combine(baseDir, "clientes.pdf");
await File.WriteAllBytesAsync(outputPath, pdfBytes);

Console.WriteLine($"Reporte generado: {outputPath}");
Console.WriteLine($"Páginas: {layout.Pages.Count}");
Console.WriteLine($"Comandos totales: {layout.Pages.Sum(p => p.Commands.Count)}");
