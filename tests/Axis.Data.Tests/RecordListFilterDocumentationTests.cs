using Axis.Data.Records;

namespace Axis.Data.Tests;

/// <summary>
/// Compares the operator table under paging and sorting in <c>docs/reference/record-api.md</c> with
/// <see cref="RecordListFilter.Operators"/>, so an operator that a filter accepts, and the kinds of field that take it, are documented.
/// </summary>
public sealed class RecordListFilterDocumentationTests
{
    [Fact]
    public void The_operator_table_names_exactly_the_operators_and_kinds_that_the_filter_accepts()
    {
        var documented = Table().ToDictionary(row => row.Operator, row => row.Kinds, StringComparer.Ordinal);

        Assert.Equal(RecordListFilter.Operators.Keys.Order(StringComparer.Ordinal), documented.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, kinds) in RecordListFilter.Operators)
        {
            Assert.Equal(kinds.Order(StringComparer.Ordinal), documented[name].Order(StringComparer.Ordinal));
        }
    }

    // The rows of the table whose header starts with Operator: the operator in code font, and its kinds in the second cell.
    private static List<(string Operator, string[] Kinds)> Table()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "reference", "record-api.md"));
        var headers = lines.Select((line, index) => (line, index)).Where(entry => entry.line.StartsWith("| Operator ", StringComparison.Ordinal)).ToList();
        Assert.True(headers.Count == 1, $"Expected one table with an Operator header, found {headers.Count}.");

        var rows = new List<(string, string[])>();
        foreach (var line in lines.Skip(headers[0].index + 2).TakeWhile(line => line.StartsWith('|')))
        {
            var cells = line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            rows.Add((cells[0].Trim('`'), cells[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
        }

        return rows;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Axis.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Axis.slnx was not found above the test output folder.");
    }
}
