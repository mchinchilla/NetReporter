using NetReporter.Core.Primitives;
using NetReporter.Core.Styles;

namespace NetReporter.Core.Elements;

public enum ChartKind
{
    /// <summary>Barras verticales (columnas). Varias series se agrupan lado a lado o se apilan.</summary>
    Bar,
    /// <summary>Barras horizontales: categorías en el eje Y, valores en el eje X.</summary>
    HorizontalBar,
    /// <summary>Líneas con marcadores, una por serie.</summary>
    Line,
    /// <summary>Área rellena bajo cada línea.</summary>
    Area,
    /// <summary>Pastel (usa la primera serie; una porción por categoría).</summary>
    Pie,
    /// <summary>Dona: pastel con hueco central (<see cref="ChartElement.InnerRadius"/>).</summary>
    Donut
}

public enum ChartLegendPosition
{
    None,
    Top,
    Bottom,
    Right
}

/// <summary>Una serie de valores paralela a <see cref="ChartElement.Categories"/>. Null = sin dato.</summary>
public sealed record ChartSeries(string Name, IReadOnlyList<double?> Values)
{
    /// <summary>Color de la serie. Null = tomado de la paleta por índice.</summary>
    public Color? Color { get; init; }
}

/// <summary>
/// Gráfico simple (bar / horizontalBar / line / area / pie / donut). Los datos llegan ya
/// materializados (categorías + series de números) — el loader YAML los extrae del JSON al enlazar,
/// y desde C# se construyen directo o con <see cref="From{TRow}"/>. El <c>LayoutEngine</c> lo
/// convierte en primitivas vectoriales (rectángulos, paths, líneas, texto), así que se ve igual en
/// PDF/SVG/HTML; el renderer XLSX lo exporta como tabla de datos.
/// El estilo del elemento (<see cref="ReportElement.Style"/>) define la tipografía de etiquetas.
/// </summary>
public sealed record ChartElement : ReportElement
{
    public ChartKind Kind { get; init; } = ChartKind.Bar;
    public required IReadOnlyList<string> Categories { get; init; }
    public required IReadOnlyList<ChartSeries> Series { get; init; }

    public string? Title { get; init; }

    /// <summary>Posición de la leyenda. Null = automático (pie/donut o varias series → abajo; si no, ninguna).</summary>
    public ChartLegendPosition? Legend { get; init; }

    /// <summary>Mostrar el valor sobre cada barra / punto / porción.</summary>
    public bool ShowValues { get; init; }

    /// <summary>Pie/donut: mostrar el porcentaje de cada porción.</summary>
    public bool ShowPercent { get; init; }

    public bool ShowGrid { get; init; } = true;

    /// <summary>Bar/horizontalBar/area: apilar las series en lugar de agruparlas.</summary>
    public bool Stacked { get; init; }

    /// <summary>Formato .NET para valores y etiquetas del eje (p. ej. <c>N0</c>, <c>C0</c>, <c>0.0'%'</c>).</summary>
    public string? ValueFormat { get; init; }

    /// <summary>Límites del eje de valores. Null = automático (siempre incluye el cero).</summary>
    public double? AxisMin { get; init; }
    public double? AxisMax { get; init; }

    /// <summary>Colores por serie (o por categoría en pie/donut). Null = paleta por defecto.</summary>
    public IReadOnlyList<Color>? Palette { get; init; }

    /// <summary>Donut: radio del hueco como fracción del radio exterior (0–0.9).</summary>
    public double InnerRadius { get; init; } = 0.55;

    /// <summary>Line/area: grosor de la línea en puntos.</summary>
    public double LineWidth { get; init; } = 2;

    public bool ShowMarkers { get; init; } = true;

    public Color? Background { get; init; }
    public BorderLine? Border { get; init; }

    /// <summary>Paleta por defecto (accesible y distinguible también en blanco y negro por luminancia).</summary>
    public static IReadOnlyList<Color> DefaultPalette { get; } =
    [
        Color.FromHex("#2563EB"), Color.FromHex("#F59E0B"), Color.FromHex("#10B981"), Color.FromHex("#EF4444"),
        Color.FromHex("#8B5CF6"), Color.FromHex("#06B6D4"), Color.FromHex("#EC4899"), Color.FromHex("#84CC16")
    ];

    /// <summary>Color efectivo del índice <paramref name="index"/> (serie o categoría).</summary>
    public Color ColorAt(int index)
    {
        var palette = Palette is { Count: > 0 } p ? p : DefaultPalette;
        return palette[index % palette.Count];
    }

    /// <summary>Construye los datos de un chart a partir de filas tipadas (sin reflection).</summary>
    public static (IReadOnlyList<string> Categories, IReadOnlyList<ChartSeries> Series) From<TRow>(
        IEnumerable<TRow> rows,
        Func<TRow, string> category,
        params (string Name, Func<TRow, double?> Value)[] series)
    {
        var list = rows.ToList();
        return (
            list.Select(category).ToList(),
            series.Select(s => new ChartSeries(s.Name, list.Select(s.Value).ToList())).ToList());
    }
}
