using System.Buffers;
using System.Text;

namespace Axis.Data.Records;

/// <summary>
/// One data row of an import file. <see cref="Number"/> counts the data rows from 1, so the header is not
/// counted. A row that could not be read has no <see cref="Cells"/> and says why in <see cref="Error"/>.
/// </summary>
public sealed record RecordImportCsvRow(int Number, IReadOnlyList<string>? Cells, string? Error);

/// <summary>
/// Reads an import file: UTF-8 text in CSV form with the quoting of RFC 4180. A cell in double quotes may hold
/// commas, line breaks (CRLF or LF) and doubled quotes, which stand for one quote. A leading byte order mark is
/// skipped, a row ends at CRLF, LF or CR, and the line break that ends the last row is no row. The first row is
/// the header. Every data row must have as many cells as the header. A row is decoded when it is read, so a
/// problem names its row: bytes that are not UTF-8, a quote in the middle of an unquoted cell, text after a
/// closing quote, a quote that is never closed, and a wrong number of cells. A reader holds only the file and
/// the position of the next row.
/// </summary>
public sealed class RecordImportCsv
{
    private static readonly UTF8Encoding _strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ReadOnlyMemory<byte> _file;
    private int _position;
    private int _number;

    private RecordImportCsv(ReadOnlyMemory<byte> file, int position, IReadOnlyList<string>? header, string? headerError)
    {
        _file = file;
        _position = position;
        Header = header;
        HeaderError = headerError;
    }

    /// <summary>The cells of the first row, or <see langword="null"/> when the file has no row or the header could not be read.</summary>
    public IReadOnlyList<string>? Header { get; }

    /// <summary>Why the header could not be read, or <see langword="null"/>.</summary>
    public string? HeaderError { get; }

    /// <summary>Opens <paramref name="file"/> and reads its header row.</summary>
    public static RecordImportCsv Open(ReadOnlyMemory<byte> file)
    {
        var position = file.Span.StartsWith("﻿"u8) ? 0 : 0;
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        if (file.Span.StartsWith(bom))
        {
            position = bom.Length;
        }

        if (position >= file.Length)
        {
            return new RecordImportCsv(file, position, null, null);
        }

        var row = ReadRow(file.Span, ref position);
        return new RecordImportCsv(file, position, row.Cells, row.Error);
    }

    /// <summary>
    /// The data rows, one at a time as the caller reads them. The rows can be read once. A row with an error
    /// is returned with the error, and reading goes on with the next row, except after a quote that is never
    /// closed, which takes the rest of the file as its last row.
    /// </summary>
    public IEnumerable<RecordImportCsvRow> Rows()
    {
        if (Header is not { } header)
        {
            yield break;
        }

        while (_position < _file.Length)
        {
            var row = ReadRow(_file.Span, ref _position);
            _number++;
            if (row.Error is not null)
            {
                yield return new RecordImportCsvRow(_number, null, row.Error);
            }
            else if (row.Cells!.Count != header.Count)
            {
                yield return new RecordImportCsvRow(_number, null, $"Has {row.Cells.Count} cells, but the header has {header.Count}.");
            }
            else
            {
                yield return new RecordImportCsvRow(_number, row.Cells, null);
            }
        }
    }

    private readonly record struct RawRow(List<string>? Cells, string? Error);

    // Reads the row at position and moves position past its line break.
    private static RawRow ReadRow(ReadOnlySpan<byte> file, ref int position)
    {
        var cells = new List<string>();
        string? error = null;
        while (true)
        {
            if (position < file.Length && file[position] == (byte)'"')
            {
                var (cell, cellError) = ReadQuoted(file, ref position);
                if (cellError is not null)
                {
                    SkipLine(file, ref position, whole: cellError == Unterminated);
                    return new RawRow(null, cellError);
                }

                cells.Add(cell!);
            }
            else
            {
                var start = position;
                while (position < file.Length && file[position] is not ((byte)',' or (byte)'\r' or (byte)'\n'))
                {
                    position++;
                }

                var bytes = file[start..position];
                if (bytes.Contains((byte)'"'))
                {
                    error ??= QuoteInCell;
                }
                else if (Decode(bytes) is { } text)
                {
                    cells.Add(text);
                }
                else
                {
                    error ??= NotUtf8;
                }
            }

            if (position < file.Length && file[position] == (byte)',')
            {
                position++;
                continue;
            }

            break;
        }

        EndLine(file, ref position);
        return error is null ? new RawRow(cells, null) : new RawRow(null, error);
    }

    private const string Unterminated = "Has a quote that is never closed.";

    private const string QuoteInCell = "Has a quote inside a cell that does not start with one.";

    private const string TextAfterQuote = "Has text after the closing quote of a cell.";

    private const string NotUtf8 = "Is not valid UTF-8.";

    // Reads a cell that starts with a quote. On an error, position is left where the problem is found.
    private static (string? Cell, string? Error) ReadQuoted(ReadOnlySpan<byte> file, ref int position)
    {
        position++;
        var buffer = new ArrayBufferWriter<byte>();
        while (true)
        {
            var quote = file[position..].IndexOf((byte)'"');
            if (quote < 0)
            {
                position = file.Length;
                return (null, Unterminated);
            }

            buffer.Write(file.Slice(position, quote));
            position += quote + 1;
            if (position < file.Length && file[position] == (byte)'"')
            {
                buffer.Write("\""u8);
                position++;
                continue;
            }

            break;
        }

        if (position < file.Length && file[position] is not ((byte)',' or (byte)'\r' or (byte)'\n'))
        {
            return (null, TextAfterQuote);
        }

        return Decode(buffer.WrittenSpan) is { } text ? (text, null) : (null, NotUtf8);
    }

    // Moves position to the end of the line, or to the end of the file after a quote that is never closed.
    private static void SkipLine(ReadOnlySpan<byte> file, ref int position, bool whole)
    {
        if (whole)
        {
            position = file.Length;
            return;
        }

        while (position < file.Length && file[position] is not ((byte)'\r' or (byte)'\n'))
        {
            position++;
        }

        EndLine(file, ref position);
    }

    private static void EndLine(ReadOnlySpan<byte> file, ref int position)
    {
        if (position < file.Length && file[position] == (byte)'\r')
        {
            position++;
        }

        if (position < file.Length && file[position] == (byte)'\n')
        {
            position++;
        }
    }

    private static string? Decode(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return _strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
