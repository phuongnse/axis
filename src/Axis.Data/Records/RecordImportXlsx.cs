using System.Globalization;
using System.IO.Compression;
using System.IO.Packaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Axis.Data.Records;

/// <summary>
/// Reads an import file that is an <c>xlsx</c> workbook, into the same rows as <see cref="RecordImportCsv"/>.
/// Only the first sheet of the workbook is read, and its first row with a value is the header. A data row's
/// <see cref="RecordImportFileRow.Number"/> is its sheet row minus the header's, so it is the number the user
/// sees in the spreadsheet, and a row with no value is skipped. A cell becomes text: a string is its text,
/// a number stays a number as written, a boolean is <c>true</c> or <c>false</c>, and a date is <c>yyyy-MM-dd</c>,
/// or <c>yyyy-MM-ddTHH:mm:ssZ</c> when it holds a time, which is read as UTC because a workbook has no time zone.
/// A formula cell is read from the value saved with it. A data row has an error when a cell holds an error value
/// or a date that does not exist, or when a cell lies to the right of the header. A workbook that cannot be read,
/// which includes rows that are not in ascending order, or that unpacks to more than 1,000 entries or 100 MiB, has a <see cref="HeaderError"/>. A workbook is parsed
/// as a stream of elements, so only one row is held at a time.
/// </summary>
public sealed class RecordImportXlsx : IRecordImportFile
{
    private const int MaxEntries = 1_000;

    private const long MaxUnpackedBytes = 100L * 1024 * 1024;

    private const long MaxCharactersInPart = 100_000_000;

    // The widest sheet of Excel.
    private const int MaxColumns = 16_384;

    private const string NotReadable = "Is not a readable xlsx workbook.";

    private const string TooLarge = "Unpacks to more than the import limit.";

    private readonly ReadOnlyMemory<byte> _file;
    private readonly int _headerRow;

    private RecordImportXlsx(ReadOnlyMemory<byte> file, int headerRow, IReadOnlyList<string>? header, string? headerError)
    {
        _file = file;
        _headerRow = headerRow;
        Header = header;
        HeaderError = headerError;
    }

    /// <summary>The cells of the first row with a value, or <see langword="null"/> when the sheet has none or the header could not be read.</summary>
    public IReadOnlyList<string>? Header { get; }

    /// <summary>Why the header could not be read, or <see langword="null"/>.</summary>
    public string? HeaderError { get; }

    /// <summary>Opens <paramref name="file"/> and reads its header row.</summary>
    public static RecordImportXlsx Open(ReadOnlyMemory<byte> file)
    {
        if (CheckLimits(file) is { } limit)
        {
            return new RecordImportXlsx(file, 0, null, limit);
        }

        try
        {
            using var rows = ReadSheet(file).GetEnumerator();
            while (rows.MoveNext())
            {
                var row = rows.Current;
                if (row.IsBlank)
                {
                    continue;
                }

                if (row.Error is not null)
                {
                    return new RecordImportXlsx(file, row.Index, null, row.Error);
                }

                var cells = row.Cells;
                while (cells[^1].Length == 0)
                {
                    cells.RemoveAt(cells.Count - 1);
                }

                return new RecordImportXlsx(file, row.Index, cells, null);
            }

            return new RecordImportXlsx(file, 0, null, null);
        }
        catch (Exception exception) when (IsReadError(exception))
        {
            return new RecordImportXlsx(file, 0, null, NotReadable);
        }
    }

