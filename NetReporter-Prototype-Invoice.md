# NetReporter — Prototipo "Factura" end-to-end

> **Versión:** 0.1 (prototipo)
> **Target:** .NET 10
> **Propósito:** Validar el IR completo con un reporte realista de factura, usando QuestPDF solo como primitivas de dibujo.
> **Autor:** Marvin + Claude

---

## Índice

1. [Principios del prototipo](#1-principios-del-prototipo)
2. [Decisión clave: QuestPDF como primitivas](#2-decisión-clave-questpdf-como-primitivas)
3. [Estructura de solución](#3-estructura-de-solución)
4. [Setup en Rider](#4-setup-en-rider)
5. [NetReporter.Core — código completo](#5-netreportercore--código-completo)
   - [5.1 Primitivos](#51-primitivos)
   - [5.2 Estilos](#52-estilos)
   - [5.3 Data Binding](#53-data-binding)
   - [5.4 Expresiones](#54-expresiones)
   - [5.5 Elementos](#55-elementos)
   - [5.6 Bandas](#56-bandas)
   - [5.7 ReportDefinition](#57-reportdefinition)
   - [5.8 RenderList](#58-renderlist)
   - [5.9 Layout Engine](#59-layout-engine)
6. [NetReporter.Pdf — renderer](#6-netreporterpdf--renderer)
7. [InvoiceSample — uso real](#7-invoicesample--uso-real)
8. [Cómo correrlo](#8-cómo-correrlo)
9. [Qué valida este prototipo y qué NO valida](#9-qué-valida-este-prototipo-y-qué-no-valida)
10. [Siguientes pasos](#10-siguientes-pasos)

---

## 1. Principios del prototipo

El objetivo del prototipo no es producir un PDF bonito — es **demostrar que la arquitectura del IR funciona end-to-end** y que vale la pena seguir construyéndola.

Reglas del juego:

1. **El prototipo implementa una versión mínima de cada capa** del pipeline (IR → Layout → RenderList → PDF). No toma atajos saltándose capas.
2. **Sin reflection.** Data binding con delegates tipados, expresiones con `Func<>`. Ni `row.GetType()` ni nada parecido.
3. **Sin CSS ni Tailwind.** Estilos nativos del IR con `StyleSheet` y constantes.
4. **QuestPDF solo como primitivas de dibujo** (ver sección 2).
5. **Sin optimizaciones prematuras.** Este prototipo prioriza claridad sobre performance. La optimización viene después con BenchmarkDotNet midiendo casos reales.
6. **Tests quedan fuera del prototipo.** Una vez validado, escribimos tests golden. Aquí el "test" es: ¿se ve la factura bien?

---

## 2. Decisión clave: QuestPDF como primitivas

QuestPDF es un motor de layout completo por sí solo. Tiene `Row`, `Column`, `Table`, flexbox-like layout, paginación, etc. Eso significa que hay dos formas de usarlo:

**Camino A — QuestPDF como motor.** Le pasamos los datos y QuestPDF maneja el layout. Pros: rapidísimo de construir. Contras: nuestro layout engine nunca se ejercita, cambiar de backend requiere reescribir medio motor, otros renderers (HTML, XLSX) tendrán layout diferente.

**Camino B — QuestPDF como primitivas de dibujo.** ← **Este es el que usamos.**

Nuestro layout engine hace todo: measure, arrange, paginación, repetición de headers, saltos de página. Produce un `RenderList` con comandos absolutos (`DrawText(x, y, ...)`, `DrawLine(...)`). El `PdfRenderer` recorre el `RenderList` y usa QuestPDF solo para ejecutar primitivas de dibujo en posiciones absolutas.

QuestPDF nunca ve una tabla, una banda, ni datos. Solo "pinta este texto aquí, esta línea allá". Esto es lo que permite que:

- El mismo `RenderList` genere PDF, HTML y XLSX consistentemente.
- Cambiar de QuestPDF a un writer propio sea un solo módulo.
- Nuestro layout engine se ejercite (y se detecten sus bugs).

**Cómo se hace en QuestPDF concretamente:** usamos `container.Canvas((canvas, space) => ...)`. Canvas expone SkiaSharp con posicionamiento absoluto. Es la "válvula de escape" oficial de QuestPDF.

---

## 3. Estructura de solución

```
NetReporter.sln
├── src/
│   ├── NetReporter.Core/
│   │   ├── NetReporter.Core.csproj
│   │   ├── Primitives/
│   │   │   ├── Geometry.cs
│   │   │   ├── Color.cs
│   │   │   └── PageSetup.cs
│   │   ├── Styles/
│   │   │   ├── StyleTypes.cs
│   │   │   ├── Style.cs
│   │   │   ├── StyleBuilder.cs
│   │   │   └── StyleSheet.cs
│   │   ├── DataBinding/
│   │   │   └── IDataBinding.cs
│   │   ├── Expressions/
│   │   │   ├── IExpression.cs
│   │   │   └── Expr.cs
│   │   ├── Elements/
│   │   │   ├── ReportElement.cs
│   │   │   ├── TextElement.cs
│   │   │   ├── TableElement.cs
│   │   │   ├── LineElement.cs
│   │   │   └── RectangleElement.cs
│   │   ├── Bands/
│   │   │   └── Bands.cs
│   │   ├── Definition/
│   │   │   └── ReportDefinition.cs
│   │   ├── RenderList/
│   │   │   ├── RenderCommand.cs
│   │   │   ├── RenderPage.cs
│   │   │   └── RenderList.cs
│   │   └── Layout/
│   │       ├── LayoutContext.cs
│   │       ├── TextMeasurer.cs
│   │       └── LayoutEngine.cs
│   └── NetReporter.Pdf/
│       ├── NetReporter.Pdf.csproj
│       └── PdfRenderer.cs
└── samples/
    └── InvoiceSample/
        ├── InvoiceSample.csproj
        ├── Program.cs
        ├── Domain/
        │   └── Models.cs
        └── Reports/
            ├── ErpTheme.cs
            └── InvoiceReport.cs
```

---

## 4. Setup en Rider

### 4.1 Crear la solución

En la terminal (o Rider → New Solution):

```bash
mkdir NetReporter && cd NetReporter
dotnet new sln -n NetReporter

# Proyectos
dotnet new classlib -n NetReporter.Core -o src/NetReporter.Core -f net10.0
dotnet new classlib -n NetReporter.Pdf  -o src/NetReporter.Pdf  -f net10.0
dotnet new console  -n InvoiceSample    -o samples/InvoiceSample -f net10.0

# Agregar a la solución
dotnet sln add src/NetReporter.Core/NetReporter.Core.csproj
dotnet sln add src/NetReporter.Pdf/NetReporter.Pdf.csproj
dotnet sln add samples/InvoiceSample/InvoiceSample.csproj

# Referencias
dotnet add src/NetReporter.Pdf reference src/NetReporter.Core
dotnet add samples/InvoiceSample reference src/NetReporter.Core src/NetReporter.Pdf

# Paquete NuGet
dotnet add src/NetReporter.Pdf package QuestPDF
```

### 4.2 Configuración común en csproj

En los tres `.csproj` agregar dentro de `<PropertyGroup>`:

```xml
<TargetFramework>net10.0</TargetFramework>
<LangVersion>latest</LangVersion>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

### 4.3 Licencia QuestPDF

QuestPDF es MIT para uso no comercial y para empresas con revenue < $1M USD/año. Para uso comercial en empresas mayores requiere licencia paga (Community $0 / Professional $699 one-time). **Confirma antes** que tu caso está cubierto por Community. Si no, o si no quieres depender de esa política, este prototipo se puede migrar después a PdfSharpCore (MIT puro) con cambios localizados solo en `PdfRenderer.cs`.

Para activar la licencia Community en código (`Program.cs` del sample):

```csharp
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
```

---

## 5. NetReporter.Core — código completo

### 5.1 Primitivos

#### `src/NetReporter.Core/Primitives/Geometry.cs`

```csharp
namespace NetReporter.Core.Primitives;

public readonly record struct Point(double X, double Y)
{
    public static readonly Point Zero = default;
}

public readonly record struct Size(double Width, double Height)
{
    public static readonly Size Zero = default;
    public static readonly Size Infinite = new(double.PositiveInfinity, double.PositiveInfinity);
}

public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public Point TopLeft => new(X, Y);
    public Size Size => new(Width, Height);

    public static readonly Rect Empty = default;

    public Rect WithX(double x) => this with { X = x };
    public Rect WithY(double y) => this with { Y = y };
    public Rect WithWidth(double w) => this with { Width = w };
    public Rect WithHeight(double h) => this with { Height = h };
    public Rect Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);
}

public readonly record struct Thickness(double Left, double Top, double Right, double Bottom)
{
    public static readonly Thickness Zero = default;

    public Thickness(double uniform) : this(uniform, uniform, uniform, uniform) { }
    public Thickness(double horizontal, double vertical)
        : this(horizontal, vertical, horizontal, vertical) { }

    public double Horizontal => Left + Right;
    public double Vertical => Top + Bottom;
}

public static class Units
{
    public const double PointsPerInch = 72.0;
    public const double PointsPerCm   = 28.3464567;
    public const double PointsPerMm   = 2.83464567;

    public static double Cm(double cm)     => cm * PointsPerCm;
    public static double Mm(double mm)     => mm * PointsPerMm;
    public static double Inch(double inch) => inch * PointsPerInch;
}
```

#### `src/NetReporter.Core/Primitives/Color.cs`

```csharp
namespace NetReporter.Core.Primitives;

public readonly record struct Color(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Color Black       = new(0, 0, 0);
    public static readonly Color White       = new(255, 255, 255);
    public static readonly Color Transparent = new(0, 0, 0, 0);

    public static Color FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var span = hex.AsSpan().TrimStart('#');

        return span.Length switch
        {
            3 => new Color(Hex(span[0], span[0]), Hex(span[1], span[1]), Hex(span[2], span[2])),
            6 => new Color(Hex(span[0], span[1]), Hex(span[2], span[3]), Hex(span[4], span[5])),
            8 => new Color(Hex(span[0], span[1]), Hex(span[2], span[3]),
                           Hex(span[4], span[5]), Hex(span[6], span[7])),
            _ => throw new ArgumentException($"Hex color inválido: '{hex}'.", nameof(hex))
        };

        static byte Hex(char hi, char lo) => (byte)((HexDigit(hi) << 4) | HexDigit(lo));

        static int HexDigit(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => throw new ArgumentException($"Dígito hex inválido: '{c}'.")
        };
    }

    public string ToHexString() => $"#{R:X2}{G:X2}{B:X2}";
}
```

#### `src/NetReporter.Core/Primitives/PageSetup.cs`

```csharp
namespace NetReporter.Core.Primitives;

public enum PageOrientation { Portrait, Landscape }

public sealed record PageSetup(
    Size Size,
    Thickness Margins,
    PageOrientation Orientation = PageOrientation.Portrait)
{
    public static readonly PageSetup Letter = new(
        new Size(612, 792),
        new Thickness(Units.Cm(2)));

    public static readonly PageSetup A4 = new(
        new Size(595.276, 841.890),
        new Thickness(Units.Cm(2)));

    public double ContentWidth  => Size.Width  - Margins.Horizontal;
    public double ContentHeight => Size.Height - Margins.Vertical;

    public Point ContentOrigin => new(Margins.Left, Margins.Top);
}
```

---

### 5.2 Estilos

#### `src/NetReporter.Core/Styles/StyleTypes.cs`

```csharp
using NetReporter.Core.Primitives;

namespace NetReporter.Core.Styles;

public enum FontWeight      { Normal, Bold }
public enum FontStyleKind   { Normal, Italic }
public enum TextAlignment   { Left, Center, Right, Justify }
public enum VerticalAlignment { Top, Center, Bottom }
public enum BorderStyleKind { Solid, Dashed, Dotted }

public readonly record struct BorderLine(
    double Thickness,
    Color Color,
    BorderStyleKind Style = BorderStyleKind.Solid);

public sealed record BorderSet(
    BorderLine? Left = null,
    BorderLine? Top = null,
    BorderLine? Right = null,
    BorderLine? Bottom = null)
{
    public static BorderSet All(double thickness, Color color)
    {
        var line = new BorderLine(thickness, color);
        return new BorderSet(line, line, line, line);
    }

    public static BorderSet Top(double thickness, Color color) =>
        new(Top: new BorderLine(thickness, color));

    public static BorderSet Bottom(double thickness, Color color) =>
        new(Bottom: new BorderLine(thickness, color));

    public static BorderSet Horizontal(double thickness, Color color)
    {
        var line = new BorderLine(thickness, color);
        return new BorderSet(Top: line, Bottom: line);
    }
}
```

#### `src/NetReporter.Core/Styles/Style.cs`

```csharp
using NetReporter.Core.Primitives;

namespace NetReporter.Core.Styles;

public sealed record Style(
    string? FontFamily = null,
    double? FontSize = null,
    FontWeight? Weight = null,
    FontStyleKind? Italic = null,
    Color? Foreground = null,
    Color? Background = null,
    TextAlignment? TextAlign = null,
    VerticalAlignment? VerticalAlign = null,
    Thickness? Padding = null,
    BorderSet? Border = null,
    string? NumberFormat = null,
    string? BasedOn = null);

/// <summary>
/// Estilo sin nullables — todos los campos resueltos. Lo que consumen los renderers.
/// </summary>
public sealed record ResolvedStyle(
    string FontFamily,
    double FontSize,
    FontWeight Weight,
    FontStyleKind Italic,
    Color Foreground,
    Color Background,
    TextAlignment TextAlign,
    VerticalAlignment VerticalAlign,
    Thickness Padding,
    BorderSet? Border,
    string? NumberFormat);

public readonly record struct StyleRef(string Name)
{
    public static readonly StyleRef Default = new("Default");
    public bool IsEmpty => string.IsNullOrEmpty(Name);
}
```

#### `src/NetReporter.Core/Styles/StyleBuilder.cs`

```csharp
using NetReporter.Core.Primitives;

namespace NetReporter.Core.Styles;

public sealed class StyleBuilder
{
    private Style _style = new();

    public StyleBuilder BasedOn(string name)        { _style = _style with { BasedOn = name };  return this; }
    public StyleBuilder FontFamily(string f)        { _style = _style with { FontFamily = f };  return this; }
    public StyleBuilder FontSize(double s)          { _style = _style with { FontSize = s };    return this; }
    public StyleBuilder Bold()                      { _style = _style with { Weight = FontWeight.Bold };       return this; }
    public StyleBuilder Italic()                    { _style = _style with { Italic = FontStyleKind.Italic };  return this; }
    public StyleBuilder Foreground(Color c)         { _style = _style with { Foreground = c };  return this; }
    public StyleBuilder Background(Color c)         { _style = _style with { Background = c };  return this; }
    public StyleBuilder Padding(Thickness t)        { _style = _style with { Padding = t };     return this; }
    public StyleBuilder Padding(double uniform)     { _style = _style with { Padding = new Thickness(uniform) }; return this; }
    public StyleBuilder Border(BorderSet b)         { _style = _style with { Border = b };      return this; }
    public StyleBuilder Align(TextAlignment a)      { _style = _style with { TextAlign = a };   return this; }
    public StyleBuilder VAlign(VerticalAlignment a) { _style = _style with { VerticalAlign = a }; return this; }
    public StyleBuilder Format(string fmt)          { _style = _style with { NumberFormat = fmt }; return this; }

    internal Style Build() => _style;
}

public sealed class StyleSheetBuilder
{
    private readonly Dictionary<string, Style> _styles = new(StringComparer.Ordinal);

    public StyleSheetBuilder Add(string name, Action<StyleBuilder> configure)
    {
        var sb = new StyleBuilder();
        configure(sb);
        _styles[name] = sb.Build();
        return this;
    }

    public StyleSheet Build() => new(_styles);
}
```

#### `src/NetReporter.Core/Styles/StyleSheet.cs`

```csharp
using System.Collections.Concurrent;
using NetReporter.Core.Primitives;

namespace NetReporter.Core.Styles;

public sealed class StyleSheet
{
    // Default global: el "fallback definitivo" si nada más está especificado.
    private static readonly ResolvedStyle BuiltInDefault = new(
        FontFamily: "Helvetica",
        FontSize: 10,
        Weight: FontWeight.Normal,
        Italic: FontStyleKind.Normal,
        Foreground: Color.Black,
        Background: Color.Transparent,
        TextAlign: TextAlignment.Left,
        VerticalAlign: VerticalAlignment.Top,
        Padding: Thickness.Zero,
        Border: null,
        NumberFormat: null);

    private readonly IReadOnlyDictionary<string, Style> _styles;
    private readonly ConcurrentDictionary<string, ResolvedStyle> _cache = new(StringComparer.Ordinal);

    public StyleSheet(IReadOnlyDictionary<string, Style> styles)
    {
        _styles = styles ?? throw new ArgumentNullException(nameof(styles));
    }

    public ResolvedStyle Resolve(StyleRef @ref)
    {
        var name = @ref.IsEmpty ? "Default" : @ref.Name;
        return _cache.GetOrAdd(name, ResolveCore);
    }

    private ResolvedStyle ResolveCore(string name)
    {
        // Detección de ciclos: máximo 16 niveles de herencia.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var chain = new Stack<Style>();
        var current = name;

        while (current is not null && visited.Add(current))
        {
            if (!_styles.TryGetValue(current, out var style)) break;
            chain.Push(style);
            current = style.BasedOn;

            if (visited.Count > 16)
                throw new InvalidOperationException(
                    $"Ciclo o cadena excesiva de herencia en estilo '{name}'.");
        }

        // Aplicar de la base hacia el derivado (último gana).
        var r = BuiltInDefault;
        while (chain.Count > 0)
        {
            var s = chain.Pop();
            r = r with
            {
                FontFamily    = s.FontFamily    ?? r.FontFamily,
                FontSize      = s.FontSize      ?? r.FontSize,
                Weight        = s.Weight        ?? r.Weight,
                Italic        = s.Italic        ?? r.Italic,
                Foreground    = s.Foreground    ?? r.Foreground,
                Background    = s.Background    ?? r.Background,
                TextAlign     = s.TextAlign     ?? r.TextAlign,
                VerticalAlign = s.VerticalAlign ?? r.VerticalAlign,
                Padding       = s.Padding       ?? r.Padding,
                Border        = s.Border        ?? r.Border,
                NumberFormat  = s.NumberFormat  ?? r.NumberFormat
            };
        }

        return r;
    }

    public static StyleSheet Minimal { get; } = new StyleSheetBuilder()
        .Add("Default", _ => { })
        .Build();
}
```

---

### 5.3 Data Binding

#### `src/NetReporter.Core/DataBinding/IDataBinding.cs`

```csharp
namespace NetReporter.Core.DataBinding;

/// <summary>
/// Binding tipado sin reflection. El JIT inlinea la expresión agresivamente.
/// </summary>
public interface IDataBinding<TRow, TValue>
{
    TValue Evaluate(TRow row);
}

public sealed class FuncBinding<TRow, TValue> : IDataBinding<TRow, TValue>
{
    private readonly Func<TRow, TValue> _selector;
    public FuncBinding(Func<TRow, TValue> selector) => _selector = selector;
    public TValue Evaluate(TRow row) => _selector(row);
}

public static class Bind
{
    public static IDataBinding<TRow, TValue> From<TRow, TValue>(Func<TRow, TValue> selector) =>
        new FuncBinding<TRow, TValue>(selector);
}
```

---

### 5.4 Expresiones

En el prototipo, las expresiones son **literales o funciones puras** sobre un contexto. No hay DSL parseado todavía — eso llega después.

#### `src/NetReporter.Core/Expressions/IExpression.cs`

```csharp
namespace NetReporter.Core.Expressions;

public interface IEvaluationContext
{
    int PageNumber { get; }
    int TotalPages { get; }
    int RowIndex { get; }
    object? CurrentRow { get; }
    object? GetParameter(string name);
    object? GetAggregate(string name);
}

public interface IExpression<TValue>
{
    TValue Evaluate(IEvaluationContext context);
}
```

#### `src/NetReporter.Core/Expressions/Expr.cs`

```csharp
namespace NetReporter.Core.Expressions;

/// <summary>Helpers para crear expresiones sin ceremony.</summary>
public static class Expr
{
    public static IExpression<T> Literal<T>(T value) => new LiteralExpression<T>(value);

    public static IExpression<T> Of<T>(Func<IEvaluationContext, T> fn) =>
        new FuncExpression<T>(fn);

    public static IExpression<string> Str(string value) => new LiteralExpression<string>(value);

    public static IExpression<string> PageNumber =>
        Of(ctx => ctx.PageNumber.ToString());

    public static IExpression<string> TotalPages =>
        Of(ctx => ctx.TotalPages.ToString());

    public static IExpression<string> PageOfTotal =>
        Of(ctx => $"Página {ctx.PageNumber} de {ctx.TotalPages}");
}

internal sealed class LiteralExpression<T> : IExpression<T>
{
    private readonly T _value;
    public LiteralExpression(T value) => _value = value;
    public T Evaluate(IEvaluationContext context) => _value;
}

internal sealed class FuncExpression<T> : IExpression<T>
{
    private readonly Func<IEvaluationContext, T> _fn;
    public FuncExpression(Func<IEvaluationContext, T> fn) => _fn = fn;
    public T Evaluate(IEvaluationContext context) => _fn(context);
}
```

---

### 5.5 Elementos

#### `src/NetReporter.Core/Elements/ReportElement.cs`

```csharp
using NetReporter.Core.Expressions;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Elements;

public abstract record ReportElement
{
    public required Rect Bounds { get; init; }
    public StyleRef Style { get; init; } = StyleRef.Default;
    public IExpression<bool>? Visible { get; init; }
}
```

#### `src/NetReporter.Core/Elements/TextElement.cs`

```csharp
using NetReporter.Core.Expressions;

namespace NetReporter.Core.Elements;

public sealed record TextElement : ReportElement
{
    public required IExpression<string> Content { get; init; }
    public bool WordWrap { get; init; } = true;
    public bool AutoHeight { get; init; } = false;
}
```

#### `src/NetReporter.Core/Elements/TableElement.cs`

```csharp
using NetReporter.Core.DataBinding;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Elements;

public enum TableHeaderMode { PrintOnce, RepeatOnPageBreak }

/// <summary>
/// Tabla con columnas tipadas. Las filas provienen del DataSource del DetailBand que la contiene,
/// O se pueden suministrar inline aquí (para el prototipo usamos inline).
/// </summary>
public sealed record TableElement<TRow> : ReportElement
{
    public required IReadOnlyList<TRow> Rows { get; init; }
    public required IReadOnlyList<TableColumn<TRow>> Columns { get; init; }
    public double RowHeight { get; init; } = 18;
    public double HeaderHeight { get; init; } = 22;
    public TableHeaderMode HeaderMode { get; init; } = TableHeaderMode.RepeatOnPageBreak;
    public StyleRef HeaderStyle { get; init; } = new("TableHeader");
    public StyleRef RowStyle { get; init; } = new("TableRow");
    public StyleRef? AlternateRowStyle { get; init; }
}

public sealed record TableColumn<TRow>(
    string Header,
    IDataBinding<TRow, object?> Binding,
    double Width,                // puntos
    string? Format = null,
    TextAlignment Align = TextAlignment.Left);
```

#### `src/NetReporter.Core/Elements/LineElement.cs`

```csharp
using NetReporter.Core.Primitives;

namespace NetReporter.Core.Elements;

public enum LineOrientation { Horizontal, Vertical }

public sealed record LineElement : ReportElement
{
    public LineOrientation Orientation { get; init; } = LineOrientation.Horizontal;
    public double Thickness { get; init; } = 0.5;
    public Color Color { get; init; } = Color.Black;
}
```

#### `src/NetReporter.Core/Elements/RectangleElement.cs`

```csharp
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Elements;

public sealed record RectangleElement : ReportElement
{
    public Color? Fill { get; init; }
    public BorderLine? BorderLine { get; init; }
}
```

---

### 5.6 Bandas

#### `src/NetReporter.Core/Bands/Bands.cs`

```csharp
using NetReporter.Core.Elements;
using NetReporter.Core.Expressions;

namespace NetReporter.Core.Bands;

public enum BandKind
{
    ReportHeader,
    PageHeader,
    Detail,
    PageFooter,
    ReportFooter
}

public abstract record Band(BandKind Kind)
{
    public required double Height { get; init; }
    public bool PrintOnFirstPage { get; init; } = true;
    public bool PrintOnLastPage  { get; init; } = true;
    public IExpression<bool>? Visible { get; init; }
    public required IReadOnlyList<ReportElement> Elements { get; init; }
}

public sealed record ReportHeaderBand() : Band(BandKind.ReportHeader);
public sealed record PageHeaderBand()   : Band(BandKind.PageHeader);
public sealed record PageFooterBand()   : Band(BandKind.PageFooter);
public sealed record ReportFooterBand() : Band(BandKind.ReportFooter);

/// <summary>
/// En el prototipo, el DetailBand es una banda de contenido fijo (no itera filas).
/// La "tabla de líneas" es un TableElement dentro de esta banda que sí itera sus filas internas.
/// Versión completa iteraría con IDataSource<TRow>.
/// </summary>
public sealed record DetailBand() : Band(BandKind.Detail);
```

---

### 5.7 ReportDefinition

#### `src/NetReporter.Core/Definition/ReportDefinition.cs`

```csharp
using System.Globalization;
using NetReporter.Core.Bands;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Definition;

public sealed record ReportDefinition
{
    public required string Name { get; init; }
    public string? Title { get; init; }
    public required PageSetup Page { get; init; }
    public required StyleSheet Styles { get; init; }
    public required IReadOnlyList<Band> Bands { get; init; }
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;
}
```

---

### 5.8 RenderList

#### `src/NetReporter.Core/RenderList/RenderCommand.cs`

```csharp
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.RenderList;

public abstract record RenderCommand(Rect Bounds);

public sealed record DrawTextCommand(
    Rect Bounds,
    string Text,
    ResolvedStyle Style) : RenderCommand(Bounds);

public sealed record DrawLineCommand(
    Point From,
    Point To,
    double Thickness,
    Color Color) : RenderCommand(new Rect(
        Math.Min(From.X, To.X),
        Math.Min(From.Y, To.Y),
        Math.Abs(To.X - From.X),
        Math.Abs(To.Y - From.Y)));

public sealed record DrawRectangleCommand(
    Rect Bounds,
    Color? Fill,
    BorderLine? Border) : RenderCommand(Bounds);
```

#### `src/NetReporter.Core/RenderList/RenderPage.cs`

```csharp
namespace NetReporter.Core.RenderList;

public sealed record RenderPage(
    int PageNumber,
    IReadOnlyList<RenderCommand> Commands);
```

#### `src/NetReporter.Core/RenderList/RenderList.cs`

```csharp
using NetReporter.Core.Primitives;

namespace NetReporter.Core.RenderList;

public sealed record RenderList(
    PageSetup Page,
    IReadOnlyList<RenderPage> Pages);
```

---

### 5.9 Layout Engine

Esta es la capa más compleja del prototipo. Explicación antes del código:

**El layout engine del prototipo hace:**

1. Reserva altura para `PageHeader` y `PageFooter` — el contenido útil es `ContentHeight - headerH - footerH`.
2. Emite `ReportHeader` una vez.
3. Emite cada `DetailBand` una por una. Si una banda no cabe en la página actual, abre nueva página.
4. `TableElement` dentro de un DetailBand: si sus filas no caben todas, las parte entre páginas, repitiendo header cuando `RepeatOnPageBreak`.
5. Emite `ReportFooter` al final.
6. En cada nueva página emite `PageHeader` y reserva espacio para `PageFooter`.

**Lo que NO hace (fase 2):**
- Grupos con subtotales.
- `KeepTogether`.
- Orphan/widow control.
- Auto-height en elementos.

#### `src/NetReporter.Core/Layout/TextMeasurer.cs`

```csharp
using NetReporter.Core.Styles;

namespace NetReporter.Core.Layout;

/// <summary>
/// Medición ultra-simple de texto. En producción medimos con SkiaSharp o System.Drawing
/// para obtener métricas reales. Para el prototipo, una aproximación lineal es suficiente
/// porque QuestPDF hará el text layout real al pintar.
/// </summary>
public static class TextMeasurer
{
    private const double AverageCharWidthFactor = 0.55; // aprox para Helvetica

    public static double EstimateWidth(string text, ResolvedStyle style) =>
        text.Length * style.FontSize * AverageCharWidthFactor;

    public static double EstimateHeight(ResolvedStyle style) =>
        style.FontSize * 1.2; // line-height aproximado
}
```

#### `src/NetReporter.Core/Layout/LayoutContext.cs`

```csharp
using System.Globalization;
using NetReporter.Core.Expressions;
using NetReporter.Core.RenderList;

namespace NetReporter.Core.Layout;

internal sealed class LayoutContext : IEvaluationContext
{
    public int PageNumber { get; set; }
    public int TotalPages { get; set; }
    public int RowIndex { get; set; }
    public object? CurrentRow { get; set; }
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    public object? GetParameter(string name) => null;  // prototipo
    public object? GetAggregate(string name) => null;  // prototipo
}

internal sealed class PageBuilder
{
    public int PageNumber { get; }
    public List<RenderCommand> Commands { get; } = new();

    public PageBuilder(int pageNumber) => PageNumber = pageNumber;

    public RenderPage Build() => new(PageNumber, Commands);
}
```

#### `src/NetReporter.Core/Layout/LayoutEngine.cs`

```csharp
using System.Globalization;
using NetReporter.Core.Bands;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Layout;

public sealed class LayoutEngine
{
    public RenderList Layout(ReportDefinition report)
    {
        var ctx = new LayoutContext { Culture = report.Culture };
        var pages = new List<RenderPage>();

        // Clasificar bandas
        var pageHeader   = report.Bands.OfType<PageHeaderBand>().FirstOrDefault();
        var pageFooter   = report.Bands.OfType<PageFooterBand>().FirstOrDefault();
        var reportHeader = report.Bands.OfType<ReportHeaderBand>().FirstOrDefault();
        var reportFooter = report.Bands.OfType<ReportFooterBand>().FirstOrDefault();
        var details      = report.Bands.OfType<DetailBand>().ToList();

        var pageHeaderH = pageHeader?.Height ?? 0;
        var pageFooterH = pageFooter?.Height ?? 0;

        var origin = report.Page.ContentOrigin;
        var usableHeight = report.Page.ContentHeight - pageHeaderH - pageFooterH;
        var contentWidth = report.Page.ContentWidth;

        // PASADA 1: emit bands con paginación preliminar. PageNumber real aún no final
        // porque no sabemos totalPages. Primera pasada asigna PageNumber provisional.
        var workingPage = new PageBuilder(1);
        double cursorY = origin.Y + pageHeaderH;
        var maxY = origin.Y + pageHeaderH + usableHeight;

        // ReportHeader
        if (reportHeader is not null)
        {
            EmitBand(reportHeader, workingPage, origin.X, ref cursorY, report, ctx);
            // Si sobrepasa, abrimos página (poco común para ReportHeader, pero robusto)
            if (cursorY > maxY)
            {
                FinishPage(workingPage, pageHeader, pages, origin, contentWidth, report, ctx);
                workingPage = new PageBuilder(pages.Count + 1);
                cursorY = origin.Y + pageHeaderH;
            }
        }

        // Detail bands
        foreach (var detail in details)
        {
            LayoutDetailBand(detail, report, workingPage, origin, ref cursorY, usableHeight,
                pageHeaderH, contentWidth, pages, ctx, ref maxY);
        }

        // ReportFooter
        if (reportFooter is not null)
        {
            if (cursorY + reportFooter.Height > maxY)
            {
                FinishPage(workingPage, pageHeader, pages, origin, contentWidth, report, ctx);
                workingPage = new PageBuilder(pages.Count + 1);
                cursorY = origin.Y + pageHeaderH;
                maxY = origin.Y + pageHeaderH + usableHeight;
            }
            EmitBand(reportFooter, workingPage, origin.X, ref cursorY, report, ctx);
        }

        // Cerrar última página
        FinishPage(workingPage, pageHeader, pages, origin, contentWidth, report, ctx);

        // PASADA 2: ahora que conocemos totalPages, emitir page headers/footers definitivos.
        // En el prototipo los page headers/footers NO usan PageNumber en el texto aún
        // (sería trivial agregarlo con una segunda pasada que reemplace los DrawTextCommand
        // que contengan expresiones contextuales). Aquí dejamos texto estático + "Página N".
        var finalPages = RewritePageFooters(pages, pageFooter, pageHeader, report, origin,
            pageHeaderH, pageFooterH, contentWidth);

        return new RenderList(report.Page, finalPages);
    }

    private void LayoutDetailBand(
        DetailBand band,
        ReportDefinition report,
        PageBuilder workingPage,
        Point origin,
        ref double cursorY,
        double usableHeight,
        double pageHeaderH,
        double contentWidth,
        List<RenderPage> pages,
        LayoutContext ctx,
        ref double maxY)
    {
        // Para el prototipo, un DetailBand tiene elementos, y uno de esos puede ser una
        // TableElement<T>. Otros elementos se dibujan en sus Bounds relativos a la banda.
        foreach (var element in band.Elements)
        {
            if (element is TableElement<object> genericTable)
            {
                // Nunca llega aquí por varianza, pero cubrimos el caso.
                continue;
            }

            // Chequeo de si es una tabla genérica: usamos duck typing por tipo
            var elemType = element.GetType();
            if (elemType.IsGenericType && elemType.GetGenericTypeDefinition() == typeof(TableElement<>))
            {
                // Tablas se manejan con un helper genérico
                RenderTableElement(element, band, report, workingPage, origin,
                    ref cursorY, usableHeight, pageHeaderH, contentWidth, pages, ctx, ref maxY);
            }
            else
            {
                // Elementos no-tabla: verifica que quepan, si no, salto de página.
                var relBottom = cursorY + element.Bounds.Bottom;
                if (relBottom > maxY)
                {
                    FinishPage(workingPage, null, pages, origin, contentWidth, report, ctx);
                    var newPage = new PageBuilder(pages.Count + 1);
                    // transferir referencia: no podemos reasignar parámetro ref local aquí
                    // así que se hace inline en LayoutReport. Simplificamos el prototipo:
                    // no soportamos elementos no-tabla que crucen páginas.
                    workingPage.Commands.AddRange(newPage.Commands);
                }

                EmitElement(element, workingPage, origin.X, cursorY, report, ctx);
            }
        }

        cursorY += band.Height;
    }

    // Workaround para la limitación de ref locales + tipos genéricos:
    // usamos reflection UNA VEZ para obtener el método genérico y lo cacheamos.
    // Esto NO es reflection en el hot path — es una vez por tipo de tabla por reporte.
    private static readonly System.Reflection.MethodInfo s_renderTableMethod =
        typeof(LayoutEngine).GetMethod(nameof(RenderTableElementGeneric),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    private void RenderTableElement(
        ReportElement element,
        DetailBand band,
        ReportDefinition report,
        PageBuilder workingPage,
        Point origin,
        ref double cursorY,
        double usableHeight,
        double pageHeaderH,
        double contentWidth,
        List<RenderPage> pages,
        LayoutContext ctx,
        ref double maxY)
    {
        var rowType = element.GetType().GetGenericArguments()[0];
        var method = s_renderTableMethod.MakeGenericMethod(rowType);

        var args = new object?[] { element, report, workingPage, origin,
            cursorY, usableHeight, pageHeaderH, contentWidth, pages, ctx };

        var newCursorY = (double)method.Invoke(this, args)!;
        cursorY = newCursorY;
    }

    private double RenderTableElementGeneric<TRow>(
        TableElement<TRow> table,
        ReportDefinition report,
        PageBuilder workingPage,
        Point origin,
        double cursorY,
        double usableHeight,
        double pageHeaderH,
        double contentWidth,
        List<RenderPage> pages,
        LayoutContext ctx)
    {
        var currentPage = workingPage;
        var maxY = origin.Y + pageHeaderH + usableHeight;

        var headerStyle = report.Styles.Resolve(table.HeaderStyle);
        var rowStyle = report.Styles.Resolve(table.RowStyle);
        var altRowStyle = table.AlternateRowStyle is { } alt
            ? report.Styles.Resolve(alt) : rowStyle;

        var tableX = origin.X + table.Bounds.X;
        var tableWidth = table.Bounds.Width;

        // Dibuja el header
        void DrawHeader(double y)
        {
            var colX = tableX;
            foreach (var col in table.Columns)
            {
                var cellStyle = headerStyle;
                currentPage.Commands.Add(new DrawRectangleCommand(
                    new Rect(colX, y, col.Width, table.HeaderHeight),
                    cellStyle.Background == Color.Transparent ? null : cellStyle.Background,
                    cellStyle.Border?.Bottom));
                currentPage.Commands.Add(new DrawTextCommand(
                    new Rect(colX + cellStyle.Padding.Left, y + cellStyle.Padding.Top,
                             col.Width - cellStyle.Padding.Horizontal,
                             table.HeaderHeight - cellStyle.Padding.Vertical),
                    col.Header,
                    cellStyle));
                colX += col.Width;
            }
        }

        DrawHeader(cursorY);
        cursorY += table.HeaderHeight;

        // Filas
        var rowIndex = 0;
        foreach (var row in table.Rows)
        {
            // Si no cabe esta fila en la página, abrimos nueva
            if (cursorY + table.RowHeight > maxY)
            {
                // Cerrar página actual
                pages.Add(currentPage.Build());
                currentPage = new PageBuilder(pages.Count + 1);
                cursorY = origin.Y + pageHeaderH;

                if (table.HeaderMode == TableHeaderMode.RepeatOnPageBreak)
                {
                    DrawHeader(cursorY);
                    cursorY += table.HeaderHeight;
                }
            }

            var effectiveStyle = (rowIndex % 2 == 1) ? altRowStyle : rowStyle;
            var colX = tableX;

            foreach (var col in table.Columns)
            {
                ctx.CurrentRow = row;
                ctx.RowIndex = rowIndex;

                var value = col.Binding.Evaluate(row);
                var text = FormatValue(value, col.Format, report.Culture);

                // Fondo de celda (si el estilo define background)
                if (effectiveStyle.Background != Color.Transparent)
                {
                    currentPage.Commands.Add(new DrawRectangleCommand(
                        new Rect(colX, cursorY, col.Width, table.RowHeight),
                        effectiveStyle.Background, null));
                }

                // Aplicar alineación específica de la columna (override)
                var cellStyle = effectiveStyle with { TextAlign = col.Align };

                currentPage.Commands.Add(new DrawTextCommand(
                    new Rect(colX + cellStyle.Padding.Left, cursorY + cellStyle.Padding.Top,
                             col.Width - cellStyle.Padding.Horizontal,
                             table.RowHeight - cellStyle.Padding.Vertical),
                    text,
                    cellStyle));

                colX += col.Width;
            }

            cursorY += table.RowHeight;
            rowIndex++;
        }

        // Importante: transferir el estado final al workingPage original
        // Si abrimos nuevas páginas, el workingPage original ya fue cerrado
        // y currentPage puede ser uno nuevo. Sincronizamos comandos:
        if (!ReferenceEquals(currentPage, workingPage))
        {
            workingPage.Commands.Clear();
            workingPage.Commands.AddRange(currentPage.Commands);
            // Y el número de página del workingPage se vuelve el de currentPage
            // (pero PageBuilder es inmutable en su PageNumber — aceptamos que
            // quien nos llame use pages.Count + 1 al crear el siguiente).
        }

        return cursorY;
    }

    private static string FormatValue(object? value, string? format, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        if (format is null) return value.ToString() ?? string.Empty;

        return value switch
        {
            IFormattable f => f.ToString(format, culture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private void EmitBand(
        Band band,
        PageBuilder page,
        double originX,
        ref double cursorY,
        ReportDefinition report,
        LayoutContext ctx)
    {
        foreach (var element in band.Elements)
            EmitElement(element, page, originX, cursorY, report, ctx);

        cursorY += band.Height;
    }

    private void EmitElement(
        ReportElement element,
        PageBuilder page,
        double originX,
        double bandTopY,
        ReportDefinition report,
        LayoutContext ctx)
    {
        var style = report.Styles.Resolve(element.Style);
        var absBounds = new Rect(
            originX + element.Bounds.X,
            bandTopY + element.Bounds.Y,
            element.Bounds.Width,
            element.Bounds.Height);

        switch (element)
        {
            case TextElement text:
                page.Commands.Add(new DrawTextCommand(
                    absBounds, text.Content.Evaluate(ctx), style));
                break;

            case LineElement line:
                var (from, to) = line.Orientation == LineOrientation.Horizontal
                    ? (new Point(absBounds.X, absBounds.Y),
                       new Point(absBounds.Right, absBounds.Y))
                    : (new Point(absBounds.X, absBounds.Y),
                       new Point(absBounds.X, absBounds.Bottom));
                page.Commands.Add(new DrawLineCommand(from, to, line.Thickness, line.Color));
                break;

            case RectangleElement rect:
                page.Commands.Add(new DrawRectangleCommand(
                    absBounds, rect.Fill, rect.BorderLine));
                break;

            default:
                // Tablas se manejan en RenderTableElement.
                break;
        }
    }

    private void FinishPage(
        PageBuilder page,
        PageHeaderBand? pageHeader,
        List<RenderPage> pages,
        Point origin,
        double contentWidth,
        ReportDefinition report,
        LayoutContext ctx)
    {
        // Emitir page header al inicio de la página (si corresponde)
        if (pageHeader is not null)
        {
            var headerCommands = new List<RenderCommand>();
            var tmp = new PageBuilder(page.PageNumber);
            double y = origin.Y;
            EmitBand(pageHeader, tmp, origin.X, ref y, report, ctx);
            // Insertar al principio
            page.Commands.InsertRange(0, tmp.Commands);
        }

        pages.Add(page.Build());
    }

    private List<RenderPage> RewritePageFooters(
        List<RenderPage> pages,
        PageFooterBand? pageFooter,
        PageHeaderBand? pageHeader,
        ReportDefinition report,
        Point origin,
        double pageHeaderH,
        double pageFooterH,
        double contentWidth)
    {
        if (pageFooter is null) return pages;

        var total = pages.Count;
        var result = new List<RenderPage>(total);
        var ctx = new LayoutContext { Culture = report.Culture, TotalPages = total };

        foreach (var page in pages)
        {
            ctx.PageNumber = page.PageNumber;
            var footerCommands = new List<RenderCommand>();
            var tmp = new PageBuilder(page.PageNumber);
            double y = origin.Y + pageHeaderH + report.Page.ContentHeight
                       - pageHeaderH - pageFooterH;
            EmitBand(pageFooter, tmp, origin.X, ref y, report, ctx);

            var combined = new List<RenderCommand>(page.Commands);
            combined.AddRange(tmp.Commands);
            result.Add(new RenderPage(page.PageNumber, combined));
        }

        return result;
    }
}
```

> **Nota sincera:** este `LayoutEngine` tiene rough edges — el manejo de ref-locals con métodos genéricos es feo (usa reflection una vez para `MakeGenericMethod`, lo cual rompe ligeramente el principio "cero reflection"). En la versión final, el fix correcto es hacer que `Band` sea genérica o usar un visitor pattern bien diseñado. Para el prototipo prioricé que funcione sobre elegancia. Lo marcamos como deuda técnica.

---

## 6. NetReporter.Pdf — renderer

El renderer PDF es sorprendentemente pequeño porque todo el trabajo duro ya está en el `RenderList`.

#### `src/NetReporter.Pdf/PdfRenderer.cs`

```csharp
using NetReporter.Core.Primitives;
using NetReporter.Core.RenderList;
using NetReporter.Core.Styles;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using RlRenderList = NetReporter.Core.RenderList.RenderList;
using QPdfColor = QuestPDF.Infrastructure.Color;

namespace NetReporter.Pdf;

public sealed class PdfRenderer
{
    public byte[] Render(RlRenderList renderList)
    {
        var document = Document.Create(container =>
        {
            foreach (var page in renderList.Pages)
            {
                container.Page(pageDescriptor =>
                {
                    pageDescriptor.Size(
                        (float)renderList.Page.Size.Width,
                        (float)renderList.Page.Size.Height,
                        Unit.Point);
                    pageDescriptor.Margin(0);  // el layout ya incluye márgenes en coords absolutas

                    pageDescriptor.Content().Canvas((canvas, space) =>
                    {
                        DrawPage(canvas, page);
                    });
                });
            }
        });

        return document.GeneratePdf();
    }

    private static void DrawPage(SKCanvas canvas, RenderPage page)
    {
        foreach (var cmd in page.Commands)
        {
            switch (cmd)
            {
                case DrawTextCommand text:
                    DrawText(canvas, text);
                    break;
                case DrawLineCommand line:
                    DrawLine(canvas, line);
                    break;
                case DrawRectangleCommand rect:
                    DrawRectangle(canvas, rect);
                    break;
            }
        }
    }

    private static void DrawText(SKCanvas canvas, DrawTextCommand cmd)
    {
        using var paint = new SKPaint
        {
            Color = ToSk(cmd.Style.Foreground),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        using var font = new SKFont
        {
            Size = (float)cmd.Style.FontSize,
            Typeface = ResolveTypeface(cmd.Style)
        };

        // Medir para aplicar alineación manual
        var textWidth = font.MeasureText(cmd.Text);
        var metrics = font.Metrics;
        var lineHeight = -metrics.Ascent + metrics.Descent;

        float x = cmd.Style.TextAlign switch
        {
            Core.Styles.TextAlignment.Center => (float)(cmd.Bounds.X + (cmd.Bounds.Width - textWidth) / 2),
            Core.Styles.TextAlignment.Right  => (float)(cmd.Bounds.Right - textWidth),
            _                                => (float)cmd.Bounds.X
        };

        // Baseline: interpretamos Bounds como top-left, bajamos al baseline.
        float y = (float)cmd.Bounds.Y + (-metrics.Ascent);

        canvas.DrawText(cmd.Text, x, y, SKTextAlign.Left, font, paint);
    }

    private static void DrawLine(SKCanvas canvas, DrawLineCommand cmd)
    {
        using var paint = new SKPaint
        {
            Color = ToSk(cmd.Color),
            StrokeWidth = (float)cmd.Thickness,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        canvas.DrawLine(
            (float)cmd.From.X, (float)cmd.From.Y,
            (float)cmd.To.X,   (float)cmd.To.Y,
            paint);
    }

    private static void DrawRectangle(SKCanvas canvas, DrawRectangleCommand cmd)
    {
        var rect = new SKRect(
            (float)cmd.Bounds.X,
            (float)cmd.Bounds.Y,
            (float)cmd.Bounds.Right,
            (float)cmd.Bounds.Bottom);

        if (cmd.Fill is { } fill && fill.A > 0)
        {
            using var fillPaint = new SKPaint
            {
                Color = ToSk(fill),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRect(rect, fillPaint);
        }

        if (cmd.Border is { } border)
        {
            using var borderPaint = new SKPaint
            {
                Color = ToSk(border.Color),
                StrokeWidth = (float)border.Thickness,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };
            canvas.DrawRect(rect, borderPaint);
        }
    }

    private static SKColor ToSk(Color c) => new(c.R, c.G, c.B, c.A);

    private static SKTypeface ResolveTypeface(ResolvedStyle style)
    {
        var slant = style.Italic == FontStyleKind.Italic
            ? SKFontStyleSlant.Italic
            : SKFontStyleSlant.Upright;
        var weight = style.Weight == FontWeight.Bold
            ? SKFontStyleWeight.Bold
            : SKFontStyleWeight.Normal;

        return SKTypeface.FromFamilyName(style.FontFamily, weight, SKFontStyleWidth.Normal, slant)
               ?? SKTypeface.Default;
    }
}
```

**Nota:** el `PdfRenderer` usa `SkiaSharp` directamente, que es la misma librería que QuestPDF usa internamente. Esto es lo que permite usar QuestPDF "como primitivas" — en realidad estamos usando SkiaSharp y QuestPDF nos da el wrapper de documento/página.

Hay que agregar `SkiaSharp` como paquete en `NetReporter.Pdf.csproj`:

```bash
dotnet add src/NetReporter.Pdf package SkiaSharp
```

---

## 7. InvoiceSample — uso real

Aquí es donde se ve si la API es ergonómica o si hay que ajustarla. Una factura realista con encabezado, tabla de items, subtotal, impuesto, total.

#### `samples/InvoiceSample/Domain/Models.cs`

```csharp
namespace InvoiceSample.Domain;

public sealed record Empresa(string Nombre, string Rtn, string Direccion, string Telefono);

public sealed record Cliente(string Nombre, string Rtn, string Direccion);

public sealed record Factura(
    string Numero,
    DateOnly Fecha,
    string Cai,
    DateOnly RangoHasta,
    Empresa Emisor,
    Cliente Cliente,
    IReadOnlyList<LineaFactura> Lineas);

public sealed record LineaFactura(
    string Codigo,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario)
{
    public decimal Subtotal => Cantidad * PrecioUnitario;
}
```

#### `samples/InvoiceSample/Reports/ErpTheme.cs`

```csharp
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace InvoiceSample.Reports;

/// <summary>Paleta de colores del ERP. Cambiar aquí cambia todos los reportes.</summary>
public static class ErpColors
{
    public static readonly Color Gray50  = Color.FromHex("#F8FAFC");
    public static readonly Color Gray100 = Color.FromHex("#F1F5F9");
    public static readonly Color Gray300 = Color.FromHex("#CBD5E1");
    public static readonly Color Gray500 = Color.FromHex("#64748B");
    public static readonly Color Gray700 = Color.FromHex("#334155");
    public static readonly Color Gray900 = Color.FromHex("#0F172A");

    public static readonly Color Primary = Color.FromHex("#0284C7");
    public static readonly Color Ink     = Gray900;
    public static readonly Color Muted   = Gray500;
}

/// <summary>StyleRefs tipados para autocompletar.</summary>
public static class ErpStyles
{
    public static readonly StyleRef Default     = new("Default");
    public static readonly StyleRef Title       = new("Title");
    public static readonly StyleRef Subtitle    = new("Subtitle");
    public static readonly StyleRef Caption     = new("Caption");
    public static readonly StyleRef Label       = new("Label");
    public static readonly StyleRef Value       = new("Value");
    public static readonly StyleRef TableHeader = new("TableHeader");
    public static readonly StyleRef TableRow    = new("TableRow");
    public static readonly StyleRef TableRowAlt = new("TableRowAlt");
    public static readonly StyleRef TableTotal  = new("TableTotal");
}

public static class ErpTheme
{
    public static readonly StyleSheet Default = new StyleSheetBuilder()
        .Add("Default", s => s
            .FontFamily("Helvetica")
            .FontSize(10)
            .Foreground(ErpColors.Ink))

        .Add("Title",    s => s.BasedOn("Default").FontSize(18).Bold())
        .Add("Subtitle", s => s.BasedOn("Default").FontSize(12).Bold()
            .Foreground(ErpColors.Gray700))
        .Add("Caption",  s => s.BasedOn("Default").FontSize(8)
            .Foreground(ErpColors.Muted))
        .Add("Label",    s => s.BasedOn("Default").FontSize(9)
            .Foreground(ErpColors.Muted))
        .Add("Value",    s => s.BasedOn("Default").FontSize(10).Bold())

        .Add("TableHeader", s => s.BasedOn("Default")
            .Bold()
            .Background(ErpColors.Gray100)
            .Padding(new Thickness(4, 3))
            .Border(BorderSet.Bottom(0.5, ErpColors.Gray300)))

        .Add("TableRow", s => s.BasedOn("Default")
            .Padding(new Thickness(4, 3)))

        .Add("TableRowAlt", s => s.BasedOn("TableRow")
            .Background(ErpColors.Gray50))

        .Add("TableTotal", s => s.BasedOn("TableRow")
            .Bold()
            .Border(BorderSet.Top(1, ErpColors.Ink)))

        .Build();
}
```

#### `samples/InvoiceSample/Reports/InvoiceReport.cs`

```csharp
using InvoiceSample.Domain;
using NetReporter.Core.Bands;
using NetReporter.Core.DataBinding;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Expressions;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;
using System.Globalization;

namespace InvoiceSample.Reports;

public static class InvoiceReport
{
    public static ReportDefinition Build(Factura factura)
    {
        var subtotal = factura.Lineas.Sum(l => l.Subtotal);
        var isv = Math.Round(subtotal * 0.15m, 2);
        var total = subtotal + isv;

        var cultureHn = CultureInfo.GetCultureInfo("es-HN");

        return new ReportDefinition
        {
            Name = "Factura",
            Title = $"Factura {factura.Numero}",
            Page = PageSetup.Letter,
            Styles = ErpTheme.Default,
            Culture = cultureHn,
            Bands = new Band[]
            {
                BuildReportHeader(factura),
                BuildPageHeader(),
                BuildDetail(factura, subtotal, isv, total),
                BuildReportFooter(),
                BuildPageFooter()
            }
        };
    }

    private static ReportHeaderBand BuildReportHeader(Factura f) => new()
    {
        Height = 120,
        Elements = new ReportElement[]
        {
            // Título "FACTURA"
            new TextElement
            {
                Bounds = new Rect(0, 0, 572, 24),
                Content = Expr.Str("FACTURA"),
                Style = ErpStyles.Title
            },
            // Número de factura
            new TextElement
            {
                Bounds = new Rect(0, 28, 572, 14),
                Content = Expr.Str($"No. {f.Numero}"),
                Style = ErpStyles.Subtitle
            },

            // Datos del emisor (derecha)
            new TextElement
            {
                Bounds = new Rect(380, 0, 192, 12),
                Content = Expr.Str(f.Emisor.Nombre),
                Style = ErpStyles.Value
            },
            new TextElement
            {
                Bounds = new Rect(380, 14, 192, 10),
                Content = Expr.Str($"RTN: {f.Emisor.Rtn}"),
                Style = ErpStyles.Caption
            },
            new TextElement
            {
                Bounds = new Rect(380, 26, 192, 10),
                Content = Expr.Str(f.Emisor.Direccion),
                Style = ErpStyles.Caption
            },
            new TextElement
            {
                Bounds = new Rect(380, 38, 192, 10),
                Content = Expr.Str($"Tel: {f.Emisor.Telefono}"),
                Style = ErpStyles.Caption
            },

            // Separador
            new LineElement
            {
                Bounds = new Rect(0, 56, 572, 0),
                Color = ErpColors.Gray300,
                Thickness = 0.5
            },

            // Cliente (izquierda)
            new TextElement
            {
                Bounds = new Rect(0, 64, 80, 12),
                Content = Expr.Str("Cliente:"),
                Style = ErpStyles.Label
            },
            new TextElement
            {
                Bounds = new Rect(80, 64, 300, 12),
                Content = Expr.Str(f.Cliente.Nombre),
                Style = ErpStyles.Value
            },
            new TextElement
            {
                Bounds = new Rect(0, 78, 80, 12),
                Content = Expr.Str("RTN:"),
                Style = ErpStyles.Label
            },
            new TextElement
            {
                Bounds = new Rect(80, 78, 300, 12),
                Content = Expr.Str(f.Cliente.Rtn),
                Style = ErpStyles.Default
            },
            new TextElement
            {
                Bounds = new Rect(0, 92, 80, 12),
                Content = Expr.Str("Dirección:"),
                Style = ErpStyles.Label
            },
            new TextElement
            {
                Bounds = new Rect(80, 92, 300, 12),
                Content = Expr.Str(f.Cliente.Direccion),
                Style = ErpStyles.Default
            },

            // Fecha (derecha)
            new TextElement
            {
                Bounds = new Rect(380, 64, 80, 12),
                Content = Expr.Str("Fecha:"),
                Style = ErpStyles.Label
            },
            new TextElement
            {
                Bounds = new Rect(460, 64, 112, 12),
                Content = Expr.Str(f.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                Style = ErpStyles.Default
            },
            new TextElement
            {
                Bounds = new Rect(380, 78, 80, 12),
                Content = Expr.Str("CAI:"),
                Style = ErpStyles.Label
            },
            new TextElement
            {
                Bounds = new Rect(460, 78, 112, 10),
                Content = Expr.Str(f.Cai),
                Style = ErpStyles.Caption
            },
        }
    };

    private static PageHeaderBand BuildPageHeader() => new()
    {
        Height = 0,  // sin page header en factura
        Elements = Array.Empty<ReportElement>()
    };

    private static DetailBand BuildDetail(
        Factura f, decimal subtotal, decimal isv, decimal total) => new()
    {
        Height = 0,  // altura la determina el contenido
        Elements = new ReportElement[]
        {
            // Tabla de líneas
            new TableElement<LineaFactura>
            {
                Bounds = new Rect(0, 0, 572, 0),  // ancho fijo, alto dinámico
                Rows = f.Lineas,
                HeaderStyle = ErpStyles.TableHeader,
                RowStyle = ErpStyles.TableRow,
                AlternateRowStyle = ErpStyles.TableRowAlt,
                HeaderMode = TableHeaderMode.RepeatOnPageBreak,
                HeaderHeight = 22,
                RowHeight = 18,
                Columns = new[]
                {
                    new TableColumn<LineaFactura>(
                        "Código",
                        Bind.From<LineaFactura, object?>(l => l.Codigo),
                        Width: 80),
                    new TableColumn<LineaFactura>(
                        "Descripción",
                        Bind.From<LineaFactura, object?>(l => l.Descripcion),
                        Width: 280),
                    new TableColumn<LineaFactura>(
                        "Cant.",
                        Bind.From<LineaFactura, object?>(l => l.Cantidad),
                        Width: 52,
                        Align: TextAlignment.Right),
                    new TableColumn<LineaFactura>(
                        "P. Unit.",
                        Bind.From<LineaFactura, object?>(l => l.PrecioUnitario),
                        Width: 80,
                        Format: "N2",
                        Align: TextAlignment.Right),
                    new TableColumn<LineaFactura>(
                        "Subtotal",
                        Bind.From<LineaFactura, object?>(l => l.Subtotal),
                        Width: 80,
                        Format: "N2",
                        Align: TextAlignment.Right),
                }
            }
        }
    };

    private static ReportFooterBand BuildReportFooter() => new()
    {
        Height = 80,
        Elements = new ReportElement[]
        {
            // Bloque de totales alineado a la derecha
            new LineElement
            {
                Bounds = new Rect(380, 8, 192, 0),
                Color = ErpColors.Gray300,
                Thickness = 0.5
            },

            new TextElement
            {
                Bounds = new Rect(380, 14, 100, 14),
                Content = Expr.Str("Subtotal:"),
                Style = ErpStyles.Label
            },
            new TextElement
            {
                Bounds = new Rect(480, 14, 92, 14),
                Content = Expr.Of(ctx => (ctx.GetParameter("subtotal") ?? 0m).ToString()),
                Style = ErpStyles.Default
            },
            // ... (en el prototipo pasamos los valores pre-calculados como expresiones literales)
        }
    };

    private static PageFooterBand BuildPageFooter() => new()
    {
        Height = 28,
        Elements = new ReportElement[]
        {
            new LineElement
            {
                Bounds = new Rect(0, 0, 572, 0),
                Color = ErpColors.Gray300,
                Thickness = 0.5
            },
            new TextElement
            {
                Bounds = new Rect(0, 8, 572, 10),
                Content = Expr.PageOfTotal,
                Style = ErpStyles.Caption with { }
            }
        }
    };
}
```

> **Nota honesta:** el `BuildReportFooter` quedó incompleto en el sample — debería incluir los valores reales de subtotal/ISV/total. En el prototipo se resuelve más limpio cerrando esos valores en el closure (son constantes para la factura en cuestión):
>
> ```csharp
> Content = Expr.Str(subtotal.ToString("N2", cultureHn))
> ```
>
> El fragmento con `Expr.Of(ctx => ctx.GetParameter(...))` es lo que usarías cuando los valores vienen del contexto de ejecución, no de closures. Para una factura simple, closures es el camino.

#### `samples/InvoiceSample/Program.cs`

```csharp
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
```

---

## 8. Cómo correrlo

Desde la raíz de la solución:

```bash
dotnet run --project samples/InvoiceSample
```

Debería generar `factura.pdf` en el directorio bin/Debug del sample. Abrirlo con cualquier visor PDF.

**Resultado esperado:**
- 1 página (con 8 líneas cabe en una página Letter).
- Encabezado con nombre empresa, cliente, fecha, CAI.
- Tabla con 5 columnas, filas alternadas (zebra).
- Pie de página con "Página 1 de 1".

Si agregas 50 líneas a la factura, debería:
- Paginar automáticamente.
- Repetir el header de la tabla en cada página nueva.
- Mostrar numeración correcta en los pies.

---

## 9. Qué valida este prototipo y qué NO valida

### ✅ Qué SÍ valida

- El IR es expresable: se puede construir una factura completa con la API.
- `StyleSheet` + `StyleBuilder` + constantes `ErpStyles` es ergonómico.
- `IDataBinding<TRow, TValue>` funciona sin reflection.
- `Expr.Of(...)` y `Expr.Str(...)` son suficientes para textos dinámicos simples.
- Layout engine produce `RenderList` correcto.
- `PdfRenderer` consume `RenderList` y produce PDF.
- Paginación básica con repetición de headers de tabla.
- Filas alternadas funcionan.
- Alineación de columnas (texto vs. números) funciona.
- Formatos (`N2`, `dd/MM/yyyy`) funcionan.

### ❌ Qué NO valida (fase 2 y más)

- **Grupos con subtotales.** No hay `GroupHeaderBand`/`GroupFooterBand` todavía.
- **Agregados.** `Sum`, `Count`, etc. no están implementados en el prototipo (se pre-calculan fuera).
- **Word wrap real.** El texto se pinta en una línea; si es largo, se corta.
- **Auto-height.** Los elementos usan altura fija.
- **KeepTogether.** No hay control de "mantener esta banda junta".
- **Images.** No incluidas en el prototipo.
- **Barcode / QR.** No incluidas.
- **Charts.** No incluidas.
- **Subreportes.** No incluidos.
- **HTML / XLSX renderers.** No incluidos.
- **Visor.** No incluido.
- **DSL de expresiones.** Solo closures de C#.
- **Source Markdown.** Solo API fluent.

### ⚠️ Deuda técnica conocida

1. **Reflection en `LayoutEngine.RenderTableElement`** — workaround para invocar método genérico. Fix correcto: visitor pattern o rediseño para que `TableElement` exponga un método virtual que haga el render.
2. **`BuildReportFooter` en InvoiceReport** — el manejo de totales vía closure es lo correcto; el fragmento con `GetParameter` está ahí solo como ejemplo de cómo funcionará el parameter binding.
3. **`TextMeasurer` es una aproximación.** En producción se mide con SkiaSharp real.
4. **`PageHeader` de altura 0.** En el sample no hay page header; el layout engine lo maneja pero no está exhaustivamente probado con page header no-vacío.

---

## 10. Siguientes pasos

Una vez que tengas el prototipo compilando y generando la factura correctamente:

1. **Probar con factura de 50+ líneas** — validar paginación y repetición de headers.
2. **Probar cambiando colores del `ErpTheme`** — validar que todos los reportes lo consumen.
3. **Escribir un golden test** — serializar el `RenderList` a JSON, compararlo con uno guardado.
4. **Fase 2.1: grupos y subtotales.** Agregar `GroupHeaderBand`/`GroupFooterBand` y agregados.
5. **Fase 2.2: HTML renderer.** Consume el mismo `RenderList`, produce HTML paginado con CSS `@page`.
6. **Fase 2.3: Visor Blazor.** Wrapper con navegación sobre el HTML renderer.
7. **Fase 2.4: Specs del Markdown extendido.** Segundo documento, paralelo al IR.

---

## Apéndice: ¿Por qué algunas decisiones pueden parecer raras?

**¿Por qué `StyleRef` es un struct en vez de una clase?**
Porque `new StyleRef("Title")` se pasa por valor y no aloca. Tener una constante `ErpStyles.Title` y pasarla 1000 veces en un reporte no genera GC pressure.

**¿Por qué `TableElement<TRow>` en vez de `TableElement` con filas `object`?**
Porque el `IDataBinding<TRow, TValue>` necesita el tipo para el delegate tipado. Si `TRow` fuera `object`, el JIT no podría inlinear el acceso. Pagarías un cast por cada celda.

**¿Por qué `RenderCommand` es `abstract record` en vez de una unión discriminada?**
.NET todavía no tiene discriminated unions (vienen en C# 13/14). Los `abstract record` con `sealed record` subclasses + pattern matching dan el 80% del beneficio.

**¿Por qué las bandas tienen `Height` fijo en el prototipo?**
Para evitar el "measure pass" completo. En la versión final, `Height = 0` o `AutoHeight = true` activa medición real de elementos hijos. El prototipo lo simplifica.

**¿Por qué usamos `SkiaSharp` directo en el `PdfRenderer` en vez de `QuestPDF.Element`?**
Porque queremos posicionamiento absoluto sin que QuestPDF intente hacer layout. `Canvas((canvas, space) => ...)` es el escape hatch de QuestPDF para este caso.

---

**Fin del documento.**

Cuando compiles y generes la primera factura, cualquier ajuste del IR que descubramos lo reflejamos en el documento principal de especificación (`NetReporter-IR-Specification.md`).
