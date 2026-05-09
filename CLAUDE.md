# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository status

NetReporter is **in production use** as the reporting engine of an internal ERP. It started as a prototype (the original spec is preserved at `NetReporter-Prototype-Invoice.md` for historical context), but the codebase has grown well past that scope: multiple renderers, a visual web designer, XLSX/HTML/SVG outputs, embedded images, vector barcodes, grouped tables with subtotals, real word-wrap, etc. Treat this repo as a working library — most tasks are **incremental feature work or bug fixes against shipped code**, not "transcribe the spec".

The README is the most up-to-date reference (`README.md` / `README.es.md`). `NetReporter-Prototype-Invoice.md` only matters when investigating very early architectural decisions.

## Repository layout

```
src/
  NetReporter.Core/        IR, Primitives, Styles, Layout, RenderList, ITextMeasurer
  NetReporter.Templates/   YAML loader/rewriter, JSON Path, template strings, fileName
  NetReporter.Pdf/         PdfRenderer (delegates to Svg)
  NetReporter.Svg/         SvgRenderer (SkiaSharp.SKSvgCanvas)
  NetReporter.Html/        HtmlRenderer (paginated HTML/CSS @page)
  NetReporter.Xlsx/        XlsxRenderer — semantic, consumes ReportDefinition directly
  NetReporter.Barcodes/    ZXing-based QR/Code128/Code39/EAN-13 (opt-in)
tests/
  NetReporter.Core.Tests/  NetReporter.Templates.Tests/  NetReporter.Html.Tests/
  NetReporter.Xlsx.Tests/  NetReporter.Barcodes.Tests/   — 184 tests total
tools/
  NetReporter.Designer/    ASP.NET Core MVC + Alpine.js + HTMX + Tailwind (CDN)
samples/
  InvoiceSample/           Imperative C# API
  TemplateSample/          9 YAML templates exporting PDF + XLSX
```

Solution file is `NetReporter.slnx` (the newer XML format — not classic `.sln`).

## Toolchain

- **.NET 10** (`dotnet --version` → 10.0.103). Do not downgrade `<TargetFramework>`.
- All csproj files include: `<LangVersion>latest</LangVersion>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. Don't relax these to make a warning go away.
- QuestPDF license must be activated wherever the Pdf renderer is invoked: `QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;`.

## Build / run / test

```bash
dotnet build
dotnet test                                                # 184 tests
dotnet run --project samples/InvoiceSample                 # → factura.pdf
dotnet run --project samples/TemplateSample                # → 9 PDF + 9 XLSX
dotnet run --project tools/NetReporter.Designer            # → http://localhost:5296
```

## Architecture — the non-negotiable invariants

The pipeline is **IR → LayoutEngine → RenderList → Renderer** (PDF/HTML/SVG). XLSX is the **semantic** exception: it consumes `ReportDefinition` directly, bypassing `LayoutEngine`/`RenderList`, because spreadsheet cells map to elements, not pixels.

1. **QuestPDF is used only as a host**, never as a layout engine. All positioning is done by `LayoutEngine`, which emits absolute `DrawText/DrawLine/DrawRectangle` commands. `PdfRenderer` delegates to `SvgRenderer` and embeds the SVG via `container.Svg(...)`. If you find yourself calling `container.Row` / `container.Table` / `container.Column` in the Pdf renderer, you are doing it wrong.

2. **No reflection in hot paths.** Data binding is `IDataBinding<TRow,TValue>` wrapping a typed `Func<TRow,TValue>`. Expressions are `IExpression<T>` with `Func<IEvaluationContext,T>`. Never do `row.GetType()` or `PropertyInfo.GetValue`. The one tolerated exception is `LayoutEngine.RenderTableElement` using `MakeGenericMethod` once per table type — tracked as tech debt (visitor pattern is the planned fix).

3. **No CSS / Tailwind for the report itself.** Styling is the native `StyleSheet` with `StyleRef` (a struct for zero-alloc passing) and named styles resolved through `BasedOn` inheritance, capped at 16 levels with cycle detection and a `ConcurrentDictionary` cache. `ResolvedStyle` is what renderers see — no nullables. (Tailwind/Alpine/HTMX are only used inside the Designer's UI chrome — never inside report templates.)

4. **`TableElement<TRow>` is generic on purpose** so `IDataBinding<TRow,…>` stays typed and JIT-inlinable. Do not propose an `object`-typed version.

5. **`RenderCommand` is an `abstract record` with sealed subclasses** (`DrawTextCommand`, `DrawLineCommand`, `DrawRectangleCommand`). C# discriminated unions aren't a thing yet; use exhaustive pattern matching.

6. **Multiple outputs from one IR.** A new renderer should consume `RenderList` (or `ReportDefinition` for semantic outputs). Never embed format-specific concepts (CSS classes, Excel cell refs, PDF objects) in the IR itself.

## Renderers — when to consume what

| Renderer | Input | Drawing primitive | Notes |
|---|---|---|---|
| `PdfRenderer` | `RenderList` | SkiaSharp via SvgRenderer + QuestPDF host | Vector, selectable text |
| `HtmlRenderer` | `RenderList` | HTML/CSS `@page` | Self-contained, printable |
| `SvgRenderer` | `RenderList` | `SKSvgCanvas` | One SVG string per page; reused by PDF and Designer preview |
| `XlsxRenderer` | `ReportDefinition` (not RenderList) | ClosedXML | Semantic — text → cells, tables → native Excel Tables. Lines/rectangles/barcodes are intentionally skipped |

When the user asks for a feature that "should also work in XLSX", remember XLSX is semantic: ask whether it has a meaningful cell-level analog before adding logic to the Xlsx renderer.

## Templates and Designer

- `NetReporter.Templates` parses YAML into `ReportDefinition` and supports JSON Path bindings (`$.client.name`), template strings (`{{ pageNumber }}`, `{{ $.title }}`, `{{ #group }}`), and templated `fileName` resolved at export.
- `YamlReportRewriter` does parse → modify POCO → re-serialize. **This drops comments**. If you change rewriter behavior, the existing tests in `NetReporter.Templates.Tests` cover all 11 mutation methods — extend them.
- The Designer renders the **SVG** preview live via HTMX, with Alpine.js for state. Drag/resize/create operations POST back to mutation endpoints (`/Home/Move`, `/Home/Update`, `/Home/Add`, etc.) which call into `YamlReportRewriter`.
- The preview supports zoom (25%–400%) via CSS `zoom` on the preview-root wrapper. Mouse coords are compensated by `this.zoom` in drag/resize/create handlers — anything new that converts viewport-px to page-pt must do the same.

## Scope discipline

- Known limitations are documented in the README "Known limitations" + "Roadmap" sections. Items marked roadmap-only (charts, multi-level grouping, KeepTogether on DetailBand, etc.) are deliberately out of scope unless the user opens that work explicitly.
- Documented technical debt (reflection in `RenderTableElement`, approximate `EstimateTextMeasurer`, YAML rewriter dropping comments) is intentional — don't "fix" them as a side-quest. If a fix is needed, flag it and propose it.
- New band kinds, new element types, or new renderers are **structural** changes — they touch IR, layout, and every renderer. Treat them as their own task with a clear scope, not as an addendum to a smaller change.

## When working in this repo

- Read the README before assuming a feature doesn't exist — the surface area is bigger than the original prototype scope.
- The samples in `samples/TemplateSample/*.yaml` are the best reference for YAML schema. `invoice-complete.yaml` exercises every Phase 1-4 feature.
- Tests live in `tests/`. There is no integration test for the Designer (it's a UI tool); manual smoke testing in the browser is expected for Designer changes.
