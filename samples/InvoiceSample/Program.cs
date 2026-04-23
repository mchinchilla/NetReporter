using InvoiceSample.Domain;
using InvoiceSample.Reports;
using NetReporter.Core.Layout;
using NetReporter.Pdf;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var factura = new Factura(
    Numero: "000-001-01-00001234",
    Fecha: new DateOnly(2026, 4, 22),
    Cai: "A1B2C3-D4E5F6-G7H8I9-J0K1L2-M3N4O5-P6",
    RangoHasta: new DateOnly(2026, 12, 31),
    Emisor: new Empresa(
        "Mi Empresa S. de R.L.",
        "08011999123456",
        "Col. Palmira, Tegucigalpa",
        "+504 2234-5678"),
    Cliente: new Cliente(
        "Distribuidora Ejemplo",
        "05019988776655",
        "Bo. Guamilito, San Pedro Sula"),
    Lineas: new List<LineaFactura>
    {
        new("A001", "Laptop Dell Latitude 5540", 2, 28500.00m),
        new("A015", "Monitor LG 27\" UltraGear", 3, 8750.00m),
        new("B100", "Teclado mecánico Keychron K2", 5, 2150.00m),
        new("B101", "Mouse inalámbrico Logitech MX Master 3S", 5, 2850.00m),
        new("C220", "Cable HDMI 2.1 de 2 metros", 10, 395.00m),
        new("C221", "Adaptador USB-C a HDMI", 8, 675.00m),
        new("D310", "Licencia Microsoft 365 Business (anual)", 10, 4200.00m),
        new("E400", "Servicio de instalación y configuración", 20, 850.00m),
    });

var report = InvoiceReport.Build(factura);
var layout = new LayoutEngine().Layout(report);
var pdfBytes = new PdfRenderer().Render(layout);

var outputPath = Path.Combine(AppContext.BaseDirectory, "factura.pdf");
await File.WriteAllBytesAsync(outputPath, pdfBytes);

Console.WriteLine($"Factura generada: {outputPath}");
Console.WriteLine($"Páginas: {layout.Pages.Count}");
Console.WriteLine($"Comandos totales: {layout.Pages.Sum(p => p.Commands.Count)}");
