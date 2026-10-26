using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Axis.Data.Tests;

/// <summary>
/// Builds <c>xlsx</c> workbooks from raw XML, so a test controls every part of the file. Cell styles are 0 for
/// General, 1 for the built-in date format 14 and 2 for the custom format <c>yyyy-mm-dd hh:mm</c>.
/// </summary>
internal static class XlsxFiles
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>A workbook with one sheet whose rows are text cells, and numbers for the cells that read as numbers. An empty cell is left out.</summary>
    public static byte[] Rows(params string[][] rows) =>
        Workbook([Sheet(string.Concat(rows.Select((row, index) => Row(index + 1, row))))]);

    /// <summary>The XML of a sheet whose <c>sheetData</c> holds <paramref name="rows"/>.</summary>
    public static string Sheet(string rows) =>
        $"""<worksheet xmlns="{Main}"><sheetData>{rows}</sheetData></worksheet>""";

    /// <summary>One row of inline strings, and numbers for the cells that read as numbers.</summary>
    public static string Row(int number, string[] cells)
    {
        var xml = new StringBuilder($"<row r=\"{number}\">");
        for (var index = 0; index < cells.Length; index++)
        {
            var reference = $"{(char)('A' + index)}{number}";
            if (cells[index].Length == 0)
            {
                continue;
            }

            xml.Append(double.TryParse(cells[index], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                ? $"""<c r="{reference}"><v>{cells[index]}</v></c>"""
                : $"""<c r="{reference}" t="inlineStr"><is><t xml:space="preserve">{SecurityElement.Escape(cells[index])}</t></is></c>""");
        }

        return xml.Append("</row>").ToString();
    }

    /// <summary>A workbook with the given sheets in order, and the shared strings when there are any.</summary>
    public static byte[] Workbook(IReadOnlyList<string> sheets, IReadOnlyList<string>? sharedStrings = null, bool date1904 = false)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var sheetTypes = string.Concat(sheets.Select((_, index) => $"""<Override PartName="/xl/worksheets/sheet{index + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>"""));
            var strings = sharedStrings is null ? "" : """<Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>""";
            Add(
                archive,
                "[Content_Types].xml",
                $"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                {sheetTypes}{strings}
                </Types>
                """);
            Add(archive, "_rels/.rels", $"""<Relationships xmlns="{PackageRelationships}"><Relationship Id="rId1" Type="{Relationships}/officeDocument" Target="xl/workbook.xml"/></Relationships>""");

            var sheetElements = string.Concat(sheets.Select((_, index) => $"""<sheet name="Sheet{index + 1}" sheetId="{index + 1}" r:id="rId{index + 1}"/>"""));
            var properties = date1904 ? "<workbookPr date1904=\"1\"/>" : "";
            Add(archive, "xl/workbook.xml", $"""<workbook xmlns="{Main}" xmlns:r="{Relationships}">{properties}<sheets>{sheetElements}</sheets></workbook>""");

            var sheetRelationships = string.Concat(sheets.Select((_, index) => $"""<Relationship Id="rId{index + 1}" Type="{Relationships}/worksheet" Target="worksheets/sheet{index + 1}.xml"/>"""));
            var stringsRelationship = sharedStrings is null ? "" : $"""<Relationship Id="rId100" Type="{Relationships}/sharedStrings" Target="sharedStrings.xml"/>""";
            var stylesRelationship = $"""<Relationship Id="rId101" Type="{Relationships}/styles" Target="styles.xml"/>""";
            Add(archive, "xl/_rels/workbook.xml.rels", $"""<Relationships xmlns="{PackageRelationships}">{sheetRelationships}{stringsRelationship}{stylesRelationship}</Relationships>""");

            Add(
                archive,
                "xl/styles.xml",
                $"""
                <styleSheet xmlns="{Main}">
                <numFmts count="1"><numFmt numFmtId="164" formatCode="yyyy\-mm\-dd\ hh:mm"/></numFmts>
                <cellXfs count="3"><xf numFmtId="0"/><xf numFmtId="14"/><xf numFmtId="164"/></cellXfs>
                </styleSheet>
                """);

            if (sharedStrings is not null)
            {
                Add(archive, "xl/sharedStrings.xml", $"""<sst xmlns="{Main}">{string.Concat(sharedStrings.Select(text => $"<si><t>{SecurityElement.Escape(text)}</t></si>"))}</sst>""");
            }

            for (var index = 0; index < sheets.Count; index++)
            {
                Add(archive, $"xl/worksheets/sheet{index + 1}.xml", sheets[index]);
            }
        }

        return stream.ToArray();
    }

    /// <summary>A zip file with <paramref name="entries"/> empty entries, which is no workbook.</summary>
    public static byte[] ZipWithEntries(int entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var index = 0; index < entries; index++)
            {
                archive.CreateEntry($"entry{index}.txt");
            }
        }

        return stream.ToArray();
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
