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
    double Height);

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
    int? ColumnCount = null);

public sealed class DesignerViewModel
{
    public string Yaml { get; init; } = string.Empty;
    public string Json { get; init; } = string.Empty;
}
