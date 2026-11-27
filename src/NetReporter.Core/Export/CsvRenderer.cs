using System.Globalization;
using System.Text;
using NetReporter.Core.Bands;
using NetReporter.Core.Definition;
using NetReporter.Core.Elements;
using NetReporter.Core.Expressions;

namespace NetReporter.Core.Export;

/// <summary>Options for <see cref="CsvRenderer"/>.</summary>
public sealed record CsvRenderOptions
{
    /// <summary>Field separator. Default <c>,</c>.</summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>Writes a UTF-8 byte order mark so Excel opens accented text correctly. Default <c>true</c>.</summary>
    public bool ByteOrderMark { get; init; } = true;

    /// <summary>Writes the table's <see cref="TableElement{TRow}.Summary"/> (grand total) as the last line. Default <c>true</c>.</summary>
    public bool IncludeSummary { get; init; } = true;

    /// <summary>
    /// Prefixes an apostrophe to a field that a spreadsheet would read as a formula (<c>=</c>, <c>@</c>, a tab, a
    /// carriage return, or <c>+</c>/<c>-</c> not followed by a digit: OWASP "CSV injection"). <c>+15551234</c> and
    /// <c>-3.5</c> stay as they are. Default <c>true</c>.
    /// </summary>
    public bool EscapeFormulas { get; init; } = true;

    /// <summary>
    /// Culture of the data values. Default invariant: numbers without thousands separators and with a dot, dates as
    /// <c>yyyy-MM-dd</c> / <c>yyyy-MM-dd HH:mm:ss</c>, so the file is data rather than a printout. Column formats are not
    /// applied.
    /// </summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    /// <summary>Line terminator. Default <c>\r\n</c> (RFC 4180).</summary>
    public string NewLine { get; init; } = "\r\n";
}

/// <summary>
/// Data export of a report: the FIRST table (in band order, skipping hidden bands and elements) as CSV — one header line
/// with the column headers, one line per row with its raw values (grouped tables are flattened; group headers and footers
/// are left out because the key is already a column), and the summary line if the table has one. Titles, charts and
/// images are not data and are left out. A report without a table has nothing to export (<see cref="HasTable"/>).
/// </summary>
public sealed class CsvRenderer
{
    /// <summary>Whether <paramref name="report"/> has a table to export.</summary>
    public static bool HasTable(ReportDefinition report) => FindTable(report, new CsvEvaluationContext()) is not null;

    /// <summary>The CSV of the report's first table; a report without one gives just the byte order mark (if any).</summary>
    public byte[] Render(ReportDefinition report, CsvRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        options ??= new CsvRenderOptions();
        var ctx = new CsvEvaluationContext { Culture = report.Culture };
        var text = new StringBuilder();
        if (FindTable(report, ctx) is { } table)
            table.Accept(new Writer(text, options, ctx));

        var encoding = new UTF8Encoding(options.ByteOrderMark);
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(text.ToString())];
    }

    private static ITableElement? FindTable(ReportDefinition report, CsvEvaluationContext ctx)
    {
        foreach (var band in report.Bands)
        {
            if (band.Visible is { } bv && !bv.Evaluate(ctx)) continue;
            foreach (var element in band.Elements)
            {
                if (element is ITableElement table && (element.Visible is not { } ev || ev.Evaluate(ctx)))
                    return table;
            }
        }
        return null;
    }

    private sealed class Writer(StringBuilder text, CsvRenderOptions options, CsvEvaluationContext ctx) : ITableVisitor<bool>
    {
        public bool Visit<TRow>(TableElement<TRow> table)
        {
            Line(table.Columns.Select(c => c.Header));
            for (int i = 0; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i];
                Line(table.Columns.Select(c => Value(c.Binding.Evaluate(row))));
            }

            if (options.IncludeSummary && table.Summary is { } summary && table.Rows.Count > 0)
            {
                ctx.CurrentRow = null;
                ctx.GroupKey = null;
                ctx.GroupRowCount = table.Rows.Count;
                Line(table.Columns.Select((column, c) => c < summary.Cells.Count && summary.Cells[c] is { } cell
                    ? cell.Aggregate is { } kind
                        ? Value(TableGrouping.Aggregate(kind, table.Rows.Select(r => column.Binding.Evaluate(r))))
                        : cell.Content?.Evaluate(ctx)
                    : null));
            }
            return true;
        }

        private void Line(IEnumerable<string?> fields)
        {
            var first = true;
            foreach (var field in fields)
            {
                if (!first) text.Append(options.Delimiter);
                first = false;
                text.Append(Field(field));
            }
            text.Append(options.NewLine);
        }

        private string? Value(object? value) => value switch
        {
            null => null,
            string s => s,
            bool b => b ? "true" : "false",
            DateTime d => d.TimeOfDay == TimeSpan.Zero
                ? d.ToString("yyyy-MM-dd", options.Culture)
                : d.ToString("yyyy-MM-dd HH:mm:ss", options.Culture),
            DateTimeOffset d => d.ToString("yyyy-MM-dd HH:mm:ss", options.Culture),
            DateOnly d => d.ToString("yyyy-MM-dd", options.Culture),
            TimeOnly t => t.ToString("HH:mm:ss", options.Culture),
            double d => d.ToString("R", options.Culture),
            float f => f.ToString("R", options.Culture),
            IFormattable f => f.ToString(null, options.Culture),
            _ => value.ToString(),
        };

        private string Field(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (options.EscapeFormulas && IsFormula(value)) value = "'" + value;
            return value.IndexOfAny([options.Delimiter, '"', '\n', '\r']) >= 0
                ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
                : value;
        }

        private static bool IsFormula(string value) =>
            value[0] is '=' or '@' or '\t' or '\r'
            || (value[0] is '+' or '-' && (value.Length == 1 || !(char.IsAsciiDigit(value[1]) || value[1] == '.')));
    }

    /// <summary>Context for the visibility conditions and summary contents: no pagination, no parameters.</summary>
    private sealed class CsvEvaluationContext : IEvaluationContext
    {
        public int PageNumber => 1;
        public int TotalPages => 1;
        public int RowIndex { get; set; }
        public object? CurrentRow { get; set; }
        public object? GroupKey { get; set; }
        public int GroupRowCount { get; set; }
        public IReadOnlyList<object?> GroupKeys => [];
        public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;
        public object? GetParameter(string name) => null;
        public object? GetAggregate(string name) => null;
    }
}
