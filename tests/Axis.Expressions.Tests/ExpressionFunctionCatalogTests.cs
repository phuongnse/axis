using System.Text.RegularExpressions;
using Axis.Expressions.Functions;
using Axis.Expressions.Parsing;
using Axis.Expressions.Syntax;

namespace Axis.Expressions.Tests;

/// <summary>
/// The compiler's function list is the one the reference documents. Every function has an example that parses and calls it,
/// and the function and aggregate tables of docs/reference/expressions.md list its signatures and groups.
/// </summary>
public sealed class ExpressionFunctionCatalogTests
{
    private const string Reference = "docs/reference/expressions.md";

    public static TheoryData<string> Names
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in ExpressionFunctions.Names.Concat(ExpressionFunctions.AggregateNames))
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Every_function_has_an_example_that_parses_and_calls_it(string name)
    {
        var parsed = ExpressionParser.Parse(ExpressionFunctions.Get(name).Example);

        Assert.True(parsed.Succeeded);
        var call = Assert.IsType<CallNode>(parsed.Expression);
        Assert.Equal(name, call.Name, ignoreCase: true);
    }

    [Fact]
    public void The_function_tables_of_the_reference_list_the_signatures_and_groups_of_the_compiler()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), Reference));
        var (scalarHeader, scalarRows) = Table(lines, "## Functions");
        var (_, aggregateRows) = Table(lines, "### Aggregates");
        var groupColumn = Array.IndexOf(scalarHeader, "Group");
        var problems = new List<string>();

        Assert.True(groupColumn >= 0, "The function table has no Group column.");
        Compare(scalarRows, groupColumn, ExpressionFunctions.Names, aggregate: false, problems);
        Compare(aggregateRows, -1, ExpressionFunctions.AggregateNames, aggregate: true, problems);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static void Compare(
        IReadOnlyList<string[]> rows, int groupColumn, IReadOnlyList<string> names, bool aggregate, List<string> problems)
    {
        // A row marked _(planned for Mx)_ is not built yet, so it may name a function the compiler does not have.
        var documented = new Dictionary<string, List<(string Signature, string? Group)>>(StringComparer.Ordinal);
        var planned = new List<string>();
        foreach (var row in rows)
        {
            var signature = row[0].Trim('`');
            var name = signature[..signature.IndexOf('(', StringComparison.Ordinal)];
            if (row.Any(cell => Regex.IsMatch(cell, @"_\(planned for M\d+\)_")))
            {
                planned.Add(name);
                continue;
            }

            if (!documented.TryGetValue(name, out var listed))
            {
                documented[name] = listed = [];
            }

            listed.Add((signature, groupColumn >= 0 ? row[groupColumn] : null));
        }

        if (!aggregate)
        {
            foreach (var name in documented.Where(entry => entry.Value.Count > 1).Select(entry => entry.Key))
            {
                problems.Add($"'{name}' is listed more than once in {Reference}");
            }
        }

        foreach (var name in planned.Intersect(names, StringComparer.Ordinal))
        {
            problems.Add($"'{name}' is marked planned in {Reference} but is already in the compiler");
        }

        foreach (var name in names)
        {
            var compiled = ExpressionFunctions.Get(name);
            var listed = $"{name}({string.Join(", ", compiled.Parameters)})";
            if (!documented.TryGetValue(name, out var listings))
            {
                problems.Add($"'{name}' is in the compiler but not in {Reference}");
                continue;
            }

            // A count has a shorter and a longer row. The longer one is the signature the compiler lists.
            var doc = listings.MaxBy(entry => entry.Signature.Length);
            if (doc.Signature != listed)
            {
                problems.Add($"'{name}' is '{listed}' in the compiler but '{doc.Signature}' in {Reference}");
            }

            if (aggregate)
            {
                if (compiled.Group != "aggregate")
                {
                    problems.Add($"Aggregate '{name}' is in the group '{compiled.Group}', not 'aggregate'");
                }
            }
            else if (doc.Group != compiled.Group)
            {
                problems.Add($"'{name}' is in the group '{compiled.Group}' in the compiler but '{doc.Group}' in {Reference}");
            }
        }

        foreach (var name in documented.Keys.Except(names, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            problems.Add($"'{name}' is in {Reference} but not in the compiler");
        }
    }

    /// <summary>The header and the body rows of the first table after <paramref name="heading"/>.</summary>
    private static (string[] Header, List<string[]> Rows) Table(string[] lines, string heading)
    {
        var start = Array.IndexOf(lines, heading);
        var header = Enumerable.Range(start, lines.Length - start).First(index => lines[index].StartsWith("| Function", StringComparison.Ordinal));
        var rows = new List<string[]>();
        for (var index = header + 2; index < lines.Length && lines[index].StartsWith('|'); index++)
        {
            rows.Add([.. lines[index].Trim().Trim('|').Split('|').Select(cell => cell.Trim())]);
        }

        return ([.. lines[header].Trim().Trim('|').Split('|').Select(cell => cell.Trim())], rows);
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
