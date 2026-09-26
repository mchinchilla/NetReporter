# NetReporter

**Reporting engine for .NET 10 with an intermediate representation: one template produces PDF, HTML, SVG and XLSX.**

[![NuGet](https://img.shields.io/nuget/v/NetReporter?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/NetReporter)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-22c55e?style=flat-square)](https://github.com/mchinchilla/NetReporter/blob/main/LICENSE)

![NetReporter Designer: YAML editor with live SVG preview, drag/drop, zoom and PDF/HTML/XLSX export](https://raw.githubusercontent.com/mchinchilla/NetReporter/main/docs/images/designer.png)

Full documentation (English and Spanish), the YAML schema reference, thirteen sample templates and the
visual web designer live on GitHub: <https://github.com/mchinchilla/NetReporter>

**New in 1.1:** expression DSL (`{{ $.qty * $.price : N2 }}`, `if()`, `sum($.lines[*].total)`), multi-level
grouped tables with grand totals, KeepTogether on detail bands, charts (bar, line, area, pie, donut,
horizontal bar), `visible:` conditions and dynamic styles. See the GitHub README for the list of
behavior changes before upgrading production templates.

## Install

```bash
dotnet add package NetReporter   # meta-package: Core + Templates + every renderer + Barcodes
```

Or reference only the pieces you need. All packages share the same version number.

| Package | Contents |
|---|---|
| `NetReporter.Core` | Report IR (bands, elements, styles, charts), typed data bindings, layout engine, `RenderList` |
| `NetReporter.Templates` | YAML templates, JSON Path bindings (`$.client.name`), template strings (`{{ pageNumber }}`), expression DSL |
| `NetReporter.Pdf` | PDF renderer (vector, selectable text; hosted by QuestPDF) |
| `NetReporter.Svg` | SVG renderer via SkiaSharp, one document per page |
| `NetReporter.Html` | Paginated, self-contained HTML/CSS `@page` renderer |
| `NetReporter.Xlsx` | Semantic XLSX renderer via ClosedXML: text becomes cells, tables become native Excel Tables |
| `NetReporter.Barcodes` | QR / Code 128 / Code 39 / EAN-13 via ZXing.Net (opt-in) |

## How it works

`ReportDefinition` (the IR) → `LayoutEngine` → `RenderList` (absolute draw commands) → renderer.
The same layout feeds the PDF, HTML and SVG renderers, so the three outputs match pixel for pixel.
XLSX is the semantic exception: it reads the `ReportDefinition` directly so that spreadsheet cells map
to elements instead of pixels.

## Quick start (C# API)

```csharp
using NetReporter.Core.Bands;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Expressions;
using NetReporter.Core.Layout;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;
using NetReporter.Pdf;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var styles = new StyleSheetBuilder()
    .Add("Default", s => s.FontFamily("Helvetica").FontSize(12))
    .Add("Title",   s => s.BasedOn("Default").FontSize(24).Bold())
    .Build();

var page = PageSetup.Letter.WithMargins(new Thickness(Units.Cm(2)));

var report = new ReportDefinition
{
    Name = "Hello",
    Page = page,
    Styles = styles,
    Bands = new Band[]
    {
        new ReportHeaderBand
        {
            Height = 60,
            Elements = new ReportElement[]
            {
                new TextElement
                {
                    Bounds = new Rect(0, 0, page.ContentWidth, 30),
                    Content = Expr.Str("Hello NetReporter"),
                    Style = new StyleRef("Title")
                }
            }
        }
    }
};

var pdf = new PdfRenderer().Render(new LayoutEngine().Layout(report));
File.WriteAllBytes("hello.pdf", pdf);
```

## Quick start (YAML template)

```yaml
name: MyReport
title: "Report {{ $.title }}"
fileName: "report-{{ $.id }}"      # optional, resolved at export time

page:
  size: Letter                       # Letter | Legal | A3-A6 | B4-B5 | Tabloid | custom
  orientation: Portrait
  margins: { horizontal: 1.5cm, vertical: 1.8cm }

culture: en-US

styles:
  Default: { fontFamily: Helvetica, fontSize: 10, foreground: "#0F172A" }
  Title:   { basedOn: Default, fontSize: 18, bold: true }

bands:
  - kind: ReportHeader               # ReportHeader | PageHeader | Detail | PageFooter | ReportFooter
    height: 60
    elements:
      - type: text                   # text | line | rectangle | table | image | barcode
        bounds: { x: 0, y: 0, width: 500, height: 24 }
        content: "{{ $.title }}"
        style: Title
```

```csharp
using NetReporter.Core.Layout;
using NetReporter.Pdf;
using NetReporter.Templates;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var template = YamlReportLoader.Load("report.yaml");
var report   = template.Bind(File.ReadAllText("data.json"));   // JSON data bound via JSON Path

var layout = new LayoutEngine().Layout(report);
File.WriteAllBytes("report.pdf", new PdfRenderer().Render(layout));
```

## Renderers

```csharp
byte[]                pdf  = new PdfRenderer().Render(layout);
string                html = new HtmlRenderer().Render(layout, new HtmlRenderOptions { Title = "Invoice" });
IReadOnlyList<string> svgs = new SvgRenderer().Render(layout);          // one SVG per page
byte[]                xlsx = new XlsxRenderer().Render(report);         // semantic: takes the ReportDefinition
```

## Features

- Layout engine with pagination, repeated table headers and Report/Page Header/Footer + Detail bands.
- Real word wrap, auto-height bands and `keepTogether` blocks (report and detail bands) that never split across pages.
- Typed tables (`TableElement<TRow>`), multi-level grouped tables with `sum` / `count` / `avg` / `min` / `max`
  subtotals and a grand-total row, computed columns (`binding: "= $.qty * $.price"`).
- Expression DSL compiled once to delegates: arithmetic, comparisons, ternaries, 40+ text/number/date/aggregate
  functions, `visible:` conditions and dynamic styles.
- Charts: bar, horizontal bar, line, area, pie and donut, drawn as vector primitives (XLSX gets the data table).
- Embedded images (PNG/JPEG/GIF/WebP), vector barcodes and QR codes.
- Inheritable style sheet (`basedOn`) with cycle detection, per-report culture for number and date formats.
- Page sizes: Letter, Legal, A3-A6, B4-B5, Tabloid and custom sizes such as 80 mm / 58 mm receipts.
- No reflection in hot paths: bindings are typed `Func<TRow, TValue>` delegates.

## Requirements and notes

- .NET 10.
- The PDF renderer is hosted by QuestPDF; set `QuestPDF.Settings.License` before rendering
  (the Community license covers personal use and companies under 1M USD/year revenue).
- On Linux, SkiaSharp and QuestPDF need `libfontconfig1` installed on the host
  (`apt-get install libfontconfig1`). The Linux native binaries themselves ship with the packages.

## License

NetReporter is released under the MIT license. Third-party components keep their own licenses:
QuestPDF (Community / Professional), SkiaSharp (MIT), YamlDotNet (MIT), ClosedXML (MIT) and
ZXing.Net (Apache 2.0, only when `NetReporter.Barcodes` is referenced).
