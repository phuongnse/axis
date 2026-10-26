using System.IO.Packaging;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Axis.Server.Exports;

/// <summary>
/// Writes an export as an <c>xlsx</c> workbook with one worksheet, row by row, so no row is kept in
/// memory. The package is write-only, so the sheet streams to the stream as it is written. Numbers are number
/// cells that hold their exact digits and booleans are boolean cells. Dates, instants and all other text are
/// inline text cells in their ISO form, so a cell is never a formula and never loses a time zone to a date
/// serial number.
/// </summary>
internal sealed class XlsxExportWriter : IExportWriter
{
    private const string SheetName = "Export";
    private const string SheetRelationshipId = "rId1";
    private const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string WorksheetContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";
    private const string OfficeDocumentRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string WorksheetRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";
    private static readonly Uri _workbookUri = new("/xl/workbook.xml", UriKind.Relative);
    private static readonly Uri _worksheetUri = new("/xl/worksheets/sheet1.xml", UriKind.Relative);

    private readonly Package _package;
    private readonly Stream _sheetStream;
    private readonly OpenXmlWriter _writer;
    private IReadOnlyList<ExportColumn> _columns = [];

    // The SDK cannot add parts to a write-only package, because it reads the relationships first, so the parts are written directly.
    public XlsxExportWriter(Stream stream)
    {
        _package = Package.Open(stream, FileMode.Create, FileAccess.Write);

        var workbookPart = _package.CreatePart(_workbookUri, WorkbookContentType);
        _package.CreateRelationship(_workbookUri, TargetMode.Internal, OfficeDocumentRelationship);
        using (var workbookStream = workbookPart.GetStream(FileMode.Create, FileAccess.Write))
        using (var workbookWriter = OpenXmlWriter.Create(workbookStream))
        {
            workbookWriter.WriteElement(new Workbook(new Sheets(new Sheet { Name = SheetName, SheetId = 1, Id = SheetRelationshipId })));
        }

        var sheetPart = _package.CreatePart(_worksheetUri, WorksheetContentType);
        workbookPart.CreateRelationship(_worksheetUri, TargetMode.Internal, WorksheetRelationship, SheetRelationshipId);
        _sheetStream = sheetPart.GetStream(FileMode.Create, FileAccess.Write);
        _writer = OpenXmlWriter.Create(_sheetStream);
        _writer.WriteStartElement(new Worksheet());
        _writer.WriteStartElement(new SheetData());
    }

    public void WriteHeader(IReadOnlyList<ExportColumn> columns)
    {
        _columns = columns;
        _writer.WriteStartElement(new Row());
        foreach (var column in columns)
        {
            _writer.WriteElement(TextCell(column.Name));
        }

        _writer.WriteEndElement();
    }

    public void WriteRow(IReadOnlyList<string?> cells)
    {
        _writer.WriteStartElement(new Row());
        for (var index = 0; index < cells.Count; index++)
        {
            _writer.WriteElement(Cell(cells[index], _columns[index]));
        }

        _writer.WriteEndElement();
    }

    public void Complete()
    {
        _writer.WriteEndElement();
        _writer.WriteEndElement();
        _writer.Close();
        _sheetStream.Dispose();
        _package.Close();
    }

    public void Dispose()
    {
        _writer.Dispose();
        _sheetStream.Dispose();
        _package.Close();
    }

    private static Cell Cell(string? text, ExportColumn column)
    {
        if (text is null)
        {
            return new Cell();
        }

        if (ExportValues.IsNumeric(column.Type))
        {
            return new Cell { DataType = CellValues.Number, CellValue = new CellValue(text) };
        }

        return column.Type == Axis.Configuration.Model.FieldType.Boolean
            ? new Cell { DataType = CellValues.Boolean, CellValue = new CellValue(text == "true" ? "1" : "0") }
            : TextCell(text);
    }

    private static Cell TextCell(string text) =>
        new()
        {
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(XmlSafe(text)) { Space = SpaceProcessingModeValues.Preserve }),
        };

    // A character that XML cannot hold would fail the whole file, so it is shown as U+FFFD.
    private static string XmlSafe(string text)
    {
        if (text.All(XmlConvert.IsXmlChar))
        {
            return text;
        }

        var safe = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (XmlConvert.IsXmlChar(text[index]))
            {
                safe.Append(text[index]);
            }
            else if (index + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[index + 1], text[index]))
            {
                safe.Append(text[index]).Append(text[++index]);
            }
            else
            {
                safe.Append('�');
            }
        }

        return safe.ToString();
    }
}
