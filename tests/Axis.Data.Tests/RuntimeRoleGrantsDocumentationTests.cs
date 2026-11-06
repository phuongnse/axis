using System.Text.RegularExpressions;
using Axis.Data.Storage;

namespace Axis.Data.Tests;

/// <summary>
/// Compares the runtime grant rows of the access table in <c>docs/reference/storage.md#database-roles</c> with
/// <see cref="RuntimeRoleGrants"/>, so a table that the grant step gives to the runtime role is named in the docs.
/// </summary>
public sealed class RuntimeRoleGrantsDocumentationTests
{
    // The two tables that "the receipts" stands for in the docs.
    private static readonly string[] _receipts = ["endpoint_receipts", "process_start_receipts"];

    [Fact]
    public void The_runtime_writes_row_names_exactly_the_runtime_written_tables()
    {
        var row = Row("| `axis` tables that runtime writes:");
        var documented = Names(row["| `axis` tables that runtime writes:".Length..], RuntimeRoleGrants.RuntimeWrittenTables);

        Assert.Equal(RuntimeRoleGrants.RuntimeWrittenTables.Order(StringComparer.Ordinal), documented.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_insert_only_row_names_exactly_the_runtime_inserted_tables()
    {
        var row = Row("| `publish_requests`");
        var documented = Names(row.Split('|')[1], RuntimeRoleGrants.RuntimeInsertedTables);

        Assert.Equal(RuntimeRoleGrants.RuntimeInsertedTables.Order(StringComparer.Ordinal), documented.Order(StringComparer.Ordinal));
    }

    // The names in the first cell of a row: a plain name, a `prefix_*` pattern that stands for the code's tables with that prefix,
    // and "the receipts", which stands for the two receipt tables. A table that two entries name counts once.
    private static List<string> Names(string cell, IReadOnlySet<string> tables)
    {
        var names = new List<string>();
        foreach (var name in Regex.Matches(cell, "`([a-z_*]+)`").Select(match => match.Groups[1].Value))
        {
            if (name.EndsWith('*'))
            {
                var prefix = name[..^1];
                names.AddRange(tables.Where(table => table.StartsWith(prefix, StringComparison.Ordinal)));
            }
            else
            {
                names.Add(name);
            }
        }

        if (cell.Contains("the receipts", StringComparison.Ordinal))
        {
            names.AddRange(_receipts);
        }

        return [.. names.Distinct(StringComparer.Ordinal)];
    }

    private static string Row(string start)
    {
        var rows = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "reference", "storage.md"))
            .Where(line => line.StartsWith(start, StringComparison.Ordinal))
            .ToList();
        Assert.True(rows.Count == 1, $"Expected one row of the access table that starts with '{start}', found {rows.Count}.");
        return rows[0];
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
