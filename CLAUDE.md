# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository status

NetReporter is **in production use** as the reporting engine of an internal ERP. It started as a prototype (the original spec is preserved at `NetReporter-Prototype-Invoice.md` for historical context), but the codebase has grown well past that scope: multiple renderers, a visual web designer, XLSX/HTML/SVG outputs, embedded images, vector barcodes, grouped tables with subtotals, real word-wrap, etc. Treat this repo as a working library — most tasks are **incremental feature work or bug fixes against shipped code**, not "transcribe the spec".

The README is the most up-to-date reference (`README.md` / `README.es.md`). `NetReporter-Prototype-Invoice.md` only matters when investigating very early architectural decisions.

## Repository layout

```
src/
  NetReporter.Core/        IR, Primitives, Styles, Layout (LayoutEngine + ChartLayout), RenderList, ITextMeasurer
  NetReporter.Templates/   YAML loader/rewriter, JSON Path, template strings, expression DSL, fileName
  NetReporter.Pdf/         PdfRenderer (delegates to Svg)
  NetReporter.Svg/         SvgRenderer (SkiaSharp.SKSvgCanvas)
  NetReporter.Html/        HtmlRenderer (paginated HTML/CSS @page)
  NetReporter.Xlsx/        XlsxRenderer — semantic, consumes ReportDefinition directly
  NetReporter.Barcodes/    ZXing-based QR/Code128/Code39/EAN-13 (opt-in)
  NetReporter/             Meta-package (no code) that references all of the above
tests/
  NetReporter.Core.Tests/  NetReporter.Templates.Tests/  NetReporter.Html.Tests/
  NetReporter.Svg.Tests/   NetReporter.Xlsx.Tests/       NetReporter.Barcodes.Tests/   — 365 tests total
tools/
  NetReporter.Designer/    ASP.NET Core MVC + Alpine.js + HTMX + Tailwind (CDN)
samples/
  InvoiceSample/           Imperative C# API
  TemplateSample/          13 YAML templates exporting PDF + XLSX
```

Solution file is `NetReporter.slnx` (the newer XML format — not classic `.sln`).

## Toolchain

