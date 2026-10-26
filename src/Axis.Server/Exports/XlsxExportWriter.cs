using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Axis.Server.Exports;

/// <summary>
/// Writes an export as an <c>xlsx</c> workbook with one worksheet, row by row, so no row is kept in
/// memory. Numbers are number cells that hold their exact digits and booleans are boolean cells. Dates,
/// instants and all other text are inline text cells in their ISO form, so a cell is never a formula
/// and never loses a time zone to a date serial number. The stream must be seekable.
/// </summary>
internal sealed class XlsxExportWriter : IExportWriter
{
    private const string SheetName = "Export";

    private readonly SpreadsheetDocument _document;
    private readonly WorksheetPart _sheet;
    private readonly OpenXmlWriter _writer;
    private IReadOnlyList<ExportColumn> _columns = [];

    public XlsxExportWriter(Stream stream)
    {
        _document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
        var workbook = _document.AddWorkbookPart();
        _sheet = workbook.AddNewPart<WorksheetPart>();
        _writer = OpenXmlWriter.Create(_sheet);
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

        var workbook = _document.WorkbookPart!;
        workbook.Workbook = new Workbook(new Sheets(new Sheet { Name = SheetName, SheetId = 1, Id = workbook.GetIdOfPart(_sheet) }));
        workbook.Workbook.Save();

        // Disposing the document writes the package into the stream.
        _document.Dispose();
    }

    public void Dispose()
    {
        _writer.Dispose();
        _document.Dispose();
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
