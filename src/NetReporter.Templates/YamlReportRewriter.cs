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
        });
    }

    private static string? NormalizeHexOrNull(string input)
    {
        var t = input.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
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
            _ => throw new FormatException(
                $"Kind desconocido: '{kind}'. Usa text/line/rectangle.")
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
