using System.Globalization;
using System.Text.Json;
using NetReporter.Core.Bands;
using NetReporter.Core.DataBinding;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Expressions;
using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;
using NetReporter.Templates.Binding;
using NetReporter.Templates.Schema;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetReporter.Templates;

/// <summary>
/// Carga un template YAML y lo convierte en un <see cref="TemplateReport"/> que se puede
/// enlazar con datos JSON para producir una <see cref="ReportDefinition"/> lista para el
/// <see cref="Core.Layout.LayoutEngine"/>.
/// </summary>
public static class YamlReportLoader
{
    private static readonly IDeserializer s_deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static TemplateReport Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var yaml = File.ReadAllText(path);
        return Parse(yaml);
    }

    public static TemplateReport Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        return new TemplateReport(ParseModel(yaml));
    }

    /// <summary>Deserializa YAML directamente al POCO <see cref="ReportYaml"/> sin construir el IR.</summary>
    public static ReportYaml ParseModel(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        return s_deserializer.Deserialize<ReportYaml>(yaml)
               ?? throw new FormatException("Template YAML vacío o inválido.");
    }

    /// <summary>
    /// Sanitiza un filename: reemplaza chars inválidos de filesystem con '-',
    /// colapsa secuencias, trunca a 200 chars, quita puntos/espacios/guiones terminales.
    /// </summary>
    public static string SanitizeFileName(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var trimmed = input.Trim();
        if (trimmed.Length == 0) return string.Empty;

        var sb = new System.Text.StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            if (c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' ||
                c == '"' || c == '<' || c == '>' || c == '|' || char.IsControl(c))
            {
                sb.Append('-');
            }
            else
            {
                sb.Append(c);
            }
        }

        var collapsed = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"[-\s]+", "-");
        var result = collapsed.Trim(' ', '.', '-');

        return result.Length <= 200 ? result : result[..200];
    }
}

/// <summary>
/// Template parseado pero aún sin datos. Se enlaza con JSON vía <see cref="Bind(string, IReadOnlyDictionary{string, StyleSheet}?)"/>.
/// </summary>
public sealed class TemplateReport
{
    private readonly ReportYaml _yaml;

    public TemplateReport(ReportYaml yaml) => _yaml = yaml;

    /// <summary>Enlaza el template con datos JSON. El JsonDocument se mantiene vivo por closures.</summary>
    public ReportDefinition Bind(string json, IReadOnlyDictionary<string, StyleSheet>? themes = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        var doc = JsonDocument.Parse(json);
        // El JsonDocument queda capturado por las expresiones. Aceptado como tradeoff.
        return Bind(doc.RootElement, themes);
    }

    public ReportDefinition Bind(JsonElement data, IReadOnlyDictionary<string, StyleSheet>? themes = null)
    {
        var page = ResolvePage(_yaml.Page);
        var styles = ResolveStyles(_yaml, themes);
        var culture = ResolveCulture(_yaml.Culture);
        var yamlBands = _yaml.Bands ?? new List<BandYaml>();
        var bands = yamlBands
            .Select((b, i) => BuildBand(b, data, i))
            .ToArray();

        return new ReportDefinition
        {
            Name = _yaml.Name ?? "Report",
            Title = _yaml.Title,
            FileName = ResolveFileName(_yaml.FileName, data),
            Page = page,
            Styles = styles,
            Culture = culture,
            Bands = bands
        };
    }

    /// <summary>
    /// Resuelve el template de filename contra el data root y sanitiza chars inválidos
    /// de filesystem. Trunca a 200 chars. Devuelve null si el template resuelve a vacío.
    /// </summary>
    private static string? ResolveFileName(string? template, JsonElement dataRoot)
    {
        if (string.IsNullOrWhiteSpace(template)) return null;
        var raw = Binding.TemplateString.Resolve(template, dataRoot);
        var clean = YamlReportLoader.SanitizeFileName(raw);
        return string.IsNullOrWhiteSpace(clean) ? null : clean;
    }

    // === Page ===