    /// <summary>
    /// The data rows, one at a time as the caller reads them. The rows can be read once. A row with an error
    /// is returned with the error, and reading goes on with the next row, except after a failure to read the
    /// workbook, which is the last row.
    /// </summary>
    public IEnumerable<RecordImportFileRow> Rows()
    {
        if (Header is not { } header)
        {
            yield break;
        }

        using var rows = ReadSheet(_file).GetEnumerator();
        var number = 0;
        while (true)
        {
            var (row, failed) = Next(rows);
            if (failed)
            {
                yield return new RecordImportFileRow(number + 1, null, NotReadable);
                yield break;
            }

            if (row is null)
            {
                yield break;
            }

            if (row.Index <= _headerRow || row.IsBlank)
            {
                continue;
            }

            number = row.Index - _headerRow;
            if (row.Error is not null)
            {
                yield return new RecordImportFileRow(number, null, row.Error);
                continue;
            }

            var cells = row.Cells;
            var beyond = header.Count;
            while (beyond < cells.Count && cells[beyond].Length == 0)
            {
                beyond++;
            }

            if (beyond < cells.Count)
            {
                yield return new RecordImportFileRow(number, null, $"Has a value in column {ColumnLetters(beyond)}, beyond the header.");
                continue;
            }

            while (cells.Count < header.Count)
            {
                cells.Add("");
            }

            yield return new RecordImportFileRow(number, cells.Count > header.Count ? cells[..header.Count] : cells, null);
        }
    }

    private static (SheetRow? Row, bool Failed) Next(IEnumerator<SheetRow> rows)
    {
        try
        {
            return rows.MoveNext() ? (rows.Current, false) : (null, false);
        }
        catch (Exception exception) when (IsReadError(exception))
        {
            return (null, true);
        }
    }

    // What a workbook that is damaged, or not a workbook, throws as it is read.
    private static bool IsReadError(Exception exception) =>
        exception is InvalidDataException or OpenXmlPackageException or FileFormatException or XmlException
            or ArgumentException or FormatException or OverflowException or InvalidOperationException or NotSupportedException or IOException;

