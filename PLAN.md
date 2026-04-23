# Plan de implementación — NetReporter Prototype Invoice

> **Propósito:** transcribir el spec `NetReporter-Prototype-Invoice.md` a código funcional y generar `factura.pdf`.
> **Diseñado para ser retomado tras interrupción.** Cada paso tiene criterio de "hecho" verificable. Ve al paso donde el último criterio de "hecho" falla.
> **Fuente de verdad del código:** `NetReporter-Prototype-Invoice.md` — el código en el spec es literal, transcríbelo tal cual salvo que esta guía indique un ajuste.

---

## 0. Estado inicial observado

- `NetReporter.slnx` existe y apunta únicamente a `NetReporter/NetReporter.csproj`.
- `NetReporter/NetReporter.csproj` es un placeholder (net10.0, sin código).
- No existen `src/` ni `samples/`.
- .NET 10.0.103 disponible.
- Formato de solución es `.slnx` (no clásico `.sln`). El spec usa comandos para `.sln` — ajuste requerido (ver §1).

---

## 1. Decisión de estructura de solución — resolver ANTES de tocar código

El spec §4.1 asume una `NetReporter.sln` clásica, pero aquí solo hay `NetReporter.slnx`. Dos caminos:

**Camino A (recomendado):** mantener `.slnx` y agregar proyectos con `dotnet sln NetReporter.slnx add <csproj>` (el CLI de .NET 10 lo soporta). Eliminar el placeholder `NetReporter/NetReporter.csproj` porque no corresponde a la estructura final.

**Camino B:** borrar `.slnx`, regenerar `NetReporter.sln` clásico y seguir el spec al pie. Más simple si hay fricción con `.slnx`.

**Criterio de "hecho":** `dotnet sln list` muestra los 3 proyectos finales (`NetReporter.Core`, `NetReporter.Pdf`, `InvoiceSample`) y no muestra el placeholder.

**⚠️ Confirma con el usuario el camino antes de borrar `NetReporter/NetReporter.csproj`** — es placeholder pero borrarlo es destructivo y cambia el layout del repo.

---

## 2. Scaffolding de proyectos

Desde `/Users/mchinchilla/RiderProjects/NetReporter/`:

```bash
# 2.1 Crear proyectos
dotnet new classlib -n NetReporter.Core -o src/NetReporter.Core -f net10.0
dotnet new classlib -n NetReporter.Pdf  -o src/NetReporter.Pdf  -f net10.0
dotnet new console  -n InvoiceSample    -o samples/InvoiceSample -f net10.0

# 2.2 Registrar en la solución (slnx)
dotnet sln NetReporter.slnx add src/NetReporter.Core/NetReporter.Core.csproj
dotnet sln NetReporter.slnx add src/NetReporter.Pdf/NetReporter.Pdf.csproj
dotnet sln NetReporter.slnx add samples/InvoiceSample/InvoiceSample.csproj

# 2.3 Referencias
dotnet add src/NetReporter.Pdf/NetReporter.Pdf.csproj reference src/NetReporter.Core/NetReporter.Core.csproj
dotnet add samples/InvoiceSample/InvoiceSample.csproj reference src/NetReporter.Core/NetReporter.Core.csproj
dotnet add samples/InvoiceSample/InvoiceSample.csproj reference src/NetReporter.Pdf/NetReporter.Pdf.csproj

# 2.4 Paquetes NuGet
dotnet add src/NetReporter.Pdf/NetReporter.Pdf.csproj package QuestPDF
dotnet add src/NetReporter.Pdf/NetReporter.Pdf.csproj package SkiaSharp
```

**Nota:** el spec también menciona que `InvoiceSample` necesitará `QuestPDF` por la línea `QuestPDF.Settings.License = ...` en `Program.cs`. Agregar también:

```bash
dotnet add samples/InvoiceSample/InvoiceSample.csproj package QuestPDF
```

### 2.5 Editar los 3 `.csproj` para añadir flags obligatorios

