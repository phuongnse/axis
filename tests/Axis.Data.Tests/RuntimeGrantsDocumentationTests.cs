using System.Text.RegularExpressions;
using Axis.Data.Storage;

namespace Axis.Data.Tests;

/// <summary>Compares the storage reference's list of the <c>axis</c> tables that runtime writes with the grant step's list.</summary>
public sealed class RuntimeGrantsDocumentationTests
{
    private const string RowStart = "| `axis` tables that runtime writes:";

    [Fact]
    public void Runtime_written_tables_row_matches_RuntimeWrittenTables()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "reference", "storage.md");
        var rows = File.ReadAllLines(path).Where(line => line.StartsWith(RowStart, StringComparison.Ordinal)).ToList();
        Assert.True(rows.Count == 1, $"Expected exactly one row that starts with '{RowStart}' in docs/reference/storage.md, found {rows.Count}.");

        // The first cell lists the tables. A name with a trailing * stands for every table that starts with it.
        var names = Regex.Matches(rows[0].Split('|')[1][(RowStart.Length - 1)..], @"`([a-z_]+\*?)`")
            .Select(match => match.Groups[1].Value)
            .ToList();
        var tables = RuntimeRoleGrants.RuntimeWrittenTables;

        var unknown = names.Where(name => !tables.Any(table => Matches(name, table))).ToList();
        var undocumented = tables.Where(table => !names.Any(name => Matches(name, table))).ToList();

        Assert.True(unknown.Count == 0, $"Not in RuntimeWrittenTables: {string.Join(", ", unknown)}.");
        Assert.True(undocumented.Count == 0, $"Missing from the runtime row of docs/reference/storage.md: {string.Join(", ", undocumented)}.");
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    private static bool Matches(string name, string table) =>
        name.EndsWith('*') ? table.StartsWith(name[..^1], StringComparison.Ordinal) : name == table;

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
