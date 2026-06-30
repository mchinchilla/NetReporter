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
            .Where(el => el is not null)
            .Select(el => el!)
            .ToArray();

        var kind = (b.Kind ?? "Detail").ToLowerInvariant();
        var autoHeight = b.AutoHeight ?? false;
        var keepTogether = b.KeepTogether ?? false;
        return kind switch
        {
            "reportheader" => new ReportHeaderBand { Height = b.Height, Elements = elements, AutoHeight = autoHeight, KeepTogether = keepTogether },
            "pageheader"   => new PageHeaderBand   { Height = b.Height, Elements = elements, AutoHeight = autoHeight, KeepTogether = keepTogether },
            "detail"       => new DetailBand       { Height = b.Height, Elements = elements, AutoHeight = autoHeight, KeepTogether = keepTogether },
            "pagefooter"   => new PageFooterBand   { Height = b.Height, Elements = elements, AutoHeight = autoHeight, KeepTogether = keepTogether },
            "reportfooter" => new ReportFooterBand { Height = b.Height, Elements = elements, AutoHeight = autoHeight, KeepTogether = keepTogether },
            _ => throw new FormatException(
                $"Band.kind desconocido: '{b.Kind}'. " +
                "Usa ReportHeader, PageHeader, Detail, PageFooter o ReportFooter.")
        };
    }

    // === Elements ===

    /// <summary>
    /// Builds one element. Returns <c>null</c> when the element should be skipped — currently only
    /// an <c>image</c> whose <c>source</c> resolves to empty or to a path that doesn't exist
    /// (a missing logo must not abort the whole report). <see cref="BuildBand"/> filters nulls out.
    /// </summary>
    private static ReportElement? BuildElement(ElementYaml e, JsonElement data, string sourcePath)
    {
        var type = (e.Type ?? "text").ToLowerInvariant();
        var bounds = ResolveBounds(e.Bounds);
        var style = e.Style is not null ? new StyleRef(e.Style) : StyleRef.Default;

        ReportElement? built = type switch
        {
            "text"      => BuildText(e, bounds, style, data),
            "table"     => BuildTable(e, bounds, style, data),
            "line"      => BuildLine(e, bounds, style),
            "rectangle" => BuildRectangle(e, bounds, style),
            "image"     => BuildImage(e, bounds, style, data),
            "barcode"   => BuildBarcode(e, bounds, style, data),
            _ => throw new FormatException(
                $"Element.type desconocido: '{e.Type}'. Usa text/table/line/rectangle/image/barcode.")
        };

        // Etiqueta el elemento con su origen en el template para que el Designer pueda editarlo.
        return built is null ? null : built with { SourcePath = sourcePath };
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
            AlternateRowStyle = e.AlternateRowStyle is not null ? new StyleRef(e.AlternateRowStyle) : null,
            GroupBy = e.GroupBy is not null ? new JsonPathBinding(e.GroupBy) : null,
            GroupHeader = e.GroupHeader is not null ? BuildGroupHeader(e.GroupHeader, data) : null,
            GroupFooter = e.GroupFooter is not null ? BuildGroupFooter(e.GroupFooter, data) : null,
            HeaderRule = e.HeaderRule is not null ? ResolveBorderLine(e.HeaderRule) : null,
            RowSeparator = e.RowSeparator is not null ? ResolveBorderLine(e.RowSeparator) : null,
            RowStyleSelector = e.RowStyleBinding is not null ? new JsonPathBinding(e.RowStyleBinding) : null,
            RowStyleMap = e.RowStyleMap is not null
                ? e.RowStyleMap.ToDictionary(kv => kv.Key, kv => new StyleRef(kv.Value))
                : null,
            FullRowBackground = e.FullRowBackground ?? false,
            SuppressAdvance = e.SuppressAdvance ?? false,
            OuterBorder = e.OuterBorder is not null ? ResolveBorderLine(e.OuterBorder) : null,
            CornerRadius = e.CornerRadius ?? 0
        };
    }

    private static GroupHeader BuildGroupHeader(GroupHeaderYaml g, JsonElement data) => new()
    {
        Height = g.Height,
        Content = TemplateString.Compile(g.Content ?? string.Empty, data),
        Style = g.Style is not null ? new StyleRef(g.Style) : new StyleRef("GroupHeader")
    };

    private static GroupFooter BuildGroupFooter(GroupFooterYaml g, JsonElement data)
    {
        var cells = (g.Cells ?? new List<GroupFooterCellYaml?>())
            .Select(c => c is null ? null : BuildGroupFooterCell(c, data))
            .ToList();
        return new GroupFooter
        {
            Height = g.Height,
            Cells = cells,
            Style = g.Style is not null ? new StyleRef(g.Style) : new StyleRef("GroupFooter")
        };
    }

    private static GroupFooterCell? BuildGroupFooterCell(GroupFooterCellYaml c, JsonElement data)
    {
        AggregateKind? aggregate = null;
        if (!string.IsNullOrWhiteSpace(c.Aggregate))
        {
            aggregate = c.Aggregate.ToLowerInvariant() switch
            {
                "sum"   => AggregateKind.Sum,
                "count" => AggregateKind.Count,
                "avg"   => AggregateKind.Avg,
                _ => throw new FormatException(
                    $"Aggregate desconocido: '{c.Aggregate}'. Usa sum/count/avg.")
            };
        }

        return new GroupFooterCell
        {
            Content = c.Content is not null ? TemplateString.Compile(c.Content, data) : null,
            Aggregate = aggregate,
            Format = c.Format,
            Align = c.Align is not null ? ParseAlign(c.Align) : null
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
            align,
            c.Style is not null ? new StyleRef(c.Style) : null);
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

    /// <summary>
    /// Builds an image element. The <c>source</c> may be a literal data URI / file path, or a
    /// template string (<c>{{ $.path }}</c>) — resolved here against the data root, just like
    /// barcode <c>value</c>. Returns <c>null</c> (element skipped) when the resolved source is
    /// empty/whitespace or points to a file that doesn't exist — e.g. a white-label logo that
    /// isn't configured shouldn't make the whole report fail to render.
    /// </summary>
    private static ImageElement? BuildImage(ElementYaml e, Rect bounds, StyleRef style, JsonElement data)
    {
        if (string.IsNullOrWhiteSpace(e.Source))
            throw new FormatException("Element.type=image requiere 'source' (path local o data URI).");

        // Resolve {{ $.path }} placeholders the same way barcode values are resolved.
        var resolvedSource = TemplateString.Resolve(e.Source, data);
        if (string.IsNullOrWhiteSpace(resolvedSource))
            return null; // optional image (e.g. unconfigured logo) — skip silently

        var fit = (e.Fit ?? "contain").ToLowerInvariant() switch
        {
            "fill"    => ImageFit.Fill,
            "contain" => ImageFit.Contain,
            _ => throw new FormatException($"Image.fit inválido: '{e.Fit}'. Usa contain/fill.")
        };

        if (!TryLoadImageSource(resolvedSource, out var imgData, out var mime))
            return null; // missing file (or malformed data URI) — skip rather than abort the report

        return new ImageElement
        {
            Bounds = bounds,
            Style = style,
            Data = imgData,
            MimeType = mime,
            Fit = fit
        };
    }

    private static BarcodeElement BuildBarcode(ElementYaml e, Rect bounds, StyleRef style, JsonElement data)
    {
        if (string.IsNullOrWhiteSpace(e.Value))
            throw new FormatException("Element.type=barcode requiere 'value' (template string o literal).");

        var format = (e.Format ?? "qr").ToLowerInvariant() switch
        {
            "qr" or "qrcode" => BarcodeFormat.QrCode,
            "code128"        => BarcodeFormat.Code128,
            "code39"         => BarcodeFormat.Code39,
            "ean13"          => BarcodeFormat.Ean13,
            _ => throw new FormatException(
                $"Barcode.format inválido: '{e.Format}'. Usa qr/code128/code39/ean13.")
        };

        return new BarcodeElement
        {
            Bounds = bounds,
            Style = style,
            Value = TemplateString.Compile(e.Value, data),
            Format = format,
            Foreground = e.BarcodeForeground is not null ? Color.FromHex(e.BarcodeForeground) : Color.Black,
            Background = e.BarcodeBackground is not null ? Color.FromHex(e.BarcodeBackground) : Color.Transparent
        };
    }

    /// <summary>
    /// Carga un source de imagen: data URI <c>data:image/png;base64,XXX</c> o path local.
    /// </summary>
    private static (byte[] Data, string Mime) LoadImageSource(string source)
    {
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            // data:image/png;base64,XXXXX
            var commaIdx = source.IndexOf(',');
            if (commaIdx < 0) throw new FormatException("Data URI sin coma separadora.");
            var meta = source.Substring(5, commaIdx - 5); // "image/png;base64"
            var payload = source[(commaIdx + 1)..];

            var mime = meta.Split(';')[0];
            if (string.IsNullOrEmpty(mime)) mime = "image/png";

            // Asume base64 (formato más común en data URIs).
            var bytes = Convert.FromBase64String(payload);
            return (bytes, mime);
        }

        // Path local — lee del filesystem.
        if (!File.Exists(source))
            throw new FileNotFoundException($"Imagen no encontrada: '{source}'.");

        var ext = Path.GetExtension(source).ToLowerInvariant();
        var mimeFromExt = ext switch
        {
            ".png"  => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif"  => "image/gif",
            ".webp" => "image/webp",
            ".bmp"  => "image/bmp",
            _ => "image/png"      // default seguro
        };
        return (File.ReadAllBytes(source), mimeFromExt);
    }

    /// <summary>
    /// Non-throwing variant of <see cref="LoadImageSource"/>. Returns <c>false</c> for a missing
    /// file or a malformed data URI instead of raising — lets <see cref="BuildImage"/> treat an
    /// optional image (e.g. an unconfigured logo) as "just don't draw it".
    /// </summary>
    private static bool TryLoadImageSource(string source, out byte[] data, out string mime)
    {
        try
        {
            (data, mime) = LoadImageSource(source);
            return true;
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException or UnauthorizedAccessException)
        {
            data = [];
            mime = "image/png";
            return false;
        }
    }
}