    // A zip bomb is refused by the entry count and the size the entries announce, before anything is unpacked.
    // Each part that is read is cut off at MaxCharactersInPart too.
    private static string? CheckLimits(ReadOnlyMemory<byte> file)
    {
        try
        {
            using var stream = OpenStream(file);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count > MaxEntries)
            {
                return TooLarge;
            }

            long unpacked = 0;
            foreach (var entry in archive.Entries)
            {
                unpacked += entry.Length;
                if (unpacked > MaxUnpackedBytes)
                {
                    return TooLarge;
                }
            }

            return null;
        }
        catch (Exception exception) when (IsReadError(exception))
        {
            return NotReadable;
        }
    }

    private static MemoryStream OpenStream(ReadOnlyMemory<byte> file) =>
        MemoryMarshal.TryGetArray(file, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(file.ToArray(), writable: false);

    // One row of the sheet: its number in the sheet, its cells with the gaps filled by empty text, and the
    // error of the first cell that could not be read.
    private sealed record SheetRow(int Index, List<string> Cells, string? Error)
    {
        public bool IsBlank => Error is null && Cells.TrueForAll(cell => cell.Length == 0);
    }

    // Reads the rows of the first sheet. The workbook is open while the caller reads the rows.
    private static IEnumerable<SheetRow> ReadSheet(ReadOnlyMemory<byte> file)
    {
        using var stream = OpenStream(file);
        using var document = SpreadsheetDocument.Open(stream, false, new OpenSettings { MaxCharactersInPart = MaxCharactersInPart });
        if (document.WorkbookPart is not { Workbook: { } workbook } workbookPart)
        {
            throw new InvalidDataException("The file has no workbook.");
        }

        if (workbook.Sheets?.Elements<Sheet>().FirstOrDefault()?.Id?.Value is not { } sheetId
            || workbookPart.GetPartById(sheetId) is not WorksheetPart sheetPart)
        {
            yield break;
        }

        var context = new CellContext(
            workbook.WorkbookProperties?.Date1904?.Value == true,
            DateStyles(workbookPart.WorkbookStylesPart?.Stylesheet),
            SharedStrings(workbookPart.SharedStringTablePart));

        using var reader = OpenXmlReader.Create(sheetPart);
        var previous = 0;
        while (reader.Read())
        {
            if (reader.ElementType != typeof(SheetData) || !reader.IsStartElement)
            {
                continue;
            }

            if (reader.ReadFirstChild())
            {
                do
                {
                    if (reader.ElementType == typeof(Row) && reader.LoadCurrentElement() is Row element)
                    {
                        var row = ReadRow(element, previous + 1, context);
                        if (row.Index <= previous)
                        {
                            throw new InvalidDataException("The rows are not in ascending order.");
                        }

                        previous = row.Index;
                        yield return row;
                    }
                }
                while (reader.ReadNextSibling());
            }

            yield break;
        }
    }

    private sealed record CellContext(bool Date1904, bool[] DateStyles, List<string> SharedStrings);

    private static SheetRow ReadRow(Row row, int fallbackIndex, CellContext context)
    {
        var index = row.RowIndex?.Value is { } written and > 0 and <= int.MaxValue ? (int)written : fallbackIndex;
        var cells = new List<string>();
        string? error = null;
        var next = 0;
        foreach (var cell in row.Elements<Cell>())
        {
            var reference = cell.CellReference?.Value;
            var column = reference is null ? next : ColumnIndex(reference);
            if (column is < 0 or >= MaxColumns)
            {
                error ??= $"Has a cell with the reference {reference} that is not valid.";
                continue;
            }

            reference ??= ColumnLetters(column) + index.ToString(CultureInfo.InvariantCulture);
            var (text, cellError) = ReadCell(cell, reference, context);
            error ??= cellError;
            while (cells.Count <= column)
            {
                cells.Add("");
            }

            cells[column] = text;
            next = column + 1;
        }

        return new SheetRow(index, cells, error);
    }

    // The text of a cell, or the reason it has none.
    private static (string Text, string? Error) ReadCell(Cell cell, string reference, CellContext context)
    {
        var value = cell.CellValue?.Text;
        var type = cell.DataType?.Value;
        if (type == CellValues.InlineString)
        {
            return (cell.InlineString is { } inline ? RichText(inline) : "", null);
        }

        if (string.IsNullOrEmpty(value))
        {
            return ("", null);
        }

        if (type == CellValues.SharedString)
        {
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var item) && item < context.SharedStrings.Count
                ? (context.SharedStrings[item], null)
                : ("", $"Cell {reference} holds a shared string that does not exist.");
        }

        if (type == CellValues.Boolean)
        {
            return value switch
            {
                "1" => ("true", null),
                "0" => ("false", null),
                _ => ("", $"Cell {reference} holds a value that is not a boolean."),
            };
        }

        if (type == CellValues.Error)
        {
            return ("", $"Cell {reference} holds the error {value}.");
        }

        if (type == CellValues.String || type == CellValues.Date)
        {
            return (value, null);
        }

        var isDate = cell.StyleIndex?.Value is { } style && style < context.DateStyles.Length && context.DateStyles[(int)style];
        return isDate ? ReadDate(value, reference, context.Date1904) : ReadNumber(value, reference);
    }

    private static (string Text, string? Error) ReadNumber(string value, string reference)
    {
        var text = value.Trim();
        if (RecordImportFiles.IsJsonNumber(text))
        {
            return (text, null);
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? (number.ToString("R", CultureInfo.InvariantCulture), null)
            : ("", $"Cell {reference} holds a value that is not a number.");
    }

    // The serial number of a date counts days from a base, and the fraction of a day is the time. In the 1900
    // system Excel counts a 29 February 1900 that never was, as serial 60, so the serials from 61 on count from
    // 30 December 1899 and the serials before it from 31 December 1899.
    private static (string Text, string? Error) ReadDate(string value, string reference, bool date1904)
    {
        const double MaxSerial = 2_958_465;
        if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
            || !double.IsFinite(serial)
            || serial < 0
            || serial >= MaxSerial)
        {
            return ("", $"Cell {reference} holds a date that does not exist.");
        }

        var days = Math.Floor(serial);
        var seconds = Math.Round((serial - days) * 86_400);
        if (seconds >= 86_400)
        {
            days++;
            seconds = 0;
        }

        if (!date1904 && days == 60)
        {
            return ("", $"Cell {reference} holds a date that does not exist.");
        }

        var origin = date1904 ? new DateTime(1904, 1, 1) : days < 60 ? new DateTime(1899, 12, 31) : new DateTime(1899, 12, 30);
        var moment = origin.AddDays(days).AddSeconds(seconds);
        return (moment.ToString(seconds == 0 ? "yyyy-MM-dd" : "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), null);
    }

    // Which cell styles, by index in cellXfs, show their number as a date or a time.
    private static bool[] DateStyles(Stylesheet? stylesheet)
    {
        if (stylesheet?.CellFormats is not { } formats)
        {
            return [];
        }

        var custom = new Dictionary<uint, bool>();
        foreach (var format in stylesheet.NumberingFormats?.Elements<NumberingFormat>() ?? [])
        {
            if (format.NumberFormatId?.Value is { } id)
            {
                custom[id] = IsDateCode(format.FormatCode?.Value ?? "");
            }
        }

        return formats.Elements<CellFormat>()
            .Select(format => format.NumberFormatId?.Value is { } id && (custom.TryGetValue(id, out var isDate) ? isDate : IsBuiltInDate(id)))
            .ToArray();
    }

    private static bool IsBuiltInDate(uint id) => id is (>= 14 and <= 22) or (>= 27 and <= 36) or (>= 45 and <= 47) or (>= 50 and <= 58);

    // A format code shows a date or a time when, outside its quoted text, escaped characters and [bracketed]
    // parts, it has a day, month, year, hour or second symbol.
    private static bool IsDateCode(string code)
    {
        var inQuote = false;
        var inBracket = false;
        for (var index = 0; index < code.Length; index++)
        {
            var character = code[index];
            if (inQuote)
            {
                inQuote = character != '"';
            }
            else if (inBracket)
            {
                inBracket = character != ']';
            }
            else if (character == '"')
            {
                inQuote = true;
            }
            else if (character == '[')
            {
                inBracket = true;
            }
            else if (character == '\\')
            {
                index++;
            }
            else if (char.ToLowerInvariant(character) is 'd' or 'm' or 'y' or 'h' or 's')
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> SharedStrings(SharedStringTablePart? part)
    {
        var strings = new List<string>();
        if (part is null)
        {
            return strings;
        }

        using var reader = OpenXmlReader.Create(part);
        while (reader.Read())
        {
            if (reader.ElementType != typeof(SharedStringTable) || !reader.IsStartElement)
            {
                continue;
            }

            if (reader.ReadFirstChild())
            {
                do
                {
                    if (reader.ElementType == typeof(SharedStringItem) && reader.LoadCurrentElement() is { } item)
                    {
                        strings.Add(RichText(item));
                    }
                }
                while (reader.ReadNextSibling());
            }

            break;
        }

        return strings;
    }

    // The text of a string item: its <t> element, or the <t> of each of its runs. The phonetic runs, which hold
    // the reading of the text, are left out.
    private static string RichText(OpenXmlElement item)
    {
        var text = new StringBuilder();
        foreach (var child in item.ChildElements)
        {
            switch (child)
            {
                case Text plain:
                    text.Append(plain.Text);
                    break;
                case Run run:
                    foreach (var part in run.Elements<Text>())
                    {
                        text.Append(part.Text);
                    }

                    break;
            }
        }

        return text.ToString();
    }

    // The 0-based column of a reference such as AB12, or -1 when it names no column.
    private static int ColumnIndex(string reference)
    {
        var column = 0;
        var letters = 0;
        while (letters < reference.Length && char.IsAsciiLetter(reference[letters]))
        {
            if (++letters > 3)
            {
                return -1;
            }

            column = (column * 26) + (char.ToUpperInvariant(reference[letters - 1]) - 'A' + 1);
        }

        return letters == 0 ? -1 : column - 1;
    }

    private static string ColumnLetters(int index)
    {
        var letters = new StringBuilder();
        for (var number = index + 1; number > 0; number = (number - 1) / 26)
        {
            letters.Insert(0, (char)('A' + ((number - 1) % 26)));
        }

        return letters.ToString();
    }
}
