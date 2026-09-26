namespace NetReporter.Designer.Models;

public sealed class PreviewRequest
{
    public string Yaml { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
}

public sealed class MoveRequest
{
    public string Yaml { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public double DeltaX { get; set; }
    public double DeltaY { get; set; }
}

public sealed class UpdateRequest
{
    public string Yaml { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public string? Style { get; set; }
    public string? Content { get; set; }
    public string? Color { get; set; }
    public double? Thickness { get; set; }
    public string? Fill { get; set; }

    // table
    public string? Rows { get; set; }
    public string? HeaderMode { get; set; }
    public double? HeaderHeight { get; set; }
    public double? RowHeight { get; set; }
    public string? HeaderStyle { get; set; }
    public string? RowStyle { get; set; }
    public string? AlternateRowStyle { get; set; }

    // común
    public string? Visible { get; set; }
    // rectangle
    public double? CornerRadius { get; set; }
    // image
    public string? Source { get; set; }
    public string? Fit { get; set; }
    // barcode
    public string? Value { get; set; }
    public string? BarcodeFormat { get; set; }
    public string? BarcodeForeground { get; set; }
    public string? BarcodeBackground { get; set; }
    // chart
    public string? ChartType { get; set; }
    public string? Category { get; set; }
    public string? Title { get; set; }
    public string? Legend { get; set; }
    public bool? ShowValues { get; set; }
    public bool? ShowPercent { get; set; }
    public bool? Stacked { get; set; }
    public string? ValueFormat { get; set; }
}

/// <summary>Operación sobre varios elementos: PathsJson = ["bands.0.elements.1", …].</summary>
public sealed class MultiPathRequest
{
    public string Yaml { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
    public string PathsJson { get; set; } = "[]";
}

/// <summary>MovesJson = [{ "path": "...", "dx": 1, "dy": 0 }, …].</summary>
public sealed class MoveManyRequest
{
    public string Yaml { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
    public string MovesJson { get; set; } = "[]";
}

public sealed class MovePayload
{
    public string? Path { get; set; }
    public double Dx { get; set; }
    public double Dy { get; set; }
}

public sealed class PasteRequest
{
    public string Yaml { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
    public string Clipboard { get; set; } = string.Empty;
    /// <summary>Banda destino; null = cada elemento vuelve a su banda original.</summary>
    public int? BandIndex { get; set; }
    public double OffsetX { get; set; } = 10;
    public double OffsetY { get; set; } = 10;
}

public sealed class UpdateSeriesRequest
{
    public string Yaml { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string SeriesJson { get; set; } = "[]";
}

public sealed class ChartSeriesPayload
{
    public string? Name { get; set; }
    public string? Value { get; set; }
    public string? Color { get; set; }
}

public sealed class AddRequest
{
    public string Yaml { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
    public int BandIndex { get; set; }
    public string Kind { get; set; } = "text";
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class DeleteRequest
{
    public string Yaml { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public sealed class DuplicateRequest
{
    public string Yaml { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public sealed class AddBandRequest
{
    public string Yaml { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
    public string Kind { get; set; } = "Detail";
    public double Height { get; set; } = 40;
}

public sealed class BandIndexRequest
{
    public string Yaml { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
    public int BandIndex { get; set; }
}

public sealed class MoveBandRequest
{
    public string Yaml { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
    public int FromIndex { get; set; }
    public int ToIndex { get; set; }
}

public sealed class SaveTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Yaml { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
}

public sealed class DeleteTemplateRequest
{
    public string Name { get; set; } = string.Empty;
}

public sealed class PreviewViewModel
{
    public IReadOnlyList<PreviewPage> Pages { get; init; } = Array.Empty<PreviewPage>();
    public IReadOnlyList<string> StyleNames { get; init; } = Array.Empty<string>();
    public string? Error { get; init; }
    public double PageWidthPt { get; init; }
    public double PageHeightPt { get; init; }
    public double MarginLeftPt { get; init; }
    public double MarginTopPt { get; init; }
}

public sealed record PreviewPage(
    int Number,
    string Svg,
    IReadOnlyList<InteractiveElement> Interactive,
    IReadOnlyList<BandRange> Bands);

public sealed record BandRange(
    int Index,
    string Kind,
    double Y,
    double Height,
    int ElementCount,
    double DeclaredHeight);

public sealed record InteractiveElement(
    string Path,
    string Kind,
    double X,
    double Y,
    double Width,
    double Height,
    string? Style,
    string? Content,
    string? Color,
    double? Thickness,
    string? Fill,
    // table-specific
    string? Rows = null,
    string? HeaderMode = null,
    double? HeaderHeight = null,
    double? RowHeight = null,
    string? HeaderStyle = null,
    string? RowStyle = null,
    string? AlternateRowStyle = null,
    int? ColumnCount = null,
    IReadOnlyList<TableColumnView>? Columns = null,
    // v1.1: visibilidad + image / barcode / chart / rectangle
    string? Visible = null,
    double? CornerRadius = null,
    string? Source = null,
    string? Fit = null,
    string? Value = null,
    string? BarcodeFormat = null,
    string? BarcodeForeground = null,
    string? BarcodeBackground = null,
    string? ChartType = null,
    string? Category = null,
    string? Title = null,
    string? Legend = null,
    bool? ShowValues = null,
    bool? ShowPercent = null,
    bool? Stacked = null,
    string? ValueFormat = null,
    IReadOnlyList<ChartSeriesView>? Series = null,
    // Bounds tal cual están en el YAML (relativos a la banda / content origin). X/Y/Width/Height de
    // arriba son el bounding box renderizado en coordenadas de página (para el overlay).
    double? BoundsX = null,
    double? BoundsY = null,
    double? BoundsWidth = null,
    double? BoundsHeight = null);

public sealed record ChartSeriesView(string Name, string Value, string Color);

public sealed record TableColumnView(
    string Header,
    string Binding,
    double Width,
    string? Format,
    string Align);

public sealed class UpdateColumnsRequest
{
    public string Yaml { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string ColumnsJson { get; set; } = "[]";
}

public sealed class TableColumnPayload
{
    public string? Header { get; set; }
    public string? Binding { get; set; }
    public double Width { get; set; }
    public string? Format { get; set; }
    public string? Align { get; set; }
}

public sealed class DesignerViewModel
{
    public string Yaml { get; init; } = string.Empty;
    public string Json { get; init; } = string.Empty;
}
