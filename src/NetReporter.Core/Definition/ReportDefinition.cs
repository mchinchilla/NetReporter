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

    /// <summary>
    /// Nombre de archivo sugerido al exportar (sin extensión). Ya resuelto y sanitizado
    /// por <see cref="NetReporter.Templates.YamlReportLoader"/> — sin caracteres inválidos de filesystem.
    /// </summary>
    public string? FileName { get; init; }
}
