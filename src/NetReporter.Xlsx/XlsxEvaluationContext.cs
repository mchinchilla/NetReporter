using System.Globalization;
using NetReporter.Core.Expressions;

namespace NetReporter.Xlsx;

/// <summary>
/// Contexto de evaluación que el renderer XLSX usa para resolver expresiones
/// (TextElement.Content, GroupHeader.Content, GroupFooterCell.Content). No hay
/// paginación real en XLSX, así que PageNumber/TotalPages quedan en 1/1.
/// </summary>
internal sealed class XlsxEvaluationContext : IEvaluationContext
{
    public int PageNumber { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int RowIndex { get; set; }
    public object? CurrentRow { get; set; }
    public object? GroupKey { get; set; }
    public int GroupRowCount { get; set; }
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    public object? GetParameter(string name) => null;
    public object? GetAggregate(string name) => null;
}
