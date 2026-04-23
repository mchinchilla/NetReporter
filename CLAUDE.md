# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository status

This repo is in **prototype phase**: the specification is written but most code has not been implemented yet. The authoritative design document is `NetReporter-Prototype-Invoice.md` (≈2000 lines in Spanish) — read it before making architectural decisions. It contains the full intended source for every file, ready to transcribe.

The current tree contains only:
- `NetReporter-Prototype-Invoice.md` — the prototype spec (full source of every file to create).
- `NetReporter.slnx` — XML-format solution referencing a single placeholder project.
- `NetReporter/NetReporter.csproj` — placeholder class library (net10.0), empty, to be replaced by the structure described in the spec.

The target layout (per spec §3) is:
```
src/NetReporter.Core/    — IR, styles, data binding, expressions, elements, bands, layout engine, RenderList
src/NetReporter.Pdf/     — PdfRenderer using QuestPDF Canvas + SkiaSharp
samples/InvoiceSample/   — Invoice demo (console app)
```

## Toolchain

- **.NET 10** (`dotnet --version` → 10.0.103 on this machine). Do not downgrade `<TargetFramework>`.
- Solution uses the newer `.slnx` format. The spec still uses `dotnet sln add` against a classic `.sln`; either convert or operate on `.slnx` directly.
- All csproj files must include: `<LangVersion>latest</LangVersion>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

## Build / run

```bash
# Once the structure exists:
dotnet build
dotnet run --project samples/InvoiceSample
# Output: samples/InvoiceSample/bin/Debug/net10.0/factura.pdf
```

QuestPDF license must be activated in `Program.cs`:
```csharp
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
```

There is no test suite yet. The spec explicitly defers tests until after the prototype renders a correct invoice; validation is visual inspection of the generated PDF.

## Architecture — the non-negotiable invariants

The pipeline is **IR → LayoutEngine → RenderList → Renderer**. Each stage is a distinct module with a clean boundary. Do not collapse stages for convenience.

1. **QuestPDF is used only as primitives**, never as a layout engine. All positioning is done by `LayoutEngine`, which emits absolute `DrawText/DrawLine/DrawRectangle` commands into `RenderList`. `PdfRenderer` consumes those commands via `container.Canvas((canvas, space) => ...)` and draws with `SkiaSharp` directly. This is what lets future HTML/XLSX renderers consume the same `RenderList`. If you find yourself calling `container.Row`/`container.Table`/`container.Column` in the renderer, you are doing it wrong.

2. **No reflection in hot paths.** Data binding is `IDataBinding<TRow,TValue>` wrapping a typed `Func<TRow,TValue>`. Expressions are `IExpression<T>` with `Func<IEvaluationContext,T>`. Never do `row.GetType()` or `PropertyInfo.GetValue`. The one known exception is `LayoutEngine.RenderTableElement` using `MakeGenericMethod` once per table type — this is tracked as tech debt (spec §5.9 nota, §9 "deuda técnica") and the fix is a visitor pattern, not more reflection.

3. **No CSS / Tailwind.** Styling is the native `StyleSheet` with `StyleRef` (a struct for zero-alloc passing) and named styles resolved through `BasedOn` inheritance, capped at 16 levels. `ResolvedStyle` is what renderers see — no nullables.

4. **`TableElement<TRow>` is generic on purpose** so `IDataBinding<TRow,…>` stays typed and JIT-inlinable. Do not propose an `object`-typed version.

5. **`RenderCommand` is an `abstract record` with sealed subclasses** (`DrawTextCommand`, `DrawLineCommand`, `DrawRectangleCommand`). C# discriminated unions aren't available yet; use exhaustive pattern matching.

6. **Bands have fixed `Height` in the prototype.** Auto-height / word-wrap / KeepTogether / groups with subtotals are explicitly out of scope (spec §9). Do not add them to the prototype; they belong in Phase 2.

## Scope discipline

When working from the spec, the task is almost always **transcribe**, not redesign. The spec's code is intentionally detailed because it doubles as the implementation. If you want to change something:

- Structural changes (new capability, new band kind, new renderer) belong in Phase 2 (spec §10). Flag them; don't smuggle them in.
- Known rough edges (listed in spec §9 "deuda técnica") are deliberate prototype trade-offs. Don't "fix" them unless asked — the user has signed off on them.
- `BuildReportFooter` in `InvoiceReport.cs` is incomplete in the spec on purpose; totals should be closed over from the `Build(Factura)` scope, not read via `ctx.GetParameter`. The spec documents this in §7 nota honesta.

## References inside the spec

When asked about a specific component, these are the canonical sections:
- Primitives (Point/Size/Rect/Color/PageSetup): §5.1
- Styles and resolution algorithm: §5.2 (note the ConcurrentDictionary cache + cycle detection)
- Data binding / expressions: §5.3–5.4
- Elements (Text/Table/Line/Rectangle): §5.5
- Bands (ReportHeader, PageHeader, Detail, PageFooter, ReportFooter): §5.6
- LayoutEngine algorithm + pagination + table split with header repeat: §5.9
- PdfRenderer (SkiaSharp + QuestPDF Canvas): §6
- Invoice sample + ErpTheme: §7