- **.NET 10** (`dotnet --version` → 10.0.103). Do not downgrade `<TargetFramework>`.
- All csproj files include: `<LangVersion>latest</LangVersion>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. Don't relax these to make a warning go away.
- QuestPDF license must be activated wherever the Pdf renderer is invoked: `QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;`.

## Build / run / test

```bash
dotnet build
dotnet test                                                # 365 tests
dotnet run --project samples/InvoiceSample                 # → factura.pdf
dotnet run --project samples/TemplateSample                # → 13 PDF + 13 XLSX
dotnet run --project tools/NetReporter.Designer            # → http://localhost:5296
```

## Architecture — the non-negotiable invariants

The pipeline is **IR → LayoutEngine → RenderList → Renderer** (PDF/HTML/SVG). XLSX is the **semantic** exception: it consumes `ReportDefinition` directly, bypassing `LayoutEngine`/`RenderList`, because spreadsheet cells map to elements, not pixels.

1. **QuestPDF is used only as a host**, never as a layout engine. All positioning is done by `LayoutEngine`, which emits absolute `DrawText/DrawLine/DrawRectangle` commands. `PdfRenderer` delegates to `SvgRenderer` and embeds the SVG via `container.Svg(...)`. If you find yourself calling `container.Row` / `container.Table` / `container.Column` in the Pdf renderer, you are doing it wrong.

2. **No reflection in hot paths.** Data binding is `IDataBinding<TRow,TValue>` wrapping a typed `Func<TRow,TValue>`. Expressions are `IExpression<T>` with `Func<IEvaluationContext,T>`. Never do `row.GetType()` or `PropertyInfo.GetValue`. Code that needs a table's row type dispatches through `ITableElement.Accept(ITableVisitor<T>)` (layout engine and XLSX emitter both do) — never `MakeGenericMethod`. The YAML expression DSL (`NetReporter.Templates/Expressions`) compiles text → delegate chain once at load; don't add runtime parsing or reflection to it.

3. **No CSS / Tailwind for the report itself.** Styling is the native `StyleSheet` with `StyleRef` (a struct for zero-alloc passing) and named styles resolved through `BasedOn` inheritance, capped at 16 levels with cycle detection and a `ConcurrentDictionary` cache. `ResolvedStyle` is what renderers see — no nullables. (Tailwind/Alpine/HTMX are only used inside the Designer's UI chrome — never inside report templates.)

4. **`TableElement<TRow>` is generic on purpose** so `IDataBinding<TRow,…>` stays typed and JIT-inlinable. Do not propose an `object`-typed version.

5. **`RenderCommand` is an `abstract record` with sealed subclasses** (`DrawTextCommand`, `DrawLineCommand`, `DrawRectangleCommand`, `DrawImageCommand`, `DrawPathCommand`). C# discriminated unions aren't a thing yet; use exhaustive pattern matching — a new command must be handled in `SvgRenderer` and `HtmlRenderer` (PDF goes through SVG). Arcs/curves are discretized in the layout (e.g. `ChartLayout`), so renderers only need move/line/close.

6. **Multiple outputs from one IR.** A new renderer should consume `RenderList` (or `ReportDefinition` for semantic outputs). Never embed format-specific concepts (CSS classes, Excel cell refs, PDF objects) in the IR itself.

## Layout engine notes (v1.1)

- `LayoutEngine.Layout` runs a private `LayoutRun`: the current page (`PageBuilder`, always the same instance — `NewPage()` snapshots it into the list and resets it), the cursor and `maxY` are fields, not `ref` parameters. Anything can break a page; don't reintroduce the old "copy commands back into workingPage" pattern.
- `Measure(Action)` does a dry-run layout into a scratch page with no bottom limit; it powers KeepTogether (report/detail bands) and auto-height band measurement. Keep emission code side-effect free apart from `_page`/`_cursorY`/`_ctx` so measuring stays safe.
- Page headers and footers are emitted in pass 2 (`EmitPageChrome`) once `totalPages` is known.
- A table's `Bounds.Y` is an offset from the current flow position. Detail bands without tables move whole to the next page when they don't fit.

## Renderers — when to consume what

| Renderer | Input | Drawing primitive | Notes |
|---|---|---|---|
| `PdfRenderer` | `RenderList` | SkiaSharp via SvgRenderer + QuestPDF host | Vector, selectable text |
| `HtmlRenderer` | `RenderList` | HTML/CSS `@page` | Self-contained, printable |
| `SvgRenderer` | `RenderList` | `SKSvgCanvas` | One SVG string per page; reused by PDF and Designer preview |
| `XlsxRenderer` | `ReportDefinition` (not RenderList) | ClosedXML | Semantic — text → cells, tables → native Excel Tables (grouped tables → outline), charts → data tables. Lines/rectangles/barcodes are intentionally skipped |

When the user asks for a feature that "should also work in XLSX", remember XLSX is semantic: ask whether it has a meaningful cell-level analog before adding logic to the Xlsx renderer.

## Templates and Designer

- `NetReporter.Templates` parses YAML into `ReportDefinition` and supports JSON Path bindings (`$.client.name`), template strings (`{{ pageNumber }}`, `{{ $.title }}`, `{{ #group }}`, any DSL expression `{{ $.a * 2 : N2 }}`), `= expr` bindings (`BindingFactory`), `visible:` conditions, dynamic `style: "{{ … }}"`, and templated `fileName`/`title` resolved at bind time. Simple `$.path` placeholders keep a fast path that prints raw JSON text — keep that output stable.
- `YamlReportRewriter` does parse → modify POCO → re-serialize. **This drops comments**. If you change rewriter behavior, extend `YamlReportRewriterTests` (single-element methods) and `YamlReportRewriterMultiTests` (multi-element moves/deletes, clipboard copy/paste, image/barcode/chart patches, `UpdateSeries`). New schema fields must be nullable so `OmitNull` serialization doesn't add defaults to users' YAML.
- The Designer renders the **SVG** preview live via HTMX, with Alpine.js for state. Drag/resize/create operations POST back to mutation endpoints (`/Home/MoveMany`, `/Home/Update`, `/Home/Add`, `/Home/DeleteMany`, `/Home/Copy`, `/Home/Paste`, `/Home/UpdateSeries`, etc.) which call into `YamlReportRewriter`.
- Overlays carry two sets of coordinates: `data-x/y/w/h` = rendered bbox in page points (for hit-testing and visual feedback) and `data-bx/by/bw/bh` = the YAML bounds (band-relative). Anything that writes bounds back to YAML must apply deltas to the `b*` values (`yamlBounds(ds)`), never write page coordinates.
- Selection state: `selection` (array of `{path, kind}`) + `selected` (primary element for the drawer). Re-render classes with `renderSelectionClasses()`; after any server response call `applyServerResponse` (it also re-applies zoom).
- The preview supports zoom (25%–400%) via `transform: scale(var(--zoom))` on `[data-preview-root]`, with an outer `[data-zoom-spacer]` reserving the post-scaled width/height so the parent's `overflow-auto` shows scroll correctly. CSS `zoom` was tried first but breaks layout: it makes the scaled content report a larger size to flexbox, which pushes the preview panel into the YAML editor / toolbox.
- Mouse coords are compensated by `this.zoom` in drag/resize/create handlers — anything new that converts viewport-px to page-pt must do the same.
- The flex chain housing the preview (`<section class="flex-1 ...">` and `<div id="preview" class="flex-1 ...">`) needs **`min-w-0`** on each flex item. Without it, flexbox's default `min-width: auto` lets the scaled content grow the panel and invade the editor — `overflow-auto` is not enough by itself. If you add a new flex container in this chain, keep `min-w-0`.

## Packaging and release

- Every library under `src/` is a NuGet package (`NetReporter.*`) plus the `NetReporter` meta-package. Shared package metadata lives in `src/Directory.Build.props`; the root `Directory.Build.props` marks everything else `IsPackable=false`.
- The README embedded in the packages is `docs/nuget-readme.md`, not the GitHub README: nuget.org strips raw HTML (the centered `<div>` shows as text) and only renders images from trusted domains, so that file stays plain Markdown with absolute `raw.githubusercontent.com` URLs. Keep it in sync when the public API or install instructions change.
- Versions come from git tags, not from csproj files. `.github/workflows/publish.yml` runs on every push to `main`: it computes the next `vX.Y.Z` from the latest tag (patch by default, `[minor]` / `[major]` in a commit message bump those parts), builds, tests, packs, pushes to nuget.org with the `NUGET_API_KEY` secret, then tags the commit and creates a GitHub release. Local builds get version `1.0.0-local`.
- `NetReporter.Svg` references `SkiaSharp.NativeAssets.Linux` on purpose: the base `SkiaSharp` package only ships macOS/Windows natives for `net10.0`, and CI (and Linux consumers) need it.

## Scope discipline

- Known limitations are documented in the README "Known limitations" + "Roadmap" sections. Items marked roadmap-only (subreports, data-driven detail bands, more chart types, DSL `where`/user functions, etc.) are deliberately out of scope unless the user opens that work explicitly.
- Documented technical debt (approximate `EstimateTextMeasurer`, YAML rewriter dropping comments) is intentional — don't "fix" them as a side-quest. If a fix is needed, flag it and propose it.
- Output-affecting changes must be listed in the README "Behavior changes" section (EN + ES): this library renders production documents.
- New band kinds, new element types, or new renderers are **structural** changes — they touch IR, layout, and every renderer. Treat them as their own task with a clear scope, not as an addendum to a smaller change.

## When working in this repo

- Read the README before assuming a feature doesn't exist — the surface area is bigger than the original prototype scope.
- The samples in `samples/TemplateSample/*.yaml` are the best reference for YAML schema. `invoice-complete.yaml` exercises every Phase 1-4 feature.
- Tests live in `tests/`. There is no integration test for the Designer (it's a UI tool); manual smoke testing in the browser is expected for Designer changes.