    private static PageSetup ResolvePage(PageYaml? yaml)
    {
        if (yaml is null) return PageSetup.Letter;

        var size = (yaml.Size ?? "letter").ToLowerInvariant();
        var page = size switch
        {
            "letter"    => PageSetup.Letter,
            "legal"     => PageSetup.Legal,
            "tabloid" or "ledger" => PageSetup.Tabloid,
            "executive" => PageSetup.Executive,
            "statement" => PageSetup.Statement,
            "a3" => PageSetup.A3,
            "a4" => PageSetup.A4,
            "a5" => PageSetup.A5,
            "a6" => PageSetup.A6,
            "b4" => PageSetup.B4,
            "b5" => PageSetup.B5,
            "custom" => PageSetup.Custom(
                yaml.Width  ?? throw new FormatException("page.size=custom requiere 'width'."),
                yaml.Height ?? throw new FormatException("page.size=custom requiere 'height'.")),
            _ => throw new FormatException($"Tamaño de página desconocido: '{yaml.Size}'.")
        };

        if (string.Equals(yaml.Orientation, "landscape", StringComparison.OrdinalIgnoreCase))
            page = page.Landscape();

        if (yaml.Margins is not null)
            page = page.WithMargins(ResolveThickness(yaml.Margins));

        return page;
    }

    private static Thickness ResolveThickness(MarginsYaml yaml)
    {
        if (yaml.All is not null)
            return new Thickness(ParseLength(yaml.All));

        if (yaml.Horizontal is not null || yaml.Vertical is not null)
        {
            var h = yaml.Horizontal is not null ? ParseLength(yaml.Horizontal) : 0;
            var v = yaml.Vertical   is not null ? ParseLength(yaml.Vertical)   : 0;
            return new Thickness(h, v);
        }

        return new Thickness(
            yaml.Left   is not null ? ParseLength(yaml.Left)   : 0,
            yaml.Top    is not null ? ParseLength(yaml.Top)    : 0,
            yaml.Right  is not null ? ParseLength(yaml.Right)  : 0,
            yaml.Bottom is not null ? ParseLength(yaml.Bottom) : 0);
    }

    private static double ParseLength(string raw)
    {
        var text = raw.Trim().ToLowerInvariant();
        return text switch
        {
            _ when text.EndsWith("cm") => double.Parse(text[..^2].Trim(), CultureInfo.InvariantCulture) * Units.PointsPerCm,
            _ when text.EndsWith("mm") => double.Parse(text[..^2].Trim(), CultureInfo.InvariantCulture) * Units.PointsPerMm,
            _ when text.EndsWith("in") => double.Parse(text[..^2].Trim(), CultureInfo.InvariantCulture) * Units.PointsPerInch,
            _ when text.EndsWith("pt") => double.Parse(text[..^2].Trim(), CultureInfo.InvariantCulture),
            _ => double.Parse(text, CultureInfo.InvariantCulture)
        };
    }

    // === Culture ===

    private static CultureInfo ResolveCulture(string? name) =>
        string.IsNullOrWhiteSpace(name) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(name);

    // === Styles ===

    private static StyleSheet ResolveStyles(ReportYaml yaml, IReadOnlyDictionary<string, StyleSheet>? themes)
    {
        if (!string.IsNullOrWhiteSpace(yaml.Theme))
        {
            if (themes is null || !themes.TryGetValue(yaml.Theme, out var th))
                throw new InvalidOperationException(
                    $"Tema '{yaml.Theme}' referenciado por el template no está registrado. " +
                    "Pasa un diccionario de temas al método Bind().");
            return th;
        }

        if (yaml.Styles is null || yaml.Styles.Count == 0)
            return StyleSheet.Minimal;

        var builder = new StyleSheetBuilder();
        foreach (var (name, styleYaml) in yaml.Styles)
            builder.Add(name, sb => ApplyStyle(sb, styleYaml));

        return builder.Build();
    }

