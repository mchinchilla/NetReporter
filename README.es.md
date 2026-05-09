<div align="center">

[🇺🇸 English](README.md) · **🇪🇸 Español**

# 🧾 NetReporter

**Motor de reportes para .NET 10 con IR — un mismo template produce PDF, HTML, SVG y XLSX.**

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Tests](https://img.shields.io/badge/tests-184%20passing-22c55e?style=flat-square&logo=xunit)](#-tests)
[![License](https://img.shields.io/badge/license-TBD-lightgrey?style=flat-square)](#-licencias)
[![Status](https://img.shields.io/badge/status-prototipo%20funcional-f59e0b?style=flat-square)](#estado)

🎨 Designer visual · 📄 PDF · 🌐 HTML · 🖼️ SVG · 📊 XLSX · 📦 Templates YAML

</div>

---

## 📚 Tabla de contenidos

1. [Filosofía y arquitectura](#-filosofía-y-arquitectura)
2. [Características](#-características)
3. [Pipeline visual](#-pipeline-visual)
4. [Stack tecnológico](#-stack-tecnológico)
5. [Estructura del repo](#-estructura-del-repo)
6. [Quick start](#-quick-start)
7. [Designer visual](#-designer-visual)
8. [Sintaxis del template YAML](#-sintaxis-del-template-yaml)
9. [Renderers disponibles](#-renderers-disponibles)
10. [Tamaños de papel](#-tamaños-de-papel)
11. [Tests](#-tests)
12. [Limitaciones conocidas](#-limitaciones-conocidas)
13. [Roadmap](#-roadmap)
14. [Licencias](#-licencias)

---

## 💡 Filosofía y arquitectura

NetReporter separa el reporte en cuatro capas con interfaces claras. **El template no sabe del formato de salida**: se describe una vez, se renderiza N veces.

```
┌────────────────────────────────────┐
│  📝 Template (YAML / API C#)       │  Definición declarativa
└────────────────────────────────────┘
                ▼
┌────────────────────────────────────┐
│  🧠 IR (ReportDefinition)          │  Modelo en memoria
└────────────────────────────────────┘
                ▼
┌────────────────────────────────────┐
│  📐 LayoutEngine                   │  Measure + arrange + paginación
└────────────────────────────────────┘
                ▼
┌────────────────────────────────────┐
│  📋 RenderList                     │  Comandos de dibujo absolutos
│  · DrawTextCommand                 │  (un solo IR para todos los outputs)
│  · DrawLineCommand                 │
│  · DrawRectangleCommand            │
└────────────────────────────────────┘
                ▼
   ┌───────────┬───────────┬──────────┐
   ▼           ▼           ▼           ▼
 ┌─────┐   ┌──────┐   ┌──────┐   ┌─────────┐
 │ 📄  │   │  🌐  │   │  🖼️  │   │  📊     │
 │ PDF │   │ HTML │   │ SVG  │   │ XLSX *  │
 └─────┘   └──────┘   └──────┘   └─────────┘

 * XLSX es semántico: consume ReportDefinition directo (texto → celdas,
   tablas → rangos Excel reales con filtros/sort), saltándose el RenderList.
```

**Principios no negociables:**

| Principio | Implementación |
|---|---|
| 🚫 **Sin reflection** en hot paths | Data binding con `Func<TRow,TValue>` tipados; expresiones con `Func<IEvaluationContext,T>` |
| 🎨 **Sin CSS / Tailwind para reportes** | Styling nativo con `StyleSheet` + `StyleRef` (struct zero-alloc) y herencia con `BasedOn` |
| 🧪 **QuestPDF solo como contenedor** | Todo el dibujo PDF pasa por SkiaSharp → SVG → `container.Svg(...)` |
| 🔄 **Mismo IR, múltiples outputs** | El `RenderList` se calcula una vez y N renderers lo consumen |
| 📦 **Templates editables sin recompilar** | YAML + JSON enlazados con JSON Path |

---

## ✨ Características

### Engine
- ⚡ **Pipeline IR** — un template, múltiples salidas
- 📐 **Layout engine** con paginación, repetición de headers de tabla, bandas (Report/Page Header/Footer + Detail)
- 📝 **Word wrap real** + auto-height — el texto se rompe naturalmente, las bandas crecen para acomodar el contenido
- 🔒 **KeepTogether** — bloques atómicos no se parten entre páginas
- 📊 **Tablas agrupadas con subtotales** — `groupBy` + group headers/footers con `Sum`/`Count`/`Avg`
- 🖼️ **Imágenes embebidas** (PNG/JPEG/GIF/WebP) — source por path o data URI
- ▦ **Barcodes y QR vectoriales** — QR / Code 128 / Code 39 / EAN-13 (vía ZXing, opt-in)
- 🎨 **StyleSheet con herencia** — `BasedOn` chain con detección de ciclos y caché
- 🔢 **Cultura por reporte** — formato de números/fechas localizado (probado con `es-HN`)
- 📋 **Tablas tipadas** — `TableElement<TRow>` con columnas tipadas, alineación y formato por columna

### Templates YAML
- 📝 **Schema declarativo** — page, styles, bands, elements
- 🔗 **JSON Path bindings** — `$.cliente.nombre`, `$.lineas[*]`
- 🪝 **Template strings** — `{{ pageNumber }}`, `{{ totalPages }}`, `{{ $.titulo }}`, `{{ #group }}`, `{{ #count }}`
- 📁 **FileName con templates** — `fileName: "factura-{{ $.numero }}"` resuelto al exportar
- 🎯 **Multi-formato** — Letter, Legal, A3-A6, B4-B5, Tabloid, custom (paperoll 80mm/58mm)

### Designer visual web
- 🖱️ **Drag-and-drop** sobre el preview SVG (mover y crear elementos)
- 🎯 **Resize handles** (8 direcciones) con snap a 5pt con Shift
- ⌨️ **Keyboard nudge** — flechas mueven 1pt, Shift+flechas 10pt
- 🔍 **Zoom in/out** — 25%–400% por pasos, ajustar al ancho, persistido entre recargas
- 📋 **CRUD completo** sobre elementos, bandas y columnas de tabla
- 💾 **Save/Load** templates a `~/.netreporter/templates/`
- 📦 **Samples builtin** + **importar archivo** desde disco
- ↶ **Undo/Redo** (50 niveles, Ctrl+Z / Ctrl+Shift+Z)
- 📥 **Export a PDF, HTML y XLSX** con un click

### Renderers
- 📄 **PDF** vía QuestPDF + SkiaSharp (Letter, A4, Legal, custom)
- 🌐 **HTML** paginado con `@page` CSS — imprimible nativo
- 🖼️ **SVG** vectorial — uno por página
- 📊 **XLSX** semántico vía ClosedXML — texto → celdas, tablas → Excel Tables nativas (filtros, sort, banded rows)

---

## 🔄 Pipeline visual

### Flujo de uso end-to-end

```mermaid
flowchart LR
    A[📝 Editar template<br/>en designer o IDE] --> B[💾 report.yaml<br/>+ data.json]
    B --> C[YamlReportLoader.Bind]
    C --> D[ReportDefinition<br/>IR]
    D --> E[LayoutEngine]
    E --> F[RenderList<br/>comandos absolutos]
    F --> G1[PdfRenderer]
    F --> G2[HtmlRenderer]
    F --> G3[SvgRenderer]
    D --> G4[XlsxRenderer<br/>semántico, sin Layout]
    G1 --> H1[📄 factura.pdf]
    G2 --> H2[🌐 factura.html]
    G3 --> H3[🖼️ pages.svg]
    G4 --> H4[📊 factura.xlsx]
```

### Flujo de edición en el designer

```mermaid
flowchart TB
    A[Click en preview] -->|selecciona| B[Drawer<br/>Propiedades]
    A -->|drag| C[Mover elemento]
    A -->|resize handle| D[Cambiar bounds]
    A -->|Del| E[Eliminar]
    A -->|Ctrl+D| F[Duplicar]

    G[Toolbox] -->|drag a banda| H[Crear elemento]

    B -->|edit campo| I[POST /Home/Update]
    C -->|drop| J[POST /Home/Move]
    D -->|drop| I
    E --> K[POST /Home/Delete]
    F --> L[POST /Home/Duplicate]
    H --> M[POST /Home/Add]

    I --> N[YamlReportRewriter]
    J --> N
    K --> N
    L --> N
    M --> N
    N --> O[YAML actualizado]
    O --> P[LayoutEngine + SvgRenderer]
    P --> Q[Preview re-render]
```

---

## 🛠️ Stack tecnológico

| Capa | Tecnología | Notas |
|---|---|---|
| Runtime | **.NET 10** | `LangVersion: latest`, `Nullable: enable`, `TreatWarningsAsErrors: true` |
| Layout & IR | C# puro | Sin dependencias externas |
| YAML parser | **YamlDotNet** ≥ 17.0 | CamelCase naming, ignore unmatched, default values handling |
| PDF backend | **QuestPDF** ≥ 2026.2 | Solo como contenedor; el dibujo va por SkiaSharp |
| 2D drawing | **SkiaSharp** ≥ 3.119 | `SKSvgCanvas` para emitir SVG |
| Web UI | **ASP.NET Core MVC** (.NET 10) | Designer interactivo |
| Frontend interactivo | **Alpine.js** 3.14 (CDN) | State + reactivity sin build pipeline |
| Network reactividad | **HTMX** 2.0 (CDN) | Live preview con debounce |
| CSS | **Tailwind CSS** (CDN play) | Utility-first |
| Barcodes (opt-in) | **ZXing.Net** 0.16 | Apache 2.0 · QR / Code128 / Code39 / EAN-13 |
| XLSX backend | **ClosedXML** 0.105 | MIT · OpenXML, Excel Tables nativas |
| Tests | **xUnit** 2.9 | 184 tests en 5 proyectos |
| JSON | `System.Text.Json` (BCL) | Sin Newtonsoft |

---

## 📁 Estructura del repo

```
NetReporter/
├── 📦 src/
│   ├── NetReporter.Core/        🧠 IR, Primitives, Styles, Layout, RenderList
│   ├── NetReporter.Templates/   📝 YAML loader/rewriter, JSON Path, template strings
│   ├── NetReporter.Pdf/         📄 PdfRenderer (delega a Svg)
│   ├── NetReporter.Svg/         🖼️ SvgRenderer (SkiaSharp.SKSvgCanvas)
│   ├── NetReporter.Html/        🌐 HtmlRenderer (HTML/CSS paginado)
│   ├── NetReporter.Xlsx/        📊 XlsxRenderer semántico (ClosedXML, Excel Tables nativas)
│   └── NetReporter.Barcodes/    ▦ Barcode/QR generator vía ZXing (opt-in)
│
├── 🧪 tests/
│   ├── NetReporter.Core.Tests/        ColorTests, PageSetupTests, StyleSheetTests, BorderSetTests, EstimateTextMeasurer, AutoHeight, KeepTogether, GroupedTable
│   ├── NetReporter.Templates.Tests/   JsonPathTests, TemplateStringTests, YamlReportRewriterTests, FileNameTests
│   ├── NetReporter.Html.Tests/        HtmlRendererTests
│   ├── NetReporter.Xlsx.Tests/        XlsxRendererTests
│   └── NetReporter.Barcodes.Tests/    ZXingBarcodeGeneratorTests
│
├── 🛠️ tools/
│   └── NetReporter.Designer/    🎨 ASP.NET Core MVC + Alpine + HTMX + Tailwind
│
└── 🎯 samples/
    ├── InvoiceSample/           Factura con API C# (sin YAML)
    └── TemplateSample/          Demo con YAML (9 templates)
        ├── report.yaml              Reporte de clientes
        ├── invoice-laser.yaml       📄 Factura Letter (laser)
        ├── invoice-paperoll.yaml    🧾 Factura 80mm (POS térmico)
        ├── wrap-demo.yaml           Word-wrap + auto-height
        ├── grouped-invoice.yaml     groupBy + subtotales sum/count/avg
        ├── invoice-with-qr.yaml     Logo + QR del CAI
        ├── barcodes-demo.yaml       Los 4 formatos de barcode
        ├── keep-together-demo.yaml  Demo de KeepTogether con page-break
        └── invoice-complete.yaml    🚀 Flagship — todas las features Fase 1-4
```

---

## 🚀 Quick start

### Requisitos

- ✅ .NET 10 SDK (probado con `10.0.103`)
- ✅ macOS / Linux / Windows
- ⚠️ Licencia QuestPDF Community para uso personal/empresas <1M USD/año

### Setup

```bash
git clone <este-repo>
cd NetReporter
dotnet build
```

### Render desde código (API C#)

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
    Name = "Hola",
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
                    Content = Expr.Str("Hola NetReporter"),
                    Style = new StyleRef("Title")
                }
            }
        }
    }
};

var pdf = new PdfRenderer().Render(new LayoutEngine().Layout(report));
File.WriteAllBytes("hola.pdf", pdf);
```

### Render desde template YAML

```csharp
using NetReporter.Core.Layout;
using NetReporter.Pdf;
using NetReporter.Templates;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var template = YamlReportLoader.Load("invoice-laser.yaml");
var json = File.ReadAllText("invoice-data.json");
var report = template.Bind(json);

var layout = new LayoutEngine().Layout(report);
File.WriteAllBytes("factura.pdf", new PdfRenderer().Render(layout));
File.WriteAllText("factura.html", new NetReporter.Html.HtmlRenderer().Render(layout));

// XLSX es semántico: consume el ReportDefinition directo (no el layout)
File.WriteAllBytes("factura.xlsx", new NetReporter.Xlsx.XlsxRenderer().Render(report));
```

### Correr los samples

```bash
# Sample con C# imperative
dotnet run --project samples/InvoiceSample
# → samples/InvoiceSample/bin/Debug/net10.0/factura.pdf

# Sample con YAML — emite PDF + XLSX por cada template (9 reportes × 2 outputs)
dotnet run --project samples/TemplateSample
# → samples/TemplateSample/bin/Debug/net10.0/*.pdf y *.xlsx
```

---

## 🎨 Designer visual

### Lanzar

```bash
dotnet run --project tools/NetReporter.Designer
```

Abre `http://localhost:5296`.

### Layout

```
┌───────────────────────────────────────────────────────────────────────────────┐
│ 🅽 Designer  [Template ▼ ●] [↥ Importar] [Save][Save as…][Delete]            │
│              [↶ Undo (n)] [↷ Redo]   [⭳ PDF] [⭳ HTML] [⭳ XLSX]               │
├──────────────────────────┬──────────┬────────────────────────────────────────┤
│ report.yaml | data.json  │ Toolbox  │ Preview                                │
│                          │          │                                        │
│ name: ClientesReport     │ [T]      │  ┌──────────────────┐                  │
│ title: Reporte clientes  │ Text     │  │ Reporte clientes │  ┌─────────────┐ │
│ page:                    │          │  │                  │  │Propiedades  │ │
│   size: Letter           │ [━]      │  │ Código  Nombre   │  │bands.0.el.0 │ │
│   margins: { all: 2cm }  │ Line     │  │ ────────────     │  │TEXT         │ │
│ ...                      │          │  │ C001    ...      │  │X     Y      │ │
│ bands:                   │ [▭]      │  │ C002    ...      │  │ 50    20    │ │
│   - kind: ReportHeader   │ Rect     │  │ C003    ...      │  │             │ │
│     ...                  │          │  └──────────────────┘  │ Width Height│ │
│                          │ [⊞]      │                        │  120   14   │ │
│                          │ Table    │                        │             │ │
│                          │          │                        │ Style: Title│ │
│                          │ BANDAS   │                        │ Content: ...│ │
│                          │ R... ▲▼✕ │                        └─────────────┘ │
│                          │ D... ▲▼✕ │                                        │
│                          │ P... ▲▼✕ │                                        │
└──────────────────────────┴──────────┴────────────────────────────────────────┘
```

### Atajos de teclado

| Acción | Shortcut |
|---|---|
| Mover elemento | Click + drag |
| Mover con snap | Shift + drag |
| Redimensionar | Drag de handle (8 direcciones) |
| Nudge 1pt | `← ↑ → ↓` |
| Nudge 10pt | `Shift + ← ↑ → ↓` |
| Eliminar | `Del` o `Backspace` |
| Duplicar | `Ctrl+D` / `Cmd+D` |
| Undo | `Ctrl+Z` / `Cmd+Z` |
| Redo | `Ctrl+Shift+Z` / `Ctrl+Y` |
| Zoom in | `Ctrl/Cmd + +` (o `=`) |
| Zoom out | `Ctrl/Cmd + -` |
| Zoom 100% | `Ctrl/Cmd + 0` |

### Samples builtin (dropdown 📦 Samples)

| Sample | Demuestra |
|---|---|
| `clientes` | Reporte tabular básico con header, tabla, page footer |
| `invoice-laser` | Factura completa Letter con bloque de totales |
| `invoice-paperoll` | Recibo continuo 80mm (impresora térmica POS) |
| `wrap-demo` | Word wrap real + auto-height de bandas |
| `grouped-invoice` | `groupBy` con headers por grupo y subtotales `Sum`/`Count`/`Avg` |
| `invoice-with-qr` | Logo PNG embebido + QR vectorial del CAI |
| `barcodes-demo` | Los 4 formatos de barcode soportados (QR / Code128 / Code39 / EAN-13) en una sola página |
| `keep-together` | `keepTogether` forzando page break limpio en un bloque "Términos & firmas" |
| `invoice-complete` | 🚀 **Flagship** — todas las features Fase 1-4 combinadas: logo, word-wrap, autoHeight, tabla agrupada, QR, barcode, KeepTogether, cultura es-HN, fileName con template |

### Flujo típico

1. **Abrir un sample** → `📦 Samples / invoice-with-qr`.
2. **Editar** datos de prueba en el tab `data.json`.
3. **Drag** un texto, **resize** una tabla, **agregar columnas** desde el drawer.
4. **Save as…** con un nombre propio → se guarda en `~/.netreporter/templates/`.
5. **⭳ PDF**, **⭳ HTML** o **⭳ XLSX** → descarga con el `fileName` del template (e.g. `factura-000-001.pdf`).

### Importar archivos del disco

`↥ Importar` abre file picker. Acepta `.yaml`, `.yml`, `.json`. Selección múltiple (yaml + json a la vez). Detecta automáticamente cuál es cuál por extensión.

### Controles de zoom

La barra del preview tiene botones `−` / `+` / `1:1` / `↔`. Los pasos son `25 · 50 · 75 · 100 · 125 · 150 · 200 · 300 · 400 %`. El botón `↔` ajusta el ancho de página al área disponible del preview. El nivel elegido se persiste en `localStorage`, así que recargar el designer lo mantiene. Las coordenadas de drag/resize se compensan por el zoom actual — mover un elemento al 200% sigue moviéndolo pt-a-pt en el YAML.

> Nota de implementación: el preview se escala con `transform: scale(var(--zoom))`, no con la propiedad CSS `zoom`. `zoom` mete el contenido escalado en el cálculo de layout de flexbox, haciendo que el panel del preview invada el editor YAML / toolbox. Los flex items del chain del preview llevan `min-w-0` para que el `overflow-auto` del contenedor sí recorte el contenido escalado en vez de dejarlo crecer al panel.

---

## 📝 Sintaxis del template YAML

### Esqueleto mínimo

```yaml
name: MiReporte
title: "Reporte {{ $.titulo }}"
fileName: "reporte-{{ $.id }}"      # opcional, soporta template strings

page:
  size: Letter                       # Letter | Legal | A3-A6 | B4-B5 | Tabloid | custom
  orientation: Portrait              # Portrait | Landscape
  margins:
    horizontal: 1.5cm
    vertical: 1.8cm

culture: es-HN                       # cultura para formato de números/fechas

styles:
  Default:
    fontFamily: Helvetica
    fontSize: 10
    foreground: "#0F172A"
  Title:
    basedOn: Default
    fontSize: 18
    bold: true

bands:
  - kind: ReportHeader               # ReportHeader | PageHeader | Detail | PageFooter | ReportFooter
    height: 60
    elements:
      - type: text                   # text | line | rectangle | table
        bounds: { x: 0, y: 0, width: 500, height: 24 }
        content: "{{ $.titulo }}"
        style: Title
```

### Elementos disponibles

| Tipo | Campos clave |
|---|---|
| `text` | `content` (template), `style`, `wordWrap`, `autoHeight` |
| `line` | `orientation` (horizontal/vertical), `color`, `thickness` |
| `rectangle` | `fill`, `borderLine: { thickness, color }` |
| `table` | `rows` (JSON Path), `columns[]`, `headerStyle`, `rowStyle`, `alternateRowStyle`, `headerHeight`, `rowHeight`, `headerMode`, `groupBy`, `groupHeader`, `groupFooter` |
| `image` | `source` (path local o `data:image/png;base64,...`), `fit` (contain/fill) |
| `barcode` | `value` (template), `format` (qr/code128/code39/ean13), `barcodeForeground`, `barcodeBackground` |

### Propiedades de banda

| Propiedad | Efecto |
|---|---|
| `height` | Altura mínima reservada (pt) |
| `autoHeight: true` | La banda crece para acomodar su contenido (max con `height`) |
| `keepTogether: true` | Si la banda no cabe en la página actual, page break antes de emitirla |

### Tabla con columnas

```yaml
- type: table
  bounds: { x: 0, y: 0, width: 527, height: 0 }
  rows: "$.lineas"
  headerStyle: TableHeader
  rowStyle: TableRow
  alternateRowStyle: TableRowAlt
  headerMode: RepeatOnPageBreak       # PrintOnce | RepeatOnPageBreak
  headerHeight: 22
  rowHeight: 18
  columns:
    - { header: "Código",      binding: "$.codigo",      width: 70,  align: left }
    - { header: "Descripción", binding: "$.descripcion", width: 257, align: left }
    - { header: "Cant.",       binding: "$.cantidad",    width: 50,  align: right }
    - { header: "Precio",      binding: "$.precio",      width: 75,  format: "N2", align: right }
    - { header: "Total",       binding: "$.total",       width: 75,  format: "N2", align: right }
```

### Imágenes y barcodes

```yaml
# Imagen embebida — path local o data URI inline
- type: image
  bounds: { x: 0, y: 0, width: 120, height: 60 }
  source: "logo.png"                            # o: data:image/png;base64,...
  fit: contain                                  # contain | fill

# QR vectorial (también: code128, code39, ean13)
- type: barcode
  bounds: { x: 0, y: 0, width: 90, height: 90 }
  value: "{{ $.cai }}"                          # template string evaluado
  format: qr
  barcodeForeground: "#0F172A"
  barcodeBackground: "#FFFFFF"
```

Los barcodes se emiten como N `DrawRectangleCommand`s (uno por módulo oscuro) — totalmente **vectoriales**, escalan perfecto en PDF/SVG/HTML a cualquier zoom. Requiere referencia a `NetReporter.Barcodes` y pasar `ZXingBarcodeGenerator.Instance` al constructor del `LayoutEngine`:

```csharp
var layout = new LayoutEngine(SkiaTextMeasurer.Instance, ZXingBarcodeGenerator.Instance)
                 .Layout(report);
```

El Designer ya lo cablea — barcodes funcionan out-of-the-box en preview/export.

### Tablas agrupadas (subtotales)

Agrupa filas consecutivas con la misma clave, emite un header por grupo y un footer con agregados:

```yaml
- type: table
  rows: "$.lineas"
  groupBy: "$.categoria"           # agrupa filas consecutivas por esta expresión

  groupHeader:
    height: 20
    style: GroupHeader
    content: "▸ {{ #group }} ({{ #count }} items)"

  groupFooter:
    height: 18
    style: GroupFooter
    cells:                          # celdas paralelas a las columnas por índice
      - { content: "Subtotal {{ #group }}:" }
      - null                        # vacía
      - { aggregate: count, format: "N0", align: right }
      - { content: "Promedio:", align: right }
      - { aggregate: sum, format: "N2", align: right }

  columns: [...]
```

Agregadores disponibles: `sum` · `count` · `avg`. Cada celda del footer agrega el **binding de la columna en su mismo índice**. Conversión numérica segura desde `int`/`long`/`decimal`/`double`/`float`/strings parseables.

> **Limitación:** las filas deben venir pre-ordenadas por la clave de grupo — no hay sort interno. Solo un nivel de anidamiento (sin grupos anidados).

### Template strings

| Placeholder | Resuelve a |
|---|---|
| `{{ pageNumber }}` | Número de página actual |
| `{{ totalPages }}` | Total de páginas |
| `{{ rowIndex }}` | Índice de fila (en contexto de tabla) |
| `{{ $.path.to.field }}` | Valor del JSON Path |
| `{{ #group }}` | Clave del grupo actual (en contexto de group header/footer) |
| `{{ #count }}` | Cantidad de filas en el grupo actual |

### Unidades

`pt`, `mm`, `cm`, `in` — el parser las reconoce: `1.5cm`, `15mm`, `1in`, `42pt`.

---

## 🎨 Renderers disponibles

### 📄 PDF — `NetReporter.Pdf`

Genera PDF vectorial vía **QuestPDF** (contenedor) + **SkiaSharp** (dibujo). El texto es seleccionable, el archivo se imprime con calidad nativa.

```csharp
byte[] pdf = new PdfRenderer().Render(renderList);
```

### 🌐 HTML — `NetReporter.Html`

Emite HTML self-contained con CSS `@page` para print y CSS de pantalla con sombras y márgenes entre páginas.

```csharp
string html = new HtmlRenderer().Render(renderList, new HtmlRenderOptions
{
    Title = "Factura",
    ShowScreenChrome = true   // sombra/margin entre páginas en pantalla
});
```

- 📱 Imprimible nativamente con Ctrl+P respetando tamaño de página
- 🚀 Cero deps externas (solo `System.Net.WebUtility`)
- 🎨 Inline CSS, fácil de embeber en cualquier página

### 🖼️ SVG — `NetReporter.Svg`

Una cadena SVG por página, usado internamente por el PDF y por el live preview del Designer.

```csharp
IReadOnlyList<string> svgs = new SvgRenderer().Render(renderList);
```

### 📊 XLSX — `NetReporter.Xlsx`

Renderer **semántico**: consume el `ReportDefinition` directo (sin `LayoutEngine`, sin `RenderList`). Los `TextElement`s se vuelven celdas, los `TableElement<TRow>` se vuelven rangos Excel reales (filtrables, sortables, banded). Los elementos decorativos (Line / Rectangle / Barcode) se omiten intencionalmente — no tienen equivalente en una hoja de cálculo.

```csharp
byte[] xlsx = new XlsxRenderer().Render(report, new XlsxRenderOptions
{
    WorksheetName = "Factura",     // default: ReportDefinition.Name (truncado a 31 chars)
    EmitNativeTables = true,       // tablas → Excel Tables (filtros + sort + banded rows)
    FreezeTableHeaders = false     // freeze pane debajo del header de cada tabla
});
```

- 📊 Tablas emitidas como **Excel Tables nativas** con filtros, sort y banded rows
- 🎨 Estilos por celda (font, color, alineación) preservados desde el `StyleSheet`
- 📐 Page setup (paper size, orientación, márgenes) mapeado al page setup de Excel
- 🖼️ Imágenes embebidas se preservan (size en puntos → pixels @ 96dpi)
- 🌍 Cultura por reporte respetada para formato de números/fechas

---

## 📐 Tamaños de papel

### US / ANSI
`Letter` · `Legal` · `Tabloid` · `Executive` · `Statement`

### ISO A
`A3` · `A4` · `A5` · `A6`

### ISO B
`B4` · `B5`

### Custom (ej. paperoll POS)

```yaml
page:
  size: custom
  width: 226.77        # 80mm en puntos
  height: 600
```

### Transformaciones fluent (API C#)

```csharp
PageSetup.A4.Landscape().WithMargins(Units.Cm(1.5))
PageSetup.Custom(226.77, 600).WithMargins(8)
```

---

## 🧪 Tests

```bash
dotnet test
```

```
┌─────────────────────────────────────┬───────┐
│ Proyecto                            │ Tests │
├─────────────────────────────────────┼───────┤
│ NetReporter.Core.Tests              │   60  │
│ NetReporter.Templates.Tests         │   92  │
│ NetReporter.Html.Tests              │   13  │
│ NetReporter.Xlsx.Tests              │   12  │
│ NetReporter.Barcodes.Tests          │    7  │
├─────────────────────────────────────┼───────┤
│ Total                               │  184  │
└─────────────────────────────────────┴───────┘
```

**Cobertura por área:**

- 🧠 **Core**: Color hex parsing, PageSetup transformations, StyleSheet (herencia + ciclos + cache), BorderSet factories, **algoritmo word-wrap**, **band auto-height**, **KeepTogether page-break**, **tablas agrupadas con sum/count/avg**.
- 📝 **Templates**: JSON Path (Select/SelectMany/edge cases), TemplateString (placeholders + literals + escape), YamlReportRewriter (los 11 métodos × casos felices y errores), FileName resolución.
- 🌐 **Html**: HTML válido, escape de chars especiales, fonts, alignments, líneas, rectangles, multi-page, print CSS.
- 📊 **Xlsx**: texto → celdas, tablas → rangos, Excel Tables nativas, mapeo de page setup, sanitización del nombre de worksheet, embedding de imágenes, formato por cultura.
- ▦ **Barcodes**: QR finder patterns, densidad de barras Code 128, validación estricta EAN-13, edge cases (empty/null/throwing fallback).

---

## ⚠️ Limitaciones conocidas

### No implementado (Fase 5+)

| Feature | Estado | Workaround actual |
|---|---|---|
| **Charts** | ❌ | — |
| **Subreportes** | ❌ | — |
| **Grupos multinivel** | ❌ | Solo un nivel de anidamiento soportado |
| **KeepTogether en DetailBand** | ⚠️ | Usa ReportHeader/Footer (limitación de ref-locals del engine) |
| **XLSX: líneas / rectángulos / barcodes** | ⚠️ | Elementos decorativos omitidos a propósito — XLSX es semántico, no pixel-perfect |

### Deuda técnica documentada

1. **Reflection en `LayoutEngine.RenderTableElement`** — workaround para invocar el método genérico `RenderTableElementGeneric<TRow>`. Una sola vez por tipo de tabla. Fix futuro: visitor pattern.
2. **`TextMeasurer` aproximado** — `chars * fontSize * 0.55`. La medición real la hace SkiaSharp al pintar. Para layout que dependa de medir texto real, reemplazar por SkiaSharp directo.
3. **Re-serialización YAML pierde comentarios** — el rewriter usa parse → modify POCO → re-serialize. Si vas a editar a mano YAML con comentarios, hazlo después de las acciones del Designer.

---

## 🛣️ Roadmap

### ✅ Completado

- [x] **Fase 1** — Pipeline IR → Layout → RenderList → PDF
- [x] **Fase 2.1** — Templates YAML + JSON binding
- [x] **Fase 2.2** — HTML renderer
- [x] **Fase 2.3** — Designer visual con drag-and-drop
- [x] **Fase 2.4** — Resize handles + snap + nudge + undo/redo
- [x] **Fase 2.5** — Toolbox para crear elementos
- [x] **Fase 2.6** — Save/Load templates a disco
- [x] **Fase 2.7** — Soporte completo de tablas en designer
- [x] **Fase 2.8** — Editor de columnas inline + resize redistributivo
- [x] **Fase 2.9** — Samples builtin + import file
- [x] **Fase 2.10** — `fileName` con template strings + sanitización
- [x] **Fase 3.1** — Word wrap real (`ITextMeasurer` + `SkiaTextMeasurer`)
- [x] **Fase 3.2** — Auto-height en bandas y text elements
- [x] **Fase 3.3** — `KeepTogether` para bloques atómicos
- [x] **Fase 3.4** — Tablas agrupadas con subtotales (`groupBy` + headers/footers + sum/count/avg)
- [x] **Fase 4.1** — Imágenes embebidas (PNG/JPEG/GIF/WebP, path local o data URI)
- [x] **Fase 4.2** — Barcodes y QR vectoriales (ZXing.Net opt-in)
- [x] **Fase 4.3** — Designer integra 9 samples builtin (clientes, invoice-laser/paperoll, wrap-demo, grouped-invoice, invoice-with-qr, barcodes-demo, keep-together, **invoice-complete** flagship)
- [x] **Fase 4.4** — XLSX renderer semántico (ClosedXML, Excel Tables nativas) + export ⭳ XLSX en el Designer
- [x] **Fase 4.5** — Zoom en el preview del Designer (25%–400%, ajustar al ancho, atajos, persistido)
- [x] **Tests** — 184 tests verdes en 5 proyectos

### 🚧 En consideración

- [ ] Grupos multinivel (nested groups)
- [ ] Multi-selección + copy/paste en Designer
- [ ] Charts simples (bar / line / pie)
- [ ] DSL parsed expressions (en lugar de closures C#)
- [ ] KeepTogether en DetailBand (requiere refactor del engine)
- [ ] UI del Designer para picker de imágenes/barcodes (hoy solo via YAML o Save-as a un sample)

---

## 📄 Licencias

| Componente | Licencia |
|---|---|
| **NetReporter** | TBD |
| **QuestPDF** | MIT para uso no comercial y empresas <1M USD/año revenue. Comercial: Professional ($699 one-time). [Pricing](https://www.questpdf.com/pricing.html) |
| **SkiaSharp** | MIT |
| **YamlDotNet** | MIT |
| **ZXing.Net** | Apache 2.0 (solo si se referencia `NetReporter.Barcodes`) |
| **ClosedXML** | MIT (solo si se referencia `NetReporter.Xlsx`) |
| **HTMX** | BSD-2-Clause |
| **Alpine.js** | MIT |
| **Tailwind CSS** | MIT |

---

## 📎 Referencias internas

- [`NetReporter-Prototype-Invoice.md`](NetReporter-Prototype-Invoice.md) — spec original del prototipo (≈2000 líneas en español).
- [`CLAUDE.md`](CLAUDE.md) — guía para asistentes Claude Code en este repo.
- [`PLAN.md`](PLAN.md) — plan paso a paso por si se retoma tras interrupción.

---

<div align="center">

**Hecho con .NET 10 · QuestPDF · SkiaSharp · YamlDotNet · ZXing.Net · ClosedXML · HTMX · Alpine.js · Tailwind**

</div>
