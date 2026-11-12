namespace Axis.ControlPlane.Admin;

/// <summary>Writes a table as the platform's admin tool does: each column as wide as its widest cell, two spaces between columns, and no trailing spaces.</summary>
internal static class TextTable
{
    public static void Write(TextWriter output, string[] header, IEnumerable<string[]> rows)
    {
        List<string[]> table = [header, .. rows];
        var widths = Enumerable.Range(0, header.Length).Select(column => table.Max(row => row[column].Length)).ToArray();
        foreach (var row in table)
        {
            output.WriteLine(string.Join("  ", row.Select((cell, column) => cell.PadRight(widths[column]))).TrimEnd());
        }
    }
}