    private static void ApplyStyle(StyleBuilder sb, StyleYaml y)
    {
        if (y.BasedOn is not null)   sb.BasedOn(y.BasedOn);
        if (y.FontFamily is not null) sb.FontFamily(y.FontFamily);
        if (y.FontSize is double fs)  sb.FontSize(fs);
        if (y.Bold == true)          sb.Bold();
        if (y.Italic == true)        sb.Italic();
        if (y.Foreground is not null) sb.Foreground(Color.FromHex(y.Foreground));
        if (y.Background is not null) sb.Background(Color.FromHex(y.Background));
        if (y.Align is not null)     sb.Align(ParseAlign(y.Align));
        if (y.VAlign is not null)    sb.VAlign(ParseVAlign(y.VAlign));
        if (y.Padding is not null)   sb.Padding(ResolveThickness(y.Padding));
        if (y.Format is not null)    sb.Format(y.Format);
        if (y.Border is not null)    sb.Border(ResolveBorder(y.Border));
    }

    private static TextAlignment ParseAlign(string s) => s.ToLowerInvariant() switch
    {
        "left"    => TextAlignment.Left,
        "center"  => TextAlignment.Center,
        "right"   => TextAlignment.Right,
        "justify" => TextAlignment.Justify,
        _ => throw new FormatException($"Align inválido: '{s}'. Usa left/center/right/justify.")
    };

    private static VerticalAlignment ParseVAlign(string s) => s.ToLowerInvariant() switch
    {
        "top"    => VerticalAlignment.Top,
        "center" => VerticalAlignment.Center,
        "bottom" => VerticalAlignment.Bottom,
        _ => throw new FormatException($"VAlign inválido: '{s}'. Usa top/center/bottom.")
    };

    private static BorderSet ResolveBorder(BorderYaml y)
    {
        if (y.All is not null)
        {
            var a = ResolveBorderLine(y.All);
            return BorderSet.All(a.Thickness, a.Color);
        }
        if (y.HorizontalEdges is not null)
        {
            var h = ResolveBorderLine(y.HorizontalEdges);
            return BorderSet.Horizontal(h.Thickness, h.Color);
        }
        return new BorderSet(
            Left:   y.Left   is not null ? ResolveBorderLine(y.Left)   : null,
            Top:    y.Top    is not null ? ResolveBorderLine(y.Top)    : null,
            Right:  y.Right  is not null ? ResolveBorderLine(y.Right)  : null,
            Bottom: y.Bottom is not null ? ResolveBorderLine(y.Bottom) : null);
    }

    private static BorderLine ResolveBorderLine(BorderLineYaml y) =>
        new(y.Thickness, y.Color is not null ? Color.FromHex(y.Color) : Color.Black);

    // === Bands ===

    private static Band BuildBand(BandYaml b, JsonElement data, int bandIndex)
    {
        var elements = (b.Elements ?? new List<ElementYaml>())
            .Select((e, j) => BuildElement(e, data, $"bands.{bandIndex}.elements.{j}"))
            .ToArray();

        var kind = (b.Kind ?? "Detail").ToLowerInvariant();
        return kind switch
        {
            "reportheader" => new ReportHeaderBand { Height = b.Height, Elements = elements },
            "pageheader"   => new PageHeaderBand   { Height = b.Height, Elements = elements },
            "detail"       => new DetailBand       { Height = b.Height, Elements = elements },
            "pagefooter"   => new PageFooterBand   { Height = b.Height, Elements = elements },
            "reportfooter" => new ReportFooterBand { Height = b.Height, Elements = elements },
            _ => throw new FormatException(
                $"Band.kind desconocido: '{b.Kind}'. " +
                "Usa ReportHeader, PageHeader, Detail, PageFooter o ReportFooter.")
        };
    }

    // === Elements ===

    private static ReportElement BuildElement(ElementYaml e, JsonElement data, string sourcePath)
    {
        var type = (e.Type ?? "text").ToLowerInvariant();
        var bounds = ResolveBounds(e.Bounds);
        var style = e.Style is not null ? new StyleRef(e.Style) : StyleRef.Default;

        ReportElement built = type switch
        {
            "text"      => BuildText(e, bounds, style, data),
            "table"     => BuildTable(e, bounds, style, data),
            "line"      => BuildLine(e, bounds, style),
            "rectangle" => BuildRectangle(e, bounds, style),
            _ => throw new FormatException(
                $"Element.type desconocido: '{e.Type}'. Usa text/table/line/rectangle.")
        };

        // Etiqueta el elemento con su origen en el template para que el Designer pueda editarlo.
        return built with { SourcePath = sourcePath };
    }

