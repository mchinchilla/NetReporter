# NetReporter

Motor de reportes para .NET 10 con arquitectura de **IR (Intermediate Representation)**: una definición declarativa del reporte se convierte primero a un `RenderList` de comandos absolutos de dibujo, y ese `RenderList` alimenta al renderer final (PDF hoy; HTML/XLSX en el futuro).

El objetivo es que **cambiar el backend de salida no requiera reescribir el reporte** — el mismo IR produce PDF, HTML y XLSX consistentemente.

> **Estado:** prototipo funcional. Genera facturas con encabezado, tabla de líneas, totales y paginación básica. Ver [Limitaciones conocidas](#limitaciones-conocidas).

---

## Tabla de contenidos

1. [Arquitectura](#arquitectura)
2. [Estructura de la solución](#estructura-de-la-solución)
3. [Setup](#setup)
4. [Quick start](#quick-start)
5. [Guía de uso por capas](#guía-de-uso-por-capas)
   - [Primitives](#1-primitives)
   - [Styles](#2-styles)
   - [Data binding](#3-data-binding)
   - [Expressions](#4-expressions)
   - [Elements](#5-elements)
   - [Bands](#6-bands)
   - [ReportDefinition](#7-reportdefinition)
   - [LayoutEngine](#8-layoutengine)
   - [PdfRenderer](#9-pdfrenderer)
6. [Tamaños de papel disponibles](#tamaños-de-papel-disponibles)
7. [Ejemplo completo: Factura](#ejemplo-completo-factura)
8. [Build y ejecución](#build-y-ejecución)
9. [Limitaciones conocidas](#limitaciones-conocidas)
10. [Roadmap](#roadmap)
11. [Licencias](#licencias)

---

## Arquitectura

```
ReportDefinition (IR)                    ← el usuario construye esto
       │
       ▼
 LayoutEngine                            ← measure + arrange + paginación
       │
       ▼
 RenderList (lista de DrawText, DrawLine, DrawRectangle)
       │
       ▼
 PdfRenderer → bytes[]  (PDF)            ← hoy
 HtmlRenderer → string  (HTML+CSS)       ← futuro
 XlsxRenderer → bytes[] (XLSX)           ← futuro
```

**Principios no negociables:**

- **QuestPDF solo como contenedor de documento/página.** Todo el dibujo pasa por SkiaSharp → SVG → `container.Svg(...)`. No se usa `container.Row/Column/Table`.
- **Sin reflection en hot paths.** Data binding con `Func<TRow, TValue>` tipados. Expresiones con `Func<IEvaluationContext, T>`. Una sola excepción: `LayoutEngine` usa `MakeGenericMethod` una vez por tipo de tabla (documentado como deuda técnica).
- **Styling nativo.** Ni CSS ni Tailwind. `StyleSheet` + `StyleRef` (struct zero-alloc) con herencia via `BasedOn` (cap 16 niveles, cycle detection).
- **Tablas genéricas.** `TableElement<TRow>` mantiene el tipo para que JIT inlinee el acceso.

---

## Estructura de la solución

```
NetReporter.slnx
├── src/
│   ├── NetReporter.Core/       Primitivos, IR, LayoutEngine, RenderList
│   └── NetReporter.Pdf/        PdfRenderer (QuestPDF + SkiaSharp → SVG)
└── samples/
    └── InvoiceSample/          Factura realista (demo end-to-end)
```

Flags obligatorios en los tres `.csproj`:

```xml
<TargetFramework>net10.0</TargetFramework>
<LangVersion>latest</LangVersion>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

---

## Setup

Requisitos:
- **.NET 10 SDK** (probado con 10.0.103).
- Paquetes NuGet (ya referenciados): `QuestPDF` ≥ 2026.2.4, `SkiaSharp` ≥ 3.119.2.

Clonar y construir:

```bash
git clone <este-repo>
cd NetReporter
dotnet build
```

Activar la licencia QuestPDF en tu `Program.cs` (antes del primer render):

```csharp
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
```

La licencia Community es gratis para uso no comercial y para empresas con revenue < 1M USD/año. Más info: https://www.questpdf.com/pricing.html.

---

## Quick start

Reporte mínimo de una página con un título y una línea:

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
                },
                new LineElement
                {
                    Bounds = new Rect(0, 40, page.ContentWidth, 0),
                    Color = Color.Black,
                    Thickness = 0.5
                }
            }
        }
    }
};

var rl = new LayoutEngine().Layout(report);
var pdf = new PdfRenderer().Render(rl);
File.WriteAllBytes("hola.pdf", pdf);
```

---

## Guía de uso por capas

### 1. Primitives

Namespace: `NetReporter.Core.Primitives`.

| Tipo | Descripción |
|---|---|
| `Point(X, Y)` | Coordenada en puntos PDF. |
| `Size(Width, Height)` | Dimensión. `Size.Infinite` disponible. |
| `Rect(X, Y, W, H)` | Rectángulo con `Right`, `Bottom`, helpers `WithX/WithY/WithWidth/WithHeight/Offset`. |
| `Thickness(L, T, R, B)` | Con overloads `(uniform)` y `(horizontal, vertical)`. |
| `Color(R, G, B, A)` | `Color.Black/White/Transparent`, `Color.FromHex("#RRGGBB")`, `ToHexString()`. |
| `Units.Cm / Mm / Inch` | Conversión a puntos (72 pt/inch). |
| `PageSetup` | Tamaño + márgenes + orientación. Ver [Tamaños](#tamaños-de-papel-disponibles). |

Todas son **records/structs inmutables**. Se componen con `with`:

```csharp
var pageA4Landscape = PageSetup.A4.Landscape().WithMargins(Units.Cm(1.5));
var customRect = new Rect(0, 0, 100, 50).WithWidth(200);
```

### 2. Styles

Namespace: `NetReporter.Core.Styles`.

Construcción del stylesheet con el builder fluent:

```csharp
var styles = new StyleSheetBuilder()
    .Add("Default", s => s
        .FontFamily("Helvetica")
        .FontSize(10)
        .Foreground(Color.FromHex("#0F172A")))

    .Add("Title", s => s.BasedOn("Default").FontSize(18).Bold())

    .Add("TableHeader", s => s.BasedOn("Default")
        .Bold()
        .Background(Color.FromHex("#F1F5F9"))
        .Padding(new Thickness(4, 3))
        .Border(BorderSet.OnlyBottom(0.5, Color.Black)))

    .Build();
```

**API disponible en `StyleBuilder`:**

`BasedOn` · `FontFamily` · `FontSize` · `Bold` · `Italic` · `Foreground` · `Background` · `Padding(Thickness)` · `Padding(double uniform)` · `Border(BorderSet)` · `Align(TextAlignment)` · `VAlign(VerticalAlignment)` · `Format(string)`.

**`BorderSet` factories** (renombrados para no colisionar con las propiedades `Top/Bottom`):

```csharp
BorderSet.All(0.5, Color.Black)
BorderSet.OnlyTop(1, Color.Black)
BorderSet.OnlyBottom(0.5, Color.Gray)
BorderSet.Horizontal(0.5, Color.Black)   // Top + Bottom
```

**Referenciar estilos en elementos:**

```csharp
// Opción 1: literal (alloc cero, struct)
Style = new StyleRef("Title")

// Opción 2: constantes tipadas (patrón recomendado)
public static class MyStyles {
    public static readonly StyleRef Title = new("Title");
}
// ...
Style = MyStyles.Title
```

Herencia: cada estilo puede declarar `BasedOn(name)`. El engine resuelve la cadena (cap 16 niveles, lanza si detecta ciclos) y cachea el resultado en `ResolvedStyle`. Los campos nulos toman el valor del padre; el fallback final es el `BuiltInDefault` (`Helvetica 10pt negro sobre transparente, TextAlign=Left, VAlign=Top, sin padding/border/format`).

### 3. Data binding

Namespace: `NetReporter.Core.DataBinding`.

```csharp
using NetReporter.Core.DataBinding;

IDataBinding<Invoice, decimal> total = Bind.From<Invoice, decimal>(i => i.Total);
IDataBinding<Invoice, object?> totalAsObj = Bind.From<Invoice, object?>(i => i.Total);
```

Se usa con `TableColumn<TRow>`:

```csharp
new TableColumn<LineaFactura>(
    "Subtotal",
    Bind.From<LineaFactura, object?>(l => l.Subtotal),
    Width: 80,
    Format: "N2",
    Align: TextAlignment.Right)
```

### 4. Expressions

Namespace: `NetReporter.Core.Expressions`.

Helpers en la clase estática `Expr`:

```csharp
Expr.Str("texto literal")                           // IExpression<string>
Expr.Literal(42)                                    // IExpression<int>
Expr.Of(ctx => ctx.PageNumber.ToString())           // IExpression<string>
Expr.PageNumber                                     // IExpression<string>, "1"
Expr.TotalPages                                     // IExpression<string>, "5"
Expr.PageOfTotal                                    // IExpression<string>, "Página 1 de 5"
```

`IEvaluationContext` expone: `PageNumber`, `TotalPages`, `RowIndex`, `CurrentRow`, `GetParameter(name)`, `GetAggregate(name)`. En el prototipo `GetParameter`/`GetAggregate` devuelven siempre `null` — para valores calculados hoy se cierra sobre variables locales (closure).

### 5. Elements

Namespace: `NetReporter.Core.Elements`.

Todos los elementos heredan de `ReportElement` con:
- `Bounds` (`Rect`, requerido) — posición **relativa a la banda**.
- `Style` (`StyleRef`, opcional).
- `Visible` (`IExpression<bool>?`, opcional — aún no se evalúa en el layout engine).

| Elemento | Campos clave |
|---|---|
| `TextElement` | `Content: IExpression<string>`, `WordWrap`, `AutoHeight` (aún sin efecto real). |
| `TableElement<TRow>` | `Rows`, `Columns`, `RowHeight`, `HeaderHeight`, `HeaderMode`, `HeaderStyle`, `RowStyle`, `AlternateRowStyle`. |
| `LineElement` | `Orientation` (Horizontal/Vertical), `Thickness`, `Color`. |
| `RectangleElement` | `Fill: Color?`, `BorderLine: BorderLine?`. |

**`TableColumn<TRow>`:**
```csharp
new TableColumn<TRow>(
    Header: "Título",
    Binding: IDataBinding<TRow, object?>,
    Width: double,         // puntos
    Format: string?,       // null, "N2", "dd/MM/yyyy", etc.
    Align: TextAlignment); // Left|Center|Right|Justify
```

**`TableHeaderMode`:** `PrintOnce` o `RepeatOnPageBreak`.

El `Format` se aplica si el valor implementa `IFormattable` con la `Culture` del reporte.

### 6. Bands

Namespace: `NetReporter.Core.Bands`.

Cinco tipos — todos heredan de `Band` con `Height`, `PrintOnFirstPage`, `PrintOnLastPage`, `Visible?`, `Elements`:

| Banda | Cuándo se imprime |
|---|---|
| `ReportHeaderBand` | Una vez al inicio del reporte. |
| `PageHeaderBand` | Al inicio de cada página. |
| `DetailBand` | Contenido principal. En el prototipo es banda fija — las filas dinámicas viven dentro de un `TableElement`. |
| `PageFooterBand` | Al final de cada página. Soporta `Expr.PageOfTotal`. |
| `ReportFooterBand` | Una vez al final del reporte (totales, etc.). |

`PageHeaderBand` y `PageFooterBand` pueden tener `Height = 0` si no los usas.

### 7. ReportDefinition

Namespace: `NetReporter.Core.Definition`.

```csharp
var report = new ReportDefinition
{
    Name = "Factura",                    // requerido
    Title = "Factura #123",              // opcional
    Page = PageSetup.Letter,             // requerido
    Styles = miStyleSheet,               // requerido
    Culture = CultureInfo.GetCultureInfo("es-HN"),  // default: Invariant
    Bands = new Band[] { ... }           // requerido
};
```

### 8. LayoutEngine

Namespace: `NetReporter.Core.Layout`.

```csharp
var renderList = new LayoutEngine().Layout(report);
// renderList.Pages.Count      → total de páginas
// renderList.Pages[0].Commands → lista de DrawTextCommand/DrawLineCommand/DrawRectangleCommand
```

**Qué hace el layout engine hoy:**
1. Reserva altura para `PageHeader` y `PageFooter` — el área útil es `ContentHeight − headerH − footerH`.
2. Emite `ReportHeader` una vez.
3. Emite cada `DetailBand`. Si una tabla no cabe, parte filas entre páginas y repite el header si `HeaderMode = RepeatOnPageBreak`.
4. Emite `ReportFooter` — si no cabe, abre página nueva.
5. Emite `PageHeader`/`PageFooter` en cada página. En la pasada 2 (post-paginación) `PageFooter` conoce `TotalPages`, por lo que `Expr.PageOfTotal` funciona.

### 9. PdfRenderer

Namespace: `NetReporter.Pdf`.

```csharp
var pdfBytes = new PdfRenderer().Render(renderList);
File.WriteAllBytes("salida.pdf", pdfBytes);
```

**Cómo funciona por dentro:**

1. Para cada `RenderPage`, crea un `SKSvgCanvas` de SkiaSharp que escribe SVG a un `MemoryStream`.
2. Recorre los `RenderCommand`s y los dibuja con la API estándar de `SKCanvas` (`DrawText`, `DrawLine`, `DrawRect`).
3. Cada `DrawText` se recorta a su `Bounds` con `canvas.ClipRect(...)` — **el texto que excede su rect se trunca visualmente** en el borde. No se agrega ellipsis.
4. El SVG resultante se pasa a `container.Svg(svg)` dentro de una `Document.Create` de QuestPDF. QuestPDF solo gestiona la paginación del documento y la salida PDF.

Este diseño reemplaza al deprecado `container.Canvas(...)` de QuestPDF (retirado en 2024.3.0) manteniendo el principio "QuestPDF solo como contenedor". Cambiar de QuestPDF a otro writer PDF afecta únicamente a `PdfRenderer.cs`.

---

## Tamaños de papel disponibles

Todos en `NetReporter.Core.Primitives.PageSetup`:

### US / ANSI
| Constante | Tamaño | Uso típico |
|---|---|---|
| `Letter` | 8.5" × 11" | Documentos estándar en US/CA/MX. |
| `Legal` | 8.5" × 14" | Contratos. |
| `Tabloid` | 11" × 17" | Planos/esquemas. |
| `Executive` | 7.25" × 10.5" | Memos. |
| `Statement` | 5.5" × 8.5" | Recibos/notas. |

### ISO A
| Constante | Tamaño |
|---|---|
| `A3` | 297 × 420 mm |
| `A4` | 210 × 297 mm |
| `A5` | 148 × 210 mm |
| `A6` | 105 × 148 mm |

### ISO B
| Constante | Tamaño |
|---|---|
| `B4` | 250 × 353 mm |
| `B5` | 176 × 250 mm |

### Custom

```csharp
PageSetup.Custom(
    widthPt: Units.Cm(10),
    heightPt: Units.Cm(15),
    margins: new Thickness(Units.Cm(1)));
```

### Transformaciones fluent

```csharp
PageSetup.A4                                     // A4 portrait, márgenes 2cm default
PageSetup.A4.Landscape()                         // intercambia width/height, Orientation = Landscape
PageSetup.Letter.WithMargins(20)                 // margins uniformes 20pt
PageSetup.Letter.WithMargins(new Thickness(Units.Cm(1.5), Units.Cm(1.8)))
PageSetup.Legal.Landscape().WithMargins(Units.Mm(10))
```

Derivados:
- `page.ContentWidth` = `Size.Width − Margins.Horizontal`
- `page.ContentHeight` = `Size.Height − Margins.Vertical`
- `page.ContentOrigin` = `new Point(Margins.Left, Margins.Top)`

---

## Ejemplo completo: Factura

Ver `samples/InvoiceSample/` para un reporte end-to-end con:

- **Modelo de datos**: `Factura`, `Empresa`, `Cliente`, `LineaFactura` (records).
- **ErpTheme**: paleta (`ErpColors`) + catálogo de estilos (`ErpStyles`) + `StyleSheet` (`ErpTheme.Default`).
- **InvoiceReport**: constructor que toma una `Factura` y un `PageSetup` opcional, con 5 bandas (ReportHeader, PageHeader vacío, Detail con tabla, ReportFooter con totales, PageFooter con "Página N de M").

### API del sample

```csharp
var report = InvoiceReport.Build(factura);                        // Letter, márgenes 1.5cm/1.8cm
var report = InvoiceReport.Build(factura, PageSetup.A4);          // A4, márgenes default 2cm
var report = InvoiceReport.Build(factura, PageSetup.Legal.Landscape().WithMargins(Units.Mm(15)));
```

Internamente toma `page.ContentWidth` y calcula todos los anchos (bloques del header, columnas de la tabla, bloque de totales) proporcionalmente — el reporte se adapta automáticamente al tamaño de papel elegido.

### Ancho proporcional de columnas

| Columna | Proporción |
|---|---|
| Código | 14% |
| Descripción | 49% (absorbe redondeo) |
| Cant. | 9% |
| P. Unit. | 14% |
| Subtotal | 14% |

---

## Build y ejecución

```bash
dotnet build
dotnet run --project samples/InvoiceSample
```

Salida:
```
Factura generada: .../samples/InvoiceSample/bin/Debug/net10.0/factura.pdf
Páginas: 1
Comandos totales: 97
```

Abrir `factura.pdf` con cualquier visor PDF.

---

## Limitaciones conocidas

### No implementado (Fase 2)

- **Grupos y subtotales.** No hay `GroupHeaderBand`/`GroupFooterBand`. Los agregados se pre-calculan fuera del reporte y se cierran con closure.
- **Word wrap real.** Los `TextElement` dibujan en una línea; si exceden el `Bounds`, se **truncan visualmente** (clipping) — no se agrega ellipsis ni se envuelve a siguiente línea.
- **Auto-height.** Los elementos usan altura fija. `AutoHeight = true` en `TextElement` aún no se respeta.
- **KeepTogether.** No hay control de "mantener esta banda sin romper entre páginas".
- **Orphan/widow control** para tablas.
- **Images / Barcode / QR / Charts.** Aún no hay primitivos para estos.
- **Subreportes.**
- **DSL de expresiones.** Solo closures C# (`Expr.Of(ctx => ...)`).
- **HTML / XLSX renderers.** Solo PDF por ahora.
- **Visor interactivo (Blazor).**
- **Parámetros / agregados.** `IEvaluationContext.GetParameter/GetAggregate` devuelven `null` — se usan closures para valores calculados.

### Deuda técnica

1. **Reflection en `LayoutEngine.RenderTableElement`.** Workaround para invocar el método genérico `RenderTableElementGeneric<TRow>`. Una sola vez por tipo de tabla (no hot path). Fix futuro: visitor pattern o rediseño para que `TableElement` exponga un método virtual de render.
2. **`TextMeasurer` es aproximación lineal** (`chars * fontSize * 0.55`). La medición real la hace SkiaSharp al pintar. Para layout que dependa de medir texto real, habría que reemplazar por medición SkiaSharp.
3. **`BorderSet.Top/Bottom` factories renombrados** a `OnlyTop`/`OnlyBottom` para no colisionar con las propiedades de instancia del mismo nombre.
4. **Alias `RlRenderList`** necesario en `LayoutEngine.cs` y `PdfRenderer.cs` porque el namespace `NetReporter.Core.RenderList` comparte nombre con el tipo `RenderList`.
5. **`DetailBand` no itera filas dinámicamente.** En el prototipo es una banda con contenido fijo; las filas repetitivas están dentro de un `TableElement<TRow>`. La versión futura tendrá `IDataSource<TRow>` a nivel de banda.

---

## Roadmap

En orden sugerido:

1. Pruebas con facturas de 50+ líneas → validar paginación y repetición de headers de tabla.
2. Golden tests: serializar `RenderList` a JSON y comparar contra snapshots.
3. **Fase 2.1** — grupos + subtotales (`GroupHeaderBand`/`GroupFooterBand`, agregados `Sum`/`Count`).
4. **Fase 2.2** — `HtmlRenderer` consumiendo el mismo `RenderList` (con CSS `@page`).
5. **Fase 2.3** — Visor Blazor sobre el HTML renderer.
6. **Fase 2.4** — Word wrap real y auto-height (requiere medición SkiaSharp).
7. **Fase 2.5** — Images, QR, barcodes.
8. **Fase 2.6** — `XlsxRenderer`.
9. **Fase 2.7** — DSL de expresiones parseado (hoy: solo closures C#).

---

## Licencias

- **NetReporter** — TBD.
- **QuestPDF** — MIT para uso no comercial y para empresas con revenue < 1M USD/año (Community License). Uso comercial en empresas mayores requiere licencia Professional ($699 one-time). Confirma antes que tu caso está cubierto: https://www.questpdf.com/pricing.html.
- **SkiaSharp** — MIT.

---

## Referencias internas

- `NetReporter-Prototype-Invoice.md` — spec original del prototipo (Español, 2000 líneas).
- `CLAUDE.md` — guía para asistentes Claude Code trabajando en este repo.
- `PLAN.md` — plan paso a paso por si se retoma tras interrupción.
