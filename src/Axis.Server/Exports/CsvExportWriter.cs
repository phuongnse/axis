using System.Text;
using Axis.Configuration.Model;

namespace Axis.Server.Exports;

/// <summary>
/// Writes an export as RFC 4180 CSV in UTF-8 with a byte order mark, so Excel reads non-ASCII text,
/// and with CRLF line endings. A text-like cell that starts with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a
/// tab or a carriage return gets a leading <c>'</c>, so a spreadsheet never reads it as a formula. A number
/// is never prefixed: it cannot be a formula, and a prefix would break its machine form.
/// </summary>
internal sealed class CsvExportWriter(Stream stream) : IExportWriter
{
    private const string LineEnd = "\r\n";

    private readonly StreamWriter _writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true) { NewLine = LineEnd };
    private IReadOnlyList<ExportColumn> _columns = [];

    public void WriteHeader(IReadOnlyList<ExportColumn> columns)
    {
        _columns = columns;
        Write(columns.Select(column => Quote(column.Name)));
    }

    public void WriteRow(IReadOnlyList<string?> cells)
    {
        Write(cells.Select((cell, index) => Quote(cell is null ? "" : Neutralise(cell, _columns[index].Type))));
    }

    public void Complete() => _writer.Flush();

    public void Dispose() => _writer.Dispose();

    private void Write(IEnumerable<string> cells)
    {
        _writer.Write(string.Join(',', cells));
        _writer.Write(LineEnd);
    }

    private static string Neutralise(string cell, FieldType type) =>
        !ExportValues.IsNumeric(type) && cell.Length > 0 && cell[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "'" + cell
            : cell;

    private static string Quote(string cell) =>
        cell.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : cell;
}
