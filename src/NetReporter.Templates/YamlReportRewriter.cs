using NetReporter.Templates.Schema;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetReporter.Templates;

/// <summary>
/// Aplica ediciones estructurales sobre un template YAML (parse → modify POCO → re-serialize).
/// Limitación conocida: la re-serialización pierde comentarios y puede reordenar claves.
/// </summary>
public static class YamlReportRewriter
{
    private static readonly IDeserializer s_deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer s_serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    /// <summary>
    /// Aplica un delta (dx, dy) al <c>bounds</c> del elemento identificado por <paramref name="path"/>
    /// y devuelve el YAML resultante.
    /// </summary>
    /// <param name="path">Path tipo "bands.N.elements.M".</param>
    public static string MoveElement(string yaml, string path, double deltaX, double deltaY)
    {
        return UpdateElement(yaml, path, e =>
        {
            e.Bounds ??= new BoundsYaml();
            e.Bounds.X = Math.Round(e.Bounds.X + deltaX, 2);
            e.Bounds.Y = Math.Round(e.Bounds.Y + deltaY, 2);
        });
    }

    /// <summary>Aplica una edición arbitraria al elemento identificado por <paramref name="path"/>.</summary>
    public static string UpdateElement(string yaml, string path, Action<ElementYaml> updater)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(updater);

        var model = s_deserializer.Deserialize<ReportYaml>(yaml)
                    ?? throw new FormatException("Template YAML vacío o inválido.");

        var (bandIndex, elementIndex) = ParsePath(path);
        var band = model.Bands?.ElementAtOrDefault(bandIndex)
                   ?? throw new InvalidOperationException($"Banda {bandIndex} no existe.");
        var element = band.Elements?.ElementAtOrDefault(elementIndex)
                      ?? throw new InvalidOperationException(
                          $"Elemento {elementIndex} no existe en banda {bandIndex}.");

        updater(element);

