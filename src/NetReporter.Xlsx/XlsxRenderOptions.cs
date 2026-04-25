namespace NetReporter.Xlsx;

/// <summary>
/// Opciones del <see cref="XlsxRenderer"/>.
/// </summary>
public sealed record XlsxRenderOptions
{
    /// <summary>Nombre de la worksheet. Default: el <c>ReportDefinition.Name</c> truncado a 31 chars.</summary>
    public string? WorksheetName { get; init; }

    /// <summary>
    /// Si <c>true</c>, los rangos de tablas se exportan como Excel Tables nativas
    /// (filtros, sort, banded rows). Default: <c>true</c>.
    /// </summary>
    public bool EmitNativeTables { get; init; } = true;

    /// <summary>Si <c>true</c>, agrega freeze pane debajo del header de cada tabla. Default: <c>false</c>.</summary>
    public bool FreezeTableHeaders { get; init; } = false;
}
