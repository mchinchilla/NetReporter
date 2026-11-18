namespace NetReporter.Viewer.Desktop;

/// <summary>Formats a desktop viewer can export the open document to.</summary>
public enum ExportFormat
{
    Pdf,
    Xlsx,
    Html,
}

public static class ExportFormats
{
    /// <summary>File extension without the dot ("pdf", "xlsx", "html").</summary>
    public static string Extension(this ExportFormat format) => format switch
    {
        ExportFormat.Pdf => "pdf",
        ExportFormat.Xlsx => "xlsx",
        ExportFormat.Html => "html",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    /// <summary>MIME type of the exported bytes.</summary>
    public static string ContentType(this ExportFormat format) => format switch
    {
        ExportFormat.Pdf => "application/pdf",
        ExportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ExportFormat.Html => "text/html; charset=utf-8",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };
}
