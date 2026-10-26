using System.Text.RegularExpressions;

namespace Axis.Data.Records;

/// <summary>
/// One data row of an import file. <see cref="Number"/> counts the data rows from 1, so the header is not
/// counted. A row that could not be read has no <see cref="Cells"/> and says why in <see cref="Error"/>.
/// </summary>
public sealed record RecordImportFileRow(int Number, IReadOnlyList<string>? Cells, string? Error);

/// <summary>
/// An import file read into rows of text cells, whatever its format. The dry run checks the rows the same way
/// for every format.
/// </summary>
internal interface IRecordImportFile
{
    /// <summary>The cells of the header row, or <see langword="null"/> when the file has no row or the header could not be read.</summary>
    IReadOnlyList<string>? Header { get; }

    /// <summary>Why the header could not be read, or <see langword="null"/>.</summary>
    string? HeaderError { get; }

    /// <summary>The data rows, one at a time as the caller reads them. The rows can be read once.</summary>
    IEnumerable<RecordImportFileRow> Rows();
}

/// <summary>What the readers of import files and the columns that map their cells have in common.</summary>
internal static partial class RecordImportFiles
{
    /// <summary>Whether <paramref name="text"/> is written as a JSON number, so it can stand in a body as one.</summary>
    public static bool IsJsonNumber(string text) => JsonNumber().IsMatch(text);

    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex JsonNumber();
}