    private static Rect ResolveBounds(BoundsYaml? b) =>
        b is null ? Rect.Empty : new Rect(b.X, b.Y, b.Width, b.Height);

    private static TextElement BuildText(ElementYaml e, Rect bounds, StyleRef style, JsonElement data)
    {
        var content = e.Content ?? string.Empty;
        return new TextElement
        {
            Bounds = bounds,
            Style = style,
            Content = TemplateString.Compile(content, data)
        };
    }

    private static TableElement<JsonElement> BuildTable(ElementYaml e, Rect bounds, StyleRef style, JsonElement data)
    {
        if (e.Rows is null)
            throw new FormatException("Element.type=table requiere 'rows' (JSON Path).");
        if (e.Columns is null || e.Columns.Count == 0)
            throw new FormatException("Element.type=table requiere al menos una columna.");

        var rows = JsonPath.SelectMany(data, e.Rows);
        var columns = e.Columns.Select(BuildColumn).ToArray();

        var headerMode = (e.HeaderMode ?? "RepeatOnPageBreak").ToLowerInvariant() switch
        {
            "printonce" => TableHeaderMode.PrintOnce,
            "repeatonpagebreak" => TableHeaderMode.RepeatOnPageBreak,
            _ => throw new FormatException($"HeaderMode desconocido: '{e.HeaderMode}'.")
        };

        return new TableElement<JsonElement>
        {
            Bounds = bounds,
            Style = style,
            Rows = rows,
            Columns = columns,
            HeaderHeight = e.HeaderHeight ?? 22,
            RowHeight = e.RowHeight ?? 18,
            HeaderMode = headerMode,
            HeaderStyle = e.HeaderStyle is not null ? new StyleRef(e.HeaderStyle) : new StyleRef("TableHeader"),
            RowStyle    = e.RowStyle    is not null ? new StyleRef(e.RowStyle)    : new StyleRef("TableRow"),
            AlternateRowStyle = e.AlternateRowStyle is not null ? new StyleRef(e.AlternateRowStyle) : null
        };
    }

    private static TableColumn<JsonElement> BuildColumn(TableColumnYaml c)
    {
        if (c.Header is null)
            throw new FormatException("TableColumn requiere 'header'.");
        if (c.Binding is null)
            throw new FormatException($"TableColumn '{c.Header}' requiere 'binding' (JSON Path).");

        var align = c.Align is not null ? ParseAlign(c.Align) : TextAlignment.Left;
        return new TableColumn<JsonElement>(
            c.Header,
            new JsonPathBinding(c.Binding),
            c.Width,
            c.Format,
            align);
    }

    private static LineElement BuildLine(ElementYaml e, Rect bounds, StyleRef style)
    {
        var orientation = (e.Orientation ?? "horizontal").ToLowerInvariant() switch
        {
            "horizontal" => LineOrientation.Horizontal,
            "vertical"   => LineOrientation.Vertical,
            _ => throw new FormatException($"Line.orientation inválido: '{e.Orientation}'.")
        };

        return new LineElement
        {
            Bounds = bounds,
            Style = style,
            Orientation = orientation,
            Thickness = e.Thickness ?? 0.5,
            Color = e.Color is not null ? Color.FromHex(e.Color) : Color.Black
        };
    }

    private static RectangleElement BuildRectangle(ElementYaml e, Rect bounds, StyleRef style)
    {
        return new RectangleElement
        {
            Bounds = bounds,
            Style = style,
            Fill = e.Fill is not null ? Color.FromHex(e.Fill) : null,
            BorderLine = e.BorderLine is not null ? ResolveBorderLine(e.BorderLine) : null
        };
    }
}
