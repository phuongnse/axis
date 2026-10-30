using System.Buffers.Binary;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Axis.Data.Tests;

/// <summary>One fuzzed import file: <see cref="Kind"/> says how it was made, <see cref="File"/> holds its bytes.</summary>
internal sealed record ImportFuzzInput(string Kind, byte[] File);

/// <summary>
/// Makes the inputs of the import fuzz tests, as docs/reference/fuzz-tests.md describes: CSV text and
/// <c>xlsx</c> workbooks with random damage, and workbooks that pass the import limits. All randomness comes from
/// the <see cref="Random"/> that the caller passes, so a seed gives the same inputs. The bombs are real zip
/// files and are built once.
/// </summary>
internal static class ImportFuzzInputs
{
    /// <summary>The kind of the bomb whose directory announces a small size for a part that unpacks to more than the limit.</summary>
    public const string LyingBomb = "xlsx bomb with a lying size";

    private const int MaxCsvLength = 64 * 1024;

    private const long BombCharacters = 101L * 1024 * 1024;

    private const string SheetPath = "xl/worksheets/sheet1.xml";

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly Lazy<byte[][]> _bombs = new(BuildBombs);

    private static readonly byte[][] _csvTokens =
    [
        "\""u8.ToArray(), "\"\""u8.ToArray(), ","u8.ToArray(), "\r"u8.ToArray(), "\n"u8.ToArray(), "\r\n"u8.ToArray(),
        [0xEF, 0xBB, 0xBF], [0xFF], [0x80], [0xC0, 0xAF], [0xE2, 0x82], [0xED, 0xA0, 0x80], [0x00],
        "=SUM(A1)"u8.ToArray(), "@cmd"u8.ToArray(), "+1+1"u8.ToArray(), "'x"u8.ToArray(),
        Encoding.ASCII.GetBytes(new string('a', 5_000)), Encoding.ASCII.GetBytes(new string('"', 200)),
    ];

    private static readonly string[] _words = ["Desk", "Chair", "a,b", "say \"hi\"", "line1\nline2", "é", "日本", "", " ", "=SUM(A1)", "@cmd", "<x>", "&", "\u0001"];

    private static readonly string[] _values = ["1", "42", "-1", "10.50", "1e3", "007", "true", "FALSE", "2026-01-31", "2026-01-31T10:00:00Z", "abc", "99999999999999999999", "", "0.0000000001"];

    private static readonly string[] _numbers = ["1E400", "NaN", "-0", "0", "1.5", "-3", "1e3", "007", "0x10", "99999999999999999999", "", " 5 ", ".5", "1,5", "Infinity"];

    private static readonly string[] _serials = ["-1", "0", "1", "59", "60", "61", "45000", "45000.75", "2958465", "2958464.99999", "1E400", "abc", "", "0.99999999"];

    private static readonly string[] _references = ["AAAA1", "1A", "XFE1", "", "A", "a1", "XFD1", "ZZZ1", "A0", "A-1", "$A$1", "A1:B2"];

    private static readonly string[] _rowNumbers = ["0", "-1", "4294967295", "2147483647", "2147483648", "99999999999", "abc", "", " 3", "1.5"];

    private static readonly string[] _errors = ["#DIV/0!", "#N/A", "#REF!", "#VALUE!", "#NAME?", ""];

    /// <summary>A CSV file of <paramref name="header"/> and a few rows, with random edits, or random bytes, or UTF-16 text.</summary>
    public static ImportFuzzInput Csv(Random random, IReadOnlyList<string> header)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(header);