Dentro del `<PropertyGroup>` único:

```xml
<TargetFramework>net10.0</TargetFramework>
<LangVersion>latest</LangVersion>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

Para `InvoiceSample` asegurar además `<OutputType>Exe</OutputType>` (viene por defecto con `dotnet new console`).

**Criterio de "hecho":**
```bash
dotnet build
```
Compila los 3 proyectos (aunque estén vacíos) sin errores. Es OK que salgan warnings por archivos auto-generados por `dotnet new` que no hemos eliminado aún (se sobrescribirán en §3).

### 2.6 Limpieza de archivos auto-generados

`dotnet new` crea `Class1.cs` en cada classlib y `Program.cs` trivial en console. Borrar:
- `src/NetReporter.Core/Class1.cs`
- `src/NetReporter.Pdf/Class1.cs`
- `samples/InvoiceSample/Program.cs` (se reemplaza en §5)

---

## 3. Transcripción de `NetReporter.Core`

Orden de implementación importa por dependencias entre namespaces. Este es el orden correcto. Cada archivo se copia literal desde el spec (líneas indicadas).

**Regla:** después de cada sub-sección, ejecutar `dotnet build src/NetReporter.Core/NetReporter.Core.csproj`. Si falla, revisar only ese archivo contra el spec. No avanzar con errores.

### 3.1 Primitives (spec §5.1, líneas 180–302)
- `src/NetReporter.Core/Primitives/Geometry.cs` — spec líneas 182–234 (Point, Size, Rect, Thickness, Units).
- `src/NetReporter.Core/Primitives/Color.cs` — spec líneas 238–273 (Color con FromHex).
- `src/NetReporter.Core/Primitives/PageSetup.cs` — spec líneas 278–300 (PageOrientation, PageSetup, Letter/A4).

**Build check:** `dotnet build src/NetReporter.Core/NetReporter.Core.csproj` → OK.

### 3.2 Styles (spec §5.2, líneas 306–522)
Orden obligatorio por dependencias dentro del namespace:
- `src/NetReporter.Core/Styles/StyleTypes.cs` — líneas 309–348 (enums + BorderLine + BorderSet).
- `src/NetReporter.Core/Styles/Style.cs` — líneas 353–392 (Style, ResolvedStyle, StyleRef).
- `src/NetReporter.Core/Styles/StyleBuilder.cs` — líneas 397–436 (StyleBuilder, StyleSheetBuilder).
- `src/NetReporter.Core/Styles/StyleSheet.cs` — líneas 441–522 (StyleSheet con Resolve + cache + detección de ciclos).

**Build check:** OK.

### 3.3 DataBinding (spec §5.3, líneas 529–553)
- `src/NetReporter.Core/DataBinding/IDataBinding.cs` — completo (IDataBinding, FuncBinding, Bind static helper).

### 3.4 Expressions (spec §5.4, líneas 562–620)
- `src/NetReporter.Core/Expressions/IExpression.cs` — líneas 564–580 (IEvaluationContext, IExpression).
- `src/NetReporter.Core/Expressions/Expr.cs` — líneas 585–620 (static Expr + LiteralExpression + FuncExpression).

### 3.5 Elements (spec §5.5, líneas 627–722)
- `src/NetReporter.Core/Elements/ReportElement.cs` — líneas 629–641 (abstract record).
- `src/NetReporter.Core/Elements/TextElement.cs` — líneas 646–656.
- `src/NetReporter.Core/Elements/TableElement.cs` — líneas 661–690 (TableElement\<TRow\>, TableColumn\<TRow\>, TableHeaderMode).
- `src/NetReporter.Core/Elements/LineElement.cs` — líneas 695–707.
- `src/NetReporter.Core/Elements/RectangleElement.cs` — líneas 712–722.

### 3.6 Bands (spec §5.6, líneas 729–765)
- `src/NetReporter.Core/Bands/Bands.cs` — archivo único con todas las bandas.

### 3.7 Definition (spec §5.7, líneas 772–790)
- `src/NetReporter.Core/Definition/ReportDefinition.cs`.

### 3.8 RenderList (spec §5.8, líneas 797–847)
- `src/NetReporter.Core/RenderList/RenderCommand.cs` — líneas 799–825 (abstract record + 3 sealed subclases).
- `src/NetReporter.Core/RenderList/RenderPage.cs` — líneas 830–835.
- `src/NetReporter.Core/RenderList/RenderList.cs` — líneas 840–847.

### 3.9 Layout (spec §5.9, líneas 852–1336)
Este es el más largo y complejo. Hacerlo en este orden y compilar entre cada uno:
- `src/NetReporter.Core/Layout/TextMeasurer.cs` — líneas 873–892.
- `src/NetReporter.Core/Layout/LayoutContext.cs` — líneas 897–924 (LayoutContext + PageBuilder internos).
- `src/NetReporter.Core/Layout/LayoutEngine.cs` — líneas 929–1336. **El archivo más largo del proyecto** (≈400 líneas). Transcribir completo de una vez pero revisar visualmente el cierre de llaves al final.

**⚠️ Gotchas conocidos del LayoutEngine (documentados en spec):**
- Usa reflection UNA vez vía `MakeGenericMethod` para invocar `RenderTableElementGeneric<TRow>`. Es deliberado (deuda técnica §5.9 nota). No "arreglarlo".
- `LayoutDetailBand` tiene un comentario admitiendo que no soporta elementos no-tabla que crucen páginas. OK.
- `RenderTableElementGeneric` copia comandos de `currentPage` a `workingPage` con `ReferenceEquals` check — hack para compensar que `PageBuilder.PageNumber` es inmutable. OK para el prototipo.

**Build check final del Core:** `dotnet build src/NetReporter.Core/NetReporter.Core.csproj` debe estar limpio.

---

## 4. Transcripción de `NetReporter.Pdf`

### 4.1 PdfRenderer (spec §6, líneas 1347–1503)
- `src/NetReporter.Pdf/PdfRenderer.cs` — archivo único.

**Gotchas:**
- Los `using` arriba del archivo incluyen alias: `using RlRenderList = NetReporter.Core.RenderList.RenderList;` y `using QPdfColor = QuestPDF.Infrastructure.Color;`. El segundo no se usa en el código visible — evaluar eliminarlo solo si compilador marca warning de `unused using` (con `TreatWarningsAsErrors=true` esto fallaría el build).
- El renderer usa `SKCanvas`, `SKPaint`, `SKFont`, `SKTypeface` directo de SkiaSharp. QuestPDF solo provee `Document.Create` + `container.Canvas((canvas, space) => …)`.
- Método `ResolveTypeface` usa `style.FontFamily` (string) → `SKTypeface.FromFamilyName(...)` con fallback a `SKTypeface.Default`.

**Build check:** `dotnet build src/NetReporter.Pdf/NetReporter.Pdf.csproj` → OK.

---

## 5. Transcripción de `InvoiceSample`

### 5.1 Domain/Models.cs (spec §7, líneas 1521–1545)
- `samples/InvoiceSample/Domain/Models.cs` — Empresa, Cliente, Factura, LineaFactura.

### 5.2 Reports/ErpTheme.cs (spec §7, líneas 1549–1620)
- Clases `ErpColors`, `ErpStyles`, `ErpTheme.Default` (StyleSheet).

### 5.3 Reports/InvoiceReport.cs (spec §7, líneas 1624–1889)
- Método `Build(Factura)` + 5 builders privados.

**⚠️ Ajuste importante sobre `BuildReportFooter` (spec §7 nota honesta, línea 1891):**
El spec deja el footer incompleto con un `Expr.Of(ctx => ctx.GetParameter("subtotal") ...)` que no funciona porque nadie inyecta el parámetro. El fix correcto que indica el spec mismo es cerrar los valores en el closure:

```csharp
Content = Expr.Str(subtotal.ToString("N2", cultureHn))
```

Aplicar este fix al transcribir. Agregar también líneas para ISV y Total con el mismo patrón, ya que el spec dice "... (en el prototipo pasamos los valores pre-calculados como expresiones literales)" pero no las escribe. Plantilla:

```csharp
// Subtotal
new TextElement { Bounds = new Rect(380, 14, 100, 14), Content = Expr.Str("Subtotal:"), Style = ErpStyles.Label },
new TextElement { Bounds = new Rect(480, 14, 92, 14),  Content = Expr.Str(subtotal.ToString("N2", cultureHn)), Style = ErpStyles.Default with { TextAlign = TextAlignment.Right } ... },
// ISV 15%
new TextElement { Bounds = new Rect(380, 30, 100, 14), Content = Expr.Str("ISV 15%:"), Style = ErpStyles.Label },
new TextElement { Bounds = new Rect(480, 30, 92, 14),  Content = Expr.Str(isv.ToString("N2", cultureHn)), Style = ErpStyles.Default },
// Total
new TextElement { Bounds = new Rect(380, 48, 100, 14), Content = Expr.Str("Total:"), Style = ErpStyles.Value },
new TextElement { Bounds = new Rect(480, 48, 92, 14),  Content = Expr.Str(total.ToString("N2", cultureHn)), Style = ErpStyles.Value },
```

**Nota sobre `Style with { TextAlign = ... }`:** `ErpStyles.Default` es `StyleRef` (struct), no `ResolvedStyle`. `StyleRef` no tiene `TextAlign`. La alineación a la derecha debe lograrse creando un estilo nombrado nuevo en `ErpTheme` (ej. `TotalValue` con `Align(TextAlignment.Right)`) y referenciándolo con `new StyleRef("TotalValue")`. No intentar `StyleRef with { ... }`.

### 5.4 Program.cs (spec §7, líneas 1899–1945)
- Top-level statements.
- Incluir `QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;` como primera línea ejecutable.
- La ruta de salida sugerida es `AppContext.BaseDirectory` — estará en `samples/InvoiceSample/bin/Debug/net10.0/factura.pdf`.

**Build check final:** `dotnet build` desde la raíz → todo limpio.

---

## 6. Ejecutar y validar

```bash
dotnet run --project samples/InvoiceSample
```

### Resultado esperado (spec §8):
- `factura.pdf` en `samples/InvoiceSample/bin/Debug/net10.0/factura.pdf`.
- **1 página** con las 8 líneas del sample.
- Consola imprime:
  - `Factura generada: <ruta>`
  - `Páginas: 1`
  - `Comandos totales: <~80-150>`

### Validación visual (abrir el PDF):
- Título "FACTURA" grande arriba a la izquierda.
- Número de factura debajo del título.
- Datos del emisor a la derecha (nombre, RTN, dirección, teléfono).
- Datos del cliente a la izquierda (Cliente/RTN/Dirección).
- Fecha y CAI a la derecha.
- Tabla de 5 columnas (Código, Descripción, Cant., P. Unit., Subtotal) con:
  - Header en fondo gris claro.
  - Filas alternadas (zebra: fondo Gray50 en impares).
  - Números alineados a la derecha.
  - Formato `N2` en decimales.
- Bloque de totales abajo a la derecha (Subtotal, ISV 15%, Total).
- Pie con "Página 1 de 1".

### Prueba de paginación (opcional pero recomendada):
Modificar `Program.cs` para generar 50 líneas con un loop. Al correr:
- Debe producir múltiples páginas.
- El header de la tabla debe repetirse en cada página (TableHeaderMode.RepeatOnPageBreak).
- El pie debe mostrar "Página N de M" correcto.

---

## 7. Contingencias comunes

| Síntoma | Causa probable | Fix |
|---|---|---|
| `TreatWarningsAsErrors` falla con "unused using QPdfColor" | Alias no usado en PdfRenderer.cs | Eliminar la línea `using QPdfColor = ...` |
| `SKFont { Size = ..., Typeface = ... }` no compila | API SkiaSharp puede diferir | Usar el ctor `new SKFont(typeface, size)` |
| QuestPDF rechaza documento por licencia | Falta `Settings.License = Community` antes del primer render | Asegurar que está en la PRIMERA línea ejecutable de `Program.cs` |
| Texto sale cortado / mal alineado | `TextMeasurer.EstimateWidth` subestima ancho | Esperado en prototipo; la alineación manual en `PdfRenderer.DrawText` sí usa `font.MeasureText` real |
| Paginación rompe en tablas largas | Bug en `RenderTableElementGeneric` al sincronizar `workingPage` | Revisar el bloque `if (!ReferenceEquals(currentPage, workingPage))` — la transferencia de `PageNumber` no es completa (deuda técnica) |
| `StyleRef with { ... }` no compila | `StyleRef` no tiene ese campo | Crear un estilo nombrado nuevo en `ErpTheme` |
| `TableElement<object>` warning o error | Varianza — ver spec §5.9 línea 1030 `if (element is TableElement<object>)` | Dejar el código como está; el chequeo por `GetType().IsGenericType` es el camino real |

---

## 8. Orden canónico si hay que reempezar todo

Checklist de alto nivel (cada ítem es verificable con `dotnet build`):

- [ ] §1 estructura de solución resuelta (slnx con 3 proyectos reales, placeholder eliminado).
- [ ] §2 scaffolding + paquetes + referencias.
- [ ] §3.1 Primitives compila.
- [ ] §3.2 Styles compila.
- [ ] §3.3 DataBinding compila.
- [ ] §3.4 Expressions compila.
- [ ] §3.5 Elements compila.
- [ ] §3.6 Bands compila.
- [ ] §3.7 Definition compila.
- [ ] §3.8 RenderList compila.
- [ ] §3.9 Layout compila (el archivo grande).
- [ ] §4 PdfRenderer compila.
- [ ] §5 InvoiceSample compila.
- [ ] §6 `dotnet run` genera `factura.pdf` abrible.
- [ ] §6 validación visual OK.
- [ ] §6 prueba de paginación con 50 líneas OK.

---

## 9. Qué NO hacer durante la transcripción

Ver también `CLAUDE.md`. Reglas duras:

1. **No usar QuestPDF fuera de `container.Canvas(...)` en el PdfRenderer.** Nada de `container.Row`/`Column`/`Table`.
2. **No introducir reflection** más allá de la única llamada a `MakeGenericMethod` ya presente en `LayoutEngine`.
3. **No "arreglar" la deuda técnica listada en spec §9** — es deliberada.
4. **No agregar features fuera de alcance del prototipo**: grupos, subtotales agregados, KeepTogether, images, QR, charts, auto-height, word-wrap real, DSL de expresiones, HTML/XLSX renderer. Todos son Fase 2.
5. **No cambiar el layout de carpetas** del spec §3 sin actualizar este plan.
6. **No cambiar `TargetFramework`** por debajo de net10.0.
7. **No commit ni push sin que el usuario lo pida** explícitamente.

---

## 10. Siguientes pasos tras el prototipo (spec §10) — fuera de alcance de este plan

Solo para contexto, no ejecutar salvo que el usuario lo pida:
1. Probar con 50+ líneas.
2. Cambiar colores de `ErpTheme` y validar propagación.
3. Escribir golden test (serializar `RenderList` a JSON y comparar).
4. Fase 2.1: grupos + subtotales.
5. Fase 2.2: HTML renderer sobre el mismo `RenderList`.
6. Fase 2.3: visor Blazor.
7. Fase 2.4: specs del Markdown extendido.

---

**Última posición segura antes de interrupción:** si reanudas, corre el checklist §8 de arriba abajo y retoma en el primer ítem sin marcar.