        return s_serializer.Serialize(model);
    }

    /// <summary>
    /// Aplica un patch con los campos no-null. Valida que el campo sea aplicable al kind.
    /// </summary>
    public static string ApplyPatch(string yaml, string path, ElementPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        return UpdateElement(yaml, path, e =>
        {
            var kind = (e.Type ?? "text").ToLowerInvariant();

            if (patch.X is double x || patch.Y is double y2 || patch.Width is double w || patch.Height is double h)
            {
                e.Bounds ??= new BoundsYaml();
                if (patch.X is double xv)      e.Bounds.X = Math.Round(xv, 2);
                if (patch.Y is double yv)      e.Bounds.Y = Math.Round(yv, 2);
                if (patch.Width is double wv)  e.Bounds.Width = Math.Round(wv, 2);
                if (patch.Height is double hv) e.Bounds.Height = Math.Round(hv, 2);
            }

            if (patch.Style is not null)
                e.Style = string.IsNullOrWhiteSpace(patch.Style) ? null : patch.Style;

            if (patch.Content is not null)
            {
                if (kind != "text")
                    throw new InvalidOperationException($"'content' no aplica al kind '{kind}'.");
                e.Content = patch.Content;
            }

            if (patch.Color is not null)
            {
                if (kind != "line")
                    throw new InvalidOperationException($"'color' no aplica al kind '{kind}'.");
                e.Color = NormalizeHexOrNull(patch.Color);
            }

            if (patch.Thickness is double tv)
            {
                if (kind != "line")
                    throw new InvalidOperationException($"'thickness' no aplica al kind '{kind}'.");
                e.Thickness = tv;
            }

            if (patch.Fill is not null)
            {
                if (kind != "rectangle")
                    throw new InvalidOperationException($"'fill' no aplica al kind '{kind}'.");
                e.Fill = NormalizeHexOrNull(patch.Fill);
            }

            // Campos específicos de tabla
            if (patch.Rows is not null ||
                patch.HeaderMode is not null ||
                patch.HeaderHeight is not null ||
                patch.RowHeight is not null ||
                patch.HeaderStyle is not null ||
                patch.RowStyle is not null ||
                patch.AlternateRowStyle is not null)
            {
                if (kind != "table")
                    throw new InvalidOperationException(
                        $"Campos de tabla no aplican al kind '{kind}'.");

                if (patch.Rows is not null)              e.Rows = patch.Rows;
                if (patch.HeaderMode is not null)        e.HeaderMode = patch.HeaderMode;
                if (patch.HeaderHeight is double hh)     e.HeaderHeight = hh;
                if (patch.RowHeight is double rh)        e.RowHeight = rh;
                if (patch.HeaderStyle is not null)       e.HeaderStyle = patch.HeaderStyle;
                if (patch.RowStyle is not null)          e.RowStyle = patch.RowStyle;
                if (patch.AlternateRowStyle is not null) e.AlternateRowStyle =
                    string.IsNullOrWhiteSpace(patch.AlternateRowStyle) ? null : patch.AlternateRowStyle;
            }

            // Resize redistributivo: si cambió el ancho de una tabla, escalar columnas proporcionalmente.
            if (kind == "table" && patch.Width is double newW &&
                e.Columns is { Count: > 0 })
            {
                var currentTotal = e.Columns.Sum(c => c.Width);
                if (currentTotal > 0 && Math.Abs(currentTotal - newW) > 0.01)
                {
                    var factor = newW / currentTotal;
                    foreach (var c in e.Columns)
                        c.Width = Math.Round(c.Width * factor, 2);
                }
            }
        });
    }

    /// <summary>Reemplaza la lista de columnas completa del TableElement en <paramref name="path"/>.</summary>
    public static string UpdateColumns(string yaml, string path, IReadOnlyList<TableColumnYaml> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        return UpdateElement(yaml, path, e =>
        {
            if ((e.Type ?? "text").ToLowerInvariant() != "table")
                throw new InvalidOperationException(
                    $"UpdateColumns solo aplica a tablas (kind='{e.Type}').");

            // Validación mínima
            foreach (var c in columns)
            {
                if (string.IsNullOrWhiteSpace(c.Header))
                    throw new ArgumentException("Cada columna requiere 'header'.");
                if (string.IsNullOrWhiteSpace(c.Binding))
                    throw new ArgumentException($"Columna '{c.Header}' requiere 'binding'.");
                if (c.Width <= 0)
                    throw new ArgumentException($"Columna '{c.Header}' requiere 'width' > 0.");
            }

            e.Columns = columns.ToList();
            // Ajustar bounds.Width para que coincida con la suma de widths.
            if (e.Columns.Count > 0)
            {
                e.Bounds ??= new BoundsYaml();
                e.Bounds.Width = Math.Round(e.Columns.Sum(c => c.Width), 2);
            }
        });
    }

    private static string? NormalizeHexOrNull(string input)
    {
        var t = input.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    /// <summary>Elimina el elemento en <paramref name="path"/> y devuelve el YAML resultante.</summary>
    public static string DeleteElement(string yaml, string path)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(path);

        var model = s_deserializer.Deserialize<ReportYaml>(yaml)
                    ?? throw new FormatException("Template YAML vacío o inválido.");

        var (bandIndex, elementIndex) = ParsePath(path);
        var band = model.Bands?.ElementAtOrDefault(bandIndex)
                   ?? throw new InvalidOperationException($"Banda {bandIndex} no existe.");
        if (band.Elements is null || elementIndex >= band.Elements.Count)
            throw new InvalidOperationException(
                $"Elemento {elementIndex} no existe en banda {bandIndex}.");

        band.Elements.RemoveAt(elementIndex);
        return s_serializer.Serialize(model);
    }

    /// <summary>
    /// Agrega un elemento nuevo con defaults razonables según <paramref name="kind"/> en la banda
    /// indicada. Devuelve el YAML resultante y el path del elemento creado (para re-seleccionarlo).
    /// </summary>
    public static (string Yaml, string NewPath) AddElement(
        string yaml, int bandIndex, string kind, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(kind);

        var model = s_deserializer.Deserialize<ReportYaml>(yaml)
                    ?? throw new FormatException("Template YAML vacío o inválido.");

        model.Bands ??= new List<BandYaml>();
        var band = model.Bands.ElementAtOrDefault(bandIndex)
                   ?? throw new InvalidOperationException($"Banda {bandIndex} no existe.");
        band.Elements ??= new List<ElementYaml>();

        var element = CreateDefaultElement(kind, x, y);
        band.Elements.Add(element);
        var newIndex = band.Elements.Count - 1;

        var newYaml = s_serializer.Serialize(model);
        var newPath = $"bands.{bandIndex}.elements.{newIndex}";
        return (newYaml, newPath);
    }

    /// <summary>
    /// Duplica el elemento en <paramref name="path"/> con un offset (+10, +10) para que sea visible.
    /// Se inserta justo después del original en la misma banda.
    /// </summary>
    public static (string Yaml, string NewPath) DuplicateElement(string yaml, string path)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(path);

        var model = s_deserializer.Deserialize<ReportYaml>(yaml)
                    ?? throw new FormatException("Template YAML vacío o inválido.");

        var (bandIndex, elementIndex) = ParsePath(path);
        var band = model.Bands?.ElementAtOrDefault(bandIndex)
                   ?? throw new InvalidOperationException($"Banda {bandIndex} no existe.");
        if (band.Elements is null || elementIndex >= band.Elements.Count)
            throw new InvalidOperationException(
                $"Elemento {elementIndex} no existe en banda {bandIndex}.");

        var original = band.Elements[elementIndex];
        var clone = CloneElement(original);
        clone.Bounds ??= new BoundsYaml();
        clone.Bounds.X = Math.Round(clone.Bounds.X + 10, 2);
        clone.Bounds.Y = Math.Round(clone.Bounds.Y + 10, 2);

        band.Elements.Insert(elementIndex + 1, clone);
        var newPath = $"bands.{bandIndex}.elements.{elementIndex + 1}";
        return (s_serializer.Serialize(model), newPath);
    }

    /// <summary>Agrega una banda nueva al final con el kind y altura dados.</summary>
    public static (string Yaml, int NewIndex) AddBand(string yaml, string kind, double height)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(kind);

        var model = s_deserializer.Deserialize<ReportYaml>(yaml)
                    ?? throw new FormatException("Template YAML vacío o inválido.");

        model.Bands ??= new List<BandYaml>();
        var normalizedKind = NormalizeBandKind(kind);
        model.Bands.Add(new BandYaml
        {
            Kind = normalizedKind,
            Height = Math.Max(height, 0),
            Elements = new List<ElementYaml>()
        });

        return (s_serializer.Serialize(model), model.Bands.Count - 1);
    }

    /// <summary>Elimina la banda en el índice dado.</summary>
    public static string DeleteBand(string yaml, int bandIndex)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var model = s_deserializer.Deserialize<ReportYaml>(yaml)
                    ?? throw new FormatException("Template YAML vacío o inválido.");

        if (model.Bands is null || bandIndex < 0 || bandIndex >= model.Bands.Count)
            throw new InvalidOperationException($"Banda {bandIndex} no existe.");

        model.Bands.RemoveAt(bandIndex);
        return s_serializer.Serialize(model);
    }

    /// <summary>Mueve una banda del índice <paramref name="from"/> al <paramref name="to"/>.</summary>
    public static string MoveBand(string yaml, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var model = s_deserializer.Deserialize<ReportYaml>(yaml)
                    ?? throw new FormatException("Template YAML vacío o inválido.");

        if (model.Bands is null || model.Bands.Count == 0)
            throw new InvalidOperationException("No hay bandas.");
        if (from < 0 || from >= model.Bands.Count)
            throw new InvalidOperationException($"Índice origen {from} inválido.");
        if (to < 0 || to >= model.Bands.Count)
            throw new InvalidOperationException($"Índice destino {to} inválido.");
        if (from == to) return yaml;

        var band = model.Bands[from];
        model.Bands.RemoveAt(from);
        model.Bands.Insert(to, band);
        return s_serializer.Serialize(model);
    }

    private static string NormalizeBandKind(string kind)
    {
        return kind.ToLowerInvariant() switch
        {
            "reportheader" => "ReportHeader",
            "pageheader"   => "PageHeader",
            "detail"       => "Detail",
            "pagefooter"   => "PageFooter",
            "reportfooter" => "ReportFooter",
            _ => throw new FormatException(
                $"Band.kind desconocido: '{kind}'. Usa ReportHeader/PageHeader/Detail/PageFooter/ReportFooter.")
        };
    }

    private static ElementYaml CloneElement(ElementYaml src)
    {
        // Roundtrip via YamlDotNet: simple y garantiza independencia profunda.
        var raw = s_serializer.Serialize(src);
        return s_deserializer.Deserialize<ElementYaml>(raw)!;
    }

    private static ElementYaml CreateDefaultElement(string kind, double x, double y) =>
        kind.ToLowerInvariant() switch
        {
            "text" => new ElementYaml
            {
                Type = "text",
                Bounds = new BoundsYaml { X = Math.Round(x, 2), Y = Math.Round(y, 2), Width = 120, Height = 14 },
                Content = "Texto nuevo",
                Style = "Default"
            },
            "line" => new ElementYaml
            {
                Type = "line",
                Bounds = new BoundsYaml { X = Math.Round(x, 2), Y = Math.Round(y, 2), Width = 120, Height = 0 },
                Color = "#000000",
                Thickness = 0.5,
                Orientation = "horizontal"
            },
            "rectangle" => new ElementYaml
            {
                Type = "rectangle",
                Bounds = new BoundsYaml { X = Math.Round(x, 2), Y = Math.Round(y, 2), Width = 100, Height = 40 },
                Fill = "#E5E7EB"
            },
            "table" => new ElementYaml
            {
                Type = "table",
                Bounds = new BoundsYaml { X = Math.Round(x, 2), Y = Math.Round(y, 2), Width = 300, Height = 0 },
                Rows = "$.items",
                HeaderStyle = "TableHeader",
                RowStyle = "TableRow",
                AlternateRowStyle = "TableRowAlt",
                HeaderMode = "RepeatOnPageBreak",
                HeaderHeight = 22,
                RowHeight = 18,
                Columns = new List<TableColumnYaml>
                {
                    new() { Header = "Columna 1", Binding = "$.col1", Width = 150, Align = "left" },
                    new() { Header = "Columna 2", Binding = "$.col2", Width = 150, Align = "left" }
                }
            },
            _ => throw new FormatException(
                $"Kind desconocido: '{kind}'. Usa text/line/rectangle/table.")
        };

    private static (int bandIndex, int elementIndex) ParsePath(string path)
    {
        // Formato esperado: "bands.N.elements.M"
        var parts = path.Split('.');
        if (parts.Length != 4 || parts[0] != "bands" || parts[2] != "elements")
            throw new FormatException(
                $"Path inválido: '{path}'. Formato esperado: 'bands.N.elements.M'.");

        if (!int.TryParse(parts[1], out var bandIdx) ||
            !int.TryParse(parts[3], out var elementIdx))
            throw new FormatException($"Índices no numéricos en path: '{path}'.");

        return (bandIdx, elementIdx);
    }
}
