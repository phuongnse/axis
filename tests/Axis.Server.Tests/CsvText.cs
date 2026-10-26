using System.Text;

namespace Axis.Server.Tests;

/// <summary>A small CSV reader for the tests of the export files.</summary>
public static class CsvText
{
    /// <summary>Reads RFC 4180 CSV, in which a quoted cell may hold commas, quotes and line breaks. Every row ends with CRLF.</summary>
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (quoted)
            {
                if (current == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else if (current == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(current);
                }
            }
            else if (current == '"')
            {
                quoted = true;
            }
            else if (current == ',')
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row);
                row = [];
                index++;
            }
            else
            {
                cell.Append(current);
            }
        }

        Assert.False(quoted);
        Assert.Empty(cell.ToString());
        Assert.Empty(row);
        return rows;
    }
}
