using System.Globalization;
using NetReporter.Templates;
using NetReporter.Viewer.Desktop;
using NetReporter.Viewer.WinForms;

namespace WinFormsViewerSample;

/// <summary>
/// Minimal host for <see cref="ReportViewer"/>: pick one of the TemplateSample templates (bound to its
/// sample data) and switch the viewer's language. Run on Windows: dotnet run --project samples/WinFormsViewerSample
/// </summary>
internal static class Program
{
    // Template → data file (the same pairs samples/TemplateSample renders).
    private static readonly (string Template, string Data)[] Reports =
    [
        ("invoice-complete", "invoice-complete-data"),
        ("invoice-laser", "invoice-data"),
        ("invoice-with-qr", "invoice-data"),
        ("invoice-paperoll", "invoice-data"),
        ("ticket-complete", "invoice-data"),
        ("grouped-invoice", "grouped-invoice-data"),
        ("nested-groups", "nested-groups-data"),
        ("account-statement", "account-statement-data"),
        ("sales-dashboard", "sales-dashboard-data"),
        ("barcodes-demo", "barcodes-demo-data"),
        ("orphan-control", "orphan-control-data"),
        ("keep-together-demo", "keep-together-data"),
        ("contracts-keep-together", "contracts-data"),
        ("wrap-demo", "wrap-data"),
        ("report", "data"),
    ];

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();      // PerMonitorV2 (ApplicationHighDpiMode in the csproj)

        var templates = Path.Combine(AppContext.BaseDirectory, "Templates");
        // Relative paths in the YAML (logo.png) resolve against the current directory.
        Directory.SetCurrentDirectory(templates);

        using var form = new Form { Text = "NetReporter — WinForms viewer", Width = 1100, Height = 850 };
        var viewer = new ReportViewer { Dock = DockStyle.Fill, ZoomMode = ZoomMode.PageWidth };

        var reportBox = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 220 };
        foreach (var (template, _) in Reports) reportBox.Items.Add(template);
        var languageBox = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 90 };
        languageBox.Items.AddRange(["Español", "English"]);
        var status = new ToolStripLabel { Alignment = ToolStripItemAlignment.Right };

        var top = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        top.Items.AddRange([new ToolStripLabel("Report:"), reportBox, new ToolStripSeparator(),
            new ToolStripLabel("Language:"), languageBox, status]);

        reportBox.SelectedIndexChanged += async (_, _) =>
        {
            var (template, data) = Reports[reportBox.SelectedIndex];
            try
            {
                var report = YamlReportLoader.Load(Path.Combine(templates, template + ".yaml"))
                    .Bind(File.ReadAllText(Path.Combine(templates, data + ".json")));
                var started = Environment.TickCount64;
                await viewer.LoadAsync(report);
                status.Text = viewer.Document is { } doc
                    ? $"{doc.PageCount} page(s) · {doc.PageSize.Width:0} × {doc.PageSize.Height:0} pt · layout {Environment.TickCount64 - started} ms"
                    : "";
            }
            catch (Exception ex)
            {
                // The viewer already shows the error; keep the sample alive.
                status.Text = ex.GetType().Name;
            }
        };
        languageBox.SelectedIndexChanged += (_, _) =>
            viewer.UICulture = CultureInfo.GetCultureInfo(languageBox.SelectedIndex == 0 ? "es" : "en");

        form.Controls.Add(viewer);
        form.Controls.Add(top);
        form.Shown += (_, _) =>
        {
            languageBox.SelectedIndex = 0;
            reportBox.SelectedIndex = 0;
        };
        Application.Run(form);
    }
}