        switch (random.Next(8))
        {
            case 0:
                return new ImportFuzzInput("csv random bytes", RandomBytes(random, random.Next(0, 2_000)));
            case 1:
                var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(SeedCsv(random, header))).ToArray();
                return new ImportFuzzInput("csv utf-16", random.Next(2) == 0 ? utf16 : EditBytes(random, utf16, header));
        }

        var bytes = Encoding.UTF8.GetBytes(SeedCsv(random, header));
        for (var edits = random.Next(1, 5); edits > 0; edits--)
        {
            bytes = EditBytes(random, bytes, header);
        }

        return new ImportFuzzInput("csv", bytes.Length > MaxCsvLength ? bytes[..MaxCsvLength] : bytes);
    }

    /// <summary>An <c>xlsx</c> workbook of <paramref name="header"/> and a few rows, with odd cells, odd encodings or zip damage, or a bomb.</summary>
    public static ImportFuzzInput Xlsx(Random random, IReadOnlyList<string> header)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(header);

        switch (random.Next(8))
        {
            case 0:
                var variant = random.Next(3);
                return new ImportFuzzInput(variant == 2 ? LyingBomb : "xlsx bomb", _bombs.Value[variant]);
            case 1:
                return new ImportFuzzInput("xlsx cells", Structured(random, header));
            case 2:
            case 3:
                return new ImportFuzzInput("xlsx encoding", OddEncoding(random, Structured(random, header)));
            case 4:
                return new ImportFuzzInput("xlsx zip entries", ZipDamage(random, Structured(random, header)));
            case 5:
                return new ImportFuzzInput("xlsx directory", PatchDirectory(random, Structured(random, header)));
            case 6:
                return new ImportFuzzInput("xlsx bytes", DamageBytes(random, Structured(random, header)));
            default:
                var odd = OddEncoding(random, Structured(random, header));
                return new ImportFuzzInput("xlsx mixed", random.Next(2) == 0 ? PatchDirectory(random, odd) : DamageBytes(random, odd));
        }
    }

    /// <summary>A workbook under the import limits whose header row is one cell of <paramref name="characters"/> letters.</summary>
    public static byte[] WorkbookWithBigCell(long characters) => BigCellWorkbook(characters);

    /// <summary>A workbook whose header row is one cell of 101 MiB of letters, which unpacks to more than the import limit.</summary>
    public static byte[] SheetBomb() => _bombs.Value[0];

    /// <summary>Writes the kind and the bytes of <paramref name="input"/> so a failure can be pasted into a test, or the length of a long file.</summary>
    public static string Show(ImportFuzzInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input.File.Length < 4_096
            ? $"{input.Kind} Convert.FromBase64String(\"{Convert.ToBase64String(input.File)}\")"
            : $"{input.Kind} of {input.File.Length} bytes";
    }

    // CSV

    private static string SeedCsv(Random random, IReadOnlyList<string> header)
    {
        var csv = new StringBuilder(string.Join(',', header)).Append('\n');
        for (var rows = random.Next(1, 7); rows > 0; rows--)
        {
            csv.Append(string.Join(',', header.Select(_ => Quote(_values[random.Next(_values.Length)])))).Append(random.Next(4) == 0 ? "\r\n" : "\n");
        }

        return csv.ToString();
    }

    private static string Quote(string value) =>
        value.Length > 0 && value.Any(c => c is ',' or '"' or '\n') ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;

    // The same edits as the expression fuzz test, on bytes.
    private static byte[] EditBytes(Random random, byte[] bytes, IReadOnlyList<string> header)
    {
        var start = random.Next(bytes.Length + 1);
        var length = random.Next(Math.Min(12, bytes.Length - start) + 1);
        var token = _csvTokens[random.Next(_csvTokens.Length)];
        switch (random.Next(7))
        {
            case 0:
                return Splice(bytes, start, 0, token);
            case 1:
                return Splice(bytes, start, length, []);
            case 2:
                return Splice(bytes, start, length, token);
            case 3:
                return Splice(bytes, start, 0, bytes[start..(start + length)]);
            case 4:
                var other = Encoding.UTF8.GetBytes(SeedCsv(random, header));
                var from = random.Next(other.Length + 1);
                return Splice(bytes, start, 0, other[from..(from + random.Next(other.Length - from + 1))]);
            default:
                return random.Next(3) switch
                {
                    0 => [.. "\""u8, .. bytes, .. "\""u8],
                    1 => [.. bytes, .. bytes],
                    _ => [.. "\"\""u8, .. bytes],
                };
        }
    }

    private static byte[] Splice(byte[] bytes, int start, int remove, byte[] insert) =>
        [.. bytes.AsSpan(0, start), .. insert, .. bytes.AsSpan(start + remove)];

    private static byte[] RandomBytes(Random random, int length)
    {
        var bytes = new byte[length];
        random.NextBytes(bytes);
        return bytes;
    }

    // Workbooks with odd cells

    private static byte[] Structured(Random random, IReadOnlyList<string> header)
    {
        var shared = random.Next(2) == 0 ? new List<string>() : null;
        var rows = new StringBuilder(Row(random, 1, header.Select(name => (name, true)).ToList(), shared));
        var number = 1;
        for (var count = random.Next(0, 7); count > 0; count--)
        {
            number += random.Next(8) == 0 ? random.Next(-2, 100) : 1;
            rows.Append(Row(random, number, header.Select(_ => (_values[random.Next(_values.Length)], false)).ToList(), shared));
        }

        return XlsxFiles.Workbook([XlsxFiles.Sheet(rows.ToString())], shared, date1904: random.Next(6) == 0);
    }

    private static string Row(Random random, int number, List<(string Text, bool Plain)> values, List<string>? shared)
    {
        var written = random.Next(12) == 0 ? _rowNumbers[random.Next(_rowNumbers.Length)] : number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var row = new StringBuilder(random.Next(20) == 0 ? "<row>" : $"<row r=\"{written}\">");
        for (var column = 0; column < values.Count; column++)
        {
            var reference = random.Next(15) == 0 ? _references[random.Next(_references.Length)] : $"{(char)('A' + column)}{number}";
            var cell = Cell(random, reference, values[column].Text, values[column].Plain, shared);
            row.Append(cell);
            if (random.Next(20) == 0)
            {
                row.Append(cell);
            }
        }

        return row.Append("</row>").ToString();
    }

    private static string Cell(Random random, string reference, string text, bool plain, List<string>? shared)
    {
        var r = random.Next(10) == 0 ? "" : $" r=\"{reference}\"";
        var escaped = SecurityElement.Escape(text);
        if (plain && random.Next(4) != 0)
        {
            return shared is not null && random.Next(2) == 0
                ? $"<c{r} t=\"s\"><v>{Share(shared, text)}</v></c>"
                : $"<c{r} t=\"inlineStr\"><is><t>{escaped}</t></is></c>";
        }

        return random.Next(16) switch
        {
            0 => $"<c{r} t=\"inlineStr\"><is><t>{escaped}</t></is></c>",
            1 => shared is null ? $"<c{r} t=\"s\"><v>0</v></c>" : $"<c{r} t=\"s\"><v>{Share(shared, text)}</v></c>",
            2 => $"<c{r} t=\"s\"><v>{new[] { "99999", "-1", "x", "4294967296", "" }[random.Next(5)]}</v></c>",
            3 => $"<c{r}><v>{_numbers[random.Next(_numbers.Length)]}</v></c>",
            4 => $"<c{r} s=\"{random.Next(1, 3)}\"><v>{_serials[random.Next(_serials.Length)]}</v></c>",
            5 => $"<c{r} s=\"{random.Next(0, 6)}\" t=\"{(random.Next(2) == 0 ? "n" : "d")}\"><v>{_serials[random.Next(_serials.Length)]}</v></c>",
            6 => $"<c{r} t=\"b\"><v>{new[] { "0", "1", "2", "true", "" }[random.Next(5)]}</v></c>",
            7 => $"<c{r} t=\"e\"><v>{_errors[random.Next(_errors.Length)]}</v></c>",
            8 => $"<c{r}><f>SUM(A1:A2)</f><v>3</v></c>",
            9 => $"<c{r}><f>SUM(A1:A2)</f></c>",
            10 => $"<c{r} t=\"str\"><f>A1&amp;B1</f><v>{escaped}</v></c>",
            11 => $"<c{r}><f t=\"shared\" ref=\"A2:A3\" si=\"0\">A1+1</f><v>2</v></c>",
            12 => $"<c{r}><f t=\"shared\" si=\"0\"/><v>3</v></c>",
            13 => $"<c{r} t=\"inlineStr\"><is><r><t>{escaped}</t></r><rPh><t>x</t></rPh></is></c>",
            14 => $"<c{r} t=\"{new[] { "z", "", "inlineStr", "s", "str", "d", "n" }[random.Next(7)]}\"/>",
            _ => $"<c{r} t=\"inlineStr\"><is><t>{SecurityElement.Escape(_words[random.Next(_words.Length)])}</t></is></c>",
        };
    }

    private static int Share(List<string> shared, string text)
    {
        shared.Add(text);
        return shared.Count - 1;
    }

    // Odd encodings

    private static byte[] OddEncoding(Random random, byte[] workbook)
    {
        var part = new[] { SheetPath, SheetPath, SheetPath, "xl/workbook.xml", "xl/styles.xml", "[Content_Types].xml", "xl/_rels/workbook.xml.rels", "_rels/.rels", "xl/sharedStrings.xml" }[random.Next(9)];
        return Rewrite(workbook, archive =>
        {
            var entry = archive.GetEntry(part);
            if (entry is null)
            {
                return;
            }

            string xml;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
            {
                xml = reader.ReadToEnd();
            }

            entry.Delete();
            archive.CreateEntry(part).Open().Write(Reencode(random, xml));
        });
    }

    private static byte[] Reencode(Random random, string xml)
    {
        const string Declaration = "<?xml version=\"1.0\" encoding=\"{0}\"?>";
        switch (random.Next(14))
        {
            case 0:
                return [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(xml)];
            case 1:
                return Encoding.Unicode.GetBytes(xml);
            case 2:
                return Encoding.Unicode.GetBytes(string.Format(null, Declaration, "UTF-16") + xml);
            case 3:
                return [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(string.Format(null, Declaration, "utf-16") + xml)];
            case 4:
                return Encoding.UTF8.GetBytes(string.Format(null, Declaration, "UTF-16") + xml);
            case 5:
                return Encoding.Unicode.GetBytes(string.Format(null, Declaration, "UTF-8") + xml);
            case 6:
                return Encoding.Latin1.GetBytes(string.Format(null, Declaration, "ISO-8859-1") + xml);
            case 7:
                return Encoding.UTF8.GetBytes(string.Format(null, Declaration, "no-such-encoding") + xml);
            case 8:
                return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(xml)];
            case 9:
                return Encoding.UTF8.GetBytes(xml.Replace("<", "&#0;<", StringComparison.Ordinal));
            case 10:
                return Encoding.UTF8.GetBytes(xml.Replace("<t>", "<t>&#0;&#1;&#xD800;", StringComparison.Ordinal).Replace("<v>", "<v>&#0;", StringComparison.Ordinal));
            case 11:
                return Encoding.UTF8.GetBytes(BillionLaughs + xml.Replace("<t>", "<t>&lol9;", StringComparison.Ordinal).Replace("<sheetData>", "<sheetData>&lol9;", StringComparison.Ordinal));
            case 12:
                return Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]>" + xml.Replace("<t>", "<t>&e;", StringComparison.Ordinal));
            default:
                return [.. RandomBytes(random, random.Next(0, 40)), .. Encoding.UTF8.GetBytes(xml)];
        }
    }

    private const string BillionLaughs =
        "<!DOCTYPE lolz [<!ENTITY lol \"lol\"><!ENTITY lol1 \"&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;\">"
        + "<!ENTITY lol2 \"&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;\">"
        + "<!ENTITY lol3 \"&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;\">"
        + "<!ENTITY lol4 \"&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;\">"
        + "<!ENTITY lol5 \"&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;\">"
        + "<!ENTITY lol6 \"&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;\">"
        + "<!ENTITY lol7 \"&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;\">"
        + "<!ENTITY lol8 \"&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;\">"
        + "<!ENTITY lol9 \"&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;\">]>";

    // Zip-level damage

    private static byte[] ZipDamage(Random random, byte[] workbook)
    {
        var missing = new[] { "[Content_Types].xml", "_rels/.rels", SheetPath, "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml" };
        switch (random.Next(4))
        {
            case 0:
                var name = missing[random.Next(missing.Length)];
                return Rewrite(workbook, archive => archive.GetEntry(name)?.Delete());
            case 1:
                var copy = missing[random.Next(missing.Length)];
                return Rewrite(workbook, archive =>
                {
                    if (archive.GetEntry(copy) is not { } entry)
                    {
                        return;
                    }

                    using var content = new MemoryStream();
                    using (var source = entry.Open())
                    {
                        source.CopyTo(content);
                    }

                    var duplicate = archive.CreateEntry(copy);
                    using var target = duplicate.Open();
                    content.Position = 0;
                    content.CopyTo(target);
                });
            default:
                var count = random.Next(3) switch
                {
                    0 => random.Next(1, 11),
                    1 => random.Next(990, 1_010),
                    _ => random.Next(1, 3_001),
                };
                return Rewrite(workbook, archive =>
                {
                    for (var index = 0; index < count; index++)
                    {
                        archive.CreateEntry($"xl/media/image{index}.bin");
                    }
                });
        }
    }

    private static byte[] Rewrite(byte[] workbook, Action<ZipArchive> edit)
    {
        var stream = new MemoryStream();
        stream.Write(workbook);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            edit(archive);
        }

        return stream.ToArray();
    }

    // Changes a field of the central directory headers: the CRC, the sizes, the method and the offset of the local header.
    private static byte[] PatchDirectory(Random random, byte[] workbook)
    {
        var patched = (byte[])workbook.Clone();
        var headers = new List<int>();
        for (var index = 0; index + 4 <= patched.Length; index++)
        {
            if (patched[index] == 0x50 && patched[index + 1] == 0x4B && patched[index + 2] == 1 && patched[index + 3] == 2)
            {
                headers.Add(index);
            }
        }

        if (headers.Count == 0)
        {
            return patched;
        }

        for (var edits = random.Next(1, 4); edits > 0; edits--)
        {
            var header = headers[random.Next(headers.Count)];
            var (offset, width) = random.Next(6) switch
            {
                0 => (16, 4),
                1 => (20, 4),
                2 or 3 => (24, 4),
                4 => (10, 2),
                _ => (42, 4),
            };
            var value = random.Next(6) switch
            {
                0 => 0u,
                1 => uint.MaxValue,
                2 => 105_000_000u,
                3 => (uint)random.Next(),
                4 => (uint)random.Next(0, 70_000),
                _ => (uint)random.Next(8) switch { 0 => 8u, 1 => 9u, 2 => 12u, 3 => 14u, 4 => 93u, 5 => 99u, 6 => 0u, _ => 1u },
            };
            if (width == 2)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(patched.AsSpan(header + offset), (ushort)value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(patched.AsSpan(header + offset), value);
            }
        }

        return patched;
    }

    private static byte[] DamageBytes(Random random, byte[] workbook)
    {
        var bytes = workbook;
        for (var edits = random.Next(1, 6); edits > 0 && bytes.Length > 0; edits--)
        {
            var position = random.Next(bytes.Length);
            switch (random.Next(4))
            {
                case 0:
                    bytes = (byte[])bytes.Clone();
                    bytes[position] ^= (byte)(1 << random.Next(8));
                    break;
                case 1:
                    bytes = Splice(bytes, position, 0, RandomBytes(random, random.Next(1, 20)));
                    break;
                case 2:
                    bytes = Splice(bytes, position, Math.Min(random.Next(1, 20), bytes.Length - position), []);
                    break;
                default:
                    bytes = bytes[..position];
                    break;
            }
        }

        return bytes;
    }

    // Bombs

    // A: one cell of 101 MiB. B: a part of 101 MiB that no one reads. C: A with the directory announcing 1 KB.
    private static byte[][] BuildBombs()
    {
        var cell = BigCellWorkbook(BombCharacters);
        var part = BuildWorkbook(replaceSheet: false, archive =>
        {
            using var stream = archive.CreateEntry("xl/media/big.bin", CompressionLevel.Optimal).Open();
            WriteLetters(stream, BombCharacters);
        });
        var lying = (byte[])cell.Clone();
        for (var index = 0; index + 28 <= lying.Length; index++)
        {
            if (lying[index] == 0x50 && lying[index + 1] == 0x4B && lying[index + 2] == 1 && lying[index + 3] == 2
                && BinaryPrimitives.ReadUInt32LittleEndian(lying.AsSpan(index + 24)) > 1024 * 1024)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(lying.AsSpan(index + 24), 1024);
            }
        }

        return [cell, part, lying];
    }

    private static byte[] BigCellWorkbook(long characters) =>
        BuildWorkbook(replaceSheet: true, archive =>
        {
            using var stream = archive.CreateEntry(SheetPath, CompressionLevel.Optimal).Open();
            stream.Write(Encoding.UTF8.GetBytes($"<worksheet xmlns=\"{Main}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>"));
            WriteLetters(stream, characters);
            stream.Write("</t></is></c></row></sheetData></worksheet>"u8);
        });

    // A valid small workbook, copied into a new archive at the optimal level, and then changed by add.
    private static byte[] BuildWorkbook(bool replaceSheet, Action<ZipArchive> add)
    {
        using var source = new ZipArchive(new MemoryStream(XlsxFiles.Rows(["name"], ["Desk"])), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries.Where(entry => !replaceSheet || entry.FullName != SheetPath))
            {
                using var from = entry.Open();
                using var to = archive.CreateEntry(entry.FullName, CompressionLevel.Optimal).Open();
                from.CopyTo(to);
            }

            add(archive);
        }

        return output.ToArray();
    }

    // Writes `count` letters 'a' in chunks of 64 KiB, so the text is never held in one piece.
    private static void WriteLetters(Stream stream, long count)
    {
        var chunk = new byte[64 * 1024];
        Array.Fill(chunk, (byte)'a');
        for (var left = count; left > 0; left -= chunk.Length)
        {
            stream.Write(chunk, 0, (int)Math.Min(left, chunk.Length));
        }
    }
}
