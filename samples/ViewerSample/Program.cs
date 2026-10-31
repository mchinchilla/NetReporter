// Minimal "ERP" showing NetReporter.Viewer:
//   /                         list of reports
//   /reports/{id}             full-page viewer (served by the package)
//   /embedded/{id}            the <netreporter-viewer> component inside the app's own layout
using System.Net;
using NetReporter.Core.Definition;
using NetReporter.Templates;
using NetReporter.Viewer;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// id → (template, data). In a real ERP the data comes from the database, filtered by query parameters.
var dir = Path.Combine(AppContext.BaseDirectory, "Reports");
var reports = new Dictionary<string, (string Yaml, string Json)>(StringComparer.OrdinalIgnoreCase)
{
    ["clientes"] = ("report.yaml", "data.json"),
    ["factura"] = ("invoice-complete.yaml", "invoice-complete-data.json"),
    ["factura-pastel"] = ("invoice-complete-pastel.yaml", "invoice-complete-data.json"),
    ["ticket"] = ("invoice-paperoll.yaml", "invoice-data.json"),
    ["dashboard"] = ("sales-dashboard.yaml", "sales-dashboard-data.json"),
    ["grupos"] = ("nested-groups.yaml", "nested-groups-data.json"),
    ["estado-cuenta"] = ("account-statement.yaml", "account-statement-data.json"),
    ["contratos"] = ("contracts-keep-together.yaml", "contracts-data.json"),
    ["codigos"] = ("barcodes-demo.yaml", "barcodes-demo-data.json"),
};

app.MapNetReporterViewer("/reports", (request, _) =>
{
    if (!reports.TryGetValue(request.ReportId, out var r)) return ValueTask.FromResult<ReportDefinition?>(null);
    var template = YamlReportLoader.Parse(File.ReadAllText(Path.Combine(dir, r.Yaml)));
    return ValueTask.FromResult<ReportDefinition?>(template.Bind(File.ReadAllText(Path.Combine(dir, r.Json))));
}, options => options.IncludeErrorDetails = app.Environment.IsDevelopment());

app.MapGet("/", () => Results.Content(Page("NetReporter Viewer — demo", $"""
    <main class="list">
      <h1>Reportes</h1>
      <table>
        {string.Concat(reports.Keys.Select(id => $"""
        <tr><td>{id}</td>
            <td><a href="/reports/{id}">Página completa</a></td>
            <td><a href="/embedded/{id}">Embebido</a></td></tr>
        """))}
      </table>
    </main>
    """), "text/html; charset=utf-8"));

app.MapGet("/embedded/{id}", (string id) => Results.Content(Page($"{id} — ERP", $"""
    <header class="erp"><a href="/">← Volver</a><strong>Mi ERP</strong><span>Reporte: {WebUtility.HtmlEncode(id)}</span></header>
    <netreporter-viewer src="/reports/{Uri.EscapeDataString(id)}/open" lang="es"></netreporter-viewer>
    <script type="module" src="/reports/_viewer/viewer.js"></script>
    """), "text/html; charset=utf-8"));

app.Run();

static string Page(string title, string body) => $$"""
    <!doctype html>
    <html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
    <title>{{WebUtility.HtmlEncode(title)}}</title>
    <style>
      body { margin: 0; font-family: system-ui, sans-serif; height: 100vh; display: flex; flex-direction: column; }
      .list { padding: 24px; } td { padding: 4px 12px 4px 0; }
      .erp { display: flex; gap: 16px; align-items: center; padding: 10px 16px; background: #0f172a; color: #fff; }
      .erp a { color: #93c5fd; }
      netreporter-viewer { flex: 1; min-height: 0; }
    </style></head>
    <body>{{body}}</body></html>
    """;
