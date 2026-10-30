using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Resources;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Functions;

namespace Axis.Configuration.Tests;

/// <summary>
/// Compares facts stated in the docs with the code. The docs are found by heading or table
/// header anywhere under docs/, so sections may move between files.
/// </summary>
public sealed class DocumentationTests
{
    private const string SchemaResourcePrefix = "Axis.Configuration.Schemas.";
    private const string SchemaResourceSuffix = ".schema.json";

    [Fact]
    public void Diagnostic_code_table_matches_DiagnosticCodes()
    {
        var tables = new List<(List<string> Built, List<string> Planned)>();
        foreach (var lines in DocsFiles().Select(File.ReadAllLines))
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^\|\s*Code\s*\|"))
                {
                    continue;
                }

                var codes = new List<string>();
                var plannedCodes = new List<string>();
                for (var j = i + 1; j < lines.Length && lines[j].StartsWith('|'); j++)
                {
                    var match = Regex.Match(lines[j], @"^\|\s*`(AXC\d{4})`\s*\|(?<planned>\s*_\(planned for )?");
                    if (match.Success)
                    {
                        // A row marked _(planned for Mx)_ is not built yet, so it is listed apart from the built codes.
                        (match.Groups["planned"].Length > 0 ? plannedCodes : codes).Add(match.Groups[1].Value);
                    }
                }

                tables.Add((codes, plannedCodes));
            }
        }

        Assert.True(tables.Count == 1, $"Expected exactly one table with header '| Code |' under docs/, found {tables.Count}.");
        var (documented, planned) = tables[0];
        // Configuration and expression codes share one number range and one table.
        var declared = Constants(typeof(DiagnosticCodes)).Concat(Constants(typeof(ExpressionDiagnosticCodes))).ToList();
        var duplicates = declared.GroupBy(code => code).Where(group => group.Count() > 1).Select(group => group.Key).ToList();

        Assert.True(duplicates.Count == 0, $"Declared more than once: {string.Join(", ", duplicates)}.");
        Assert.True(!planned.Intersect(declared).Any(), $"Marked planned in the docs but already declared: {string.Join(", ", planned.Intersect(declared))}.");
        Assert.True(!declared.Except(documented).Any(), $"Missing from the docs table: {string.Join(", ", declared.Except(documented))}.");
        Assert.True(!documented.Except(declared).Any(), $"Not in DiagnosticCodes or ExpressionDiagnosticCodes: {string.Join(", ", documented.Except(declared))}. Mark the row _(planned for Mx)_ or declare the code.");
        Assert.Equal(documented.Count + planned.Count, documented.Concat(planned).Distinct().Count());
    }

    [Fact]
    public void Function_table_matches_ExpressionFunctions()
    {
        // The aggregates table has other columns, so it is not matched.
        var tables = FunctionTables(@"^\|\s*Function\s*\|\s*Result\s*\|\s*Notes\s*\|\s*SQL\s*\|");

        Assert.True(tables.Count == 1, $"Expected exactly one table with header '| Function | Result | Notes | SQL |' under docs/, found {tables.Count}.");
        var (documented, planned) = tables[0];
        var declared = ExpressionFunctions.Names;

        Assert.True(!planned.Intersect(declared).Any(), $"Marked planned in the docs but already in ExpressionFunctions: {string.Join(", ", planned.Intersect(declared))}.");
        Assert.True(!declared.Except(documented).Any(), $"Missing from the docs table: {string.Join(", ", declared.Except(documented))}. Add a row to the table in docs/reference/expressions.md.");
        Assert.True(!documented.Except(declared).Any(), $"Not in ExpressionFunctions: {string.Join(", ", documented.Except(declared))}. Mark the row _(planned for Mx)_ or add the function to ExpressionFunctions.");
        Assert.Equal(documented.Count, documented.Distinct().Count());
    }

    [Fact]
    public void Aggregate_table_matches_ExpressionFunctions()
    {
        var tables = FunctionTables(@"^\|\s*Function\s*\|\s*Result\s*\|\s*Empty list\s*\|");

        Assert.True(tables.Count == 1, $"Expected exactly one table with header '| Function | Result | Empty list |' under docs/, found {tables.Count}.");

        // count has a row with a condition and one without.
        var documented = tables[0].Built.Distinct().ToList();
        var planned = tables[0].Planned;
        var declared = ExpressionFunctions.AggregateNames;

        Assert.True(!planned.Intersect(declared).Any(), $"Marked planned in the docs but already in ExpressionFunctions: {string.Join(", ", planned.Intersect(declared))}.");
        Assert.True(!declared.Except(documented).Any(), $"Missing from the docs table: {string.Join(", ", declared.Except(documented))}. Add a row to the table in docs/reference/expressions.md.");
        Assert.True(!documented.Except(declared).Any(), $"Not in ExpressionFunctions: {string.Join(", ", documented.Except(declared))}. Mark the row _(planned for Mx)_ or add the function to ExpressionFunctions.");
    }

    [Fact]
    public void Load_step_kinds_match_ResourceKinds_and_schema_files()
    {
        var loadSteps = new List<string>();
        foreach (var lines in DocsFiles().Select(File.ReadAllLines))
        {
            for (var i = 0; i < lines.Length; i++)
            {
                var heading = Regex.Match(lines[i], @"^(#{1,6})\s+Configuration pipeline\s*$");
                if (!heading.Success)
                {
                    continue;
                }

                var level = heading.Groups[1].Length;
                for (var j = i + 1; j < lines.Length && !IsHeadingAtOrAbove(lines[j], level); j++)
                {
                    if (!Regex.IsMatch(lines[j], @"^\s*1\.\s+\*\*Load\.\*\*"))
                    {
                        continue;
                    }

                    var step = new List<string> { lines[j] };
                    for (var k = j + 1; k < lines.Length && !Regex.IsMatch(lines[k], @"^\s*\d+\.\s"); k++)
                    {
                        step.Add(lines[k]);
                    }

                    loadSteps.Add(string.Join(' ', step));
                    break;
                }
            }
        }

        Assert.True(loadSteps.Count == 1, $"Expected exactly one Load step under a 'Configuration pipeline' heading in docs/, found {loadSteps.Count}.");
        var kindList = Regex.Match(loadSteps[0], @"`kind`\s*\(([^)]*)\)");
        Assert.True(kindList.Success, "The Load step no longer names the kinds as: for its `kind` (`application`, ... or `seed`).");
        var documented = Regex.Matches(kindList.Groups[1].Value, "`([A-Za-z]+)`").Select(match => match.Groups[1].Value).ToList();
        var declared = Constants(typeof(ResourceKinds));

        Assert.Equal(declared.Order().ToArray(), documented.Order().ToArray());

        var assembly = typeof(ResourceKinds).Assembly;
        var schemaKinds = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(SchemaResourcePrefix, StringComparison.Ordinal) && name.EndsWith(SchemaResourceSuffix, StringComparison.Ordinal))
            .Select(name => name[SchemaResourcePrefix.Length..^SchemaResourceSuffix.Length])
            .ToList();

        Assert.Equal(declared.Order().ToArray(), schemaKinds.Order().ToArray());
        foreach (var kind in declared)
        {
            using var stream = assembly.GetManifestResourceStream($"{SchemaResourcePrefix}{kind}{SchemaResourceSuffix}")!;
            using var schema = JsonDocument.Parse(stream);
            Assert.Equal(kind, schema.RootElement.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString());
        }
    }

    // A row marked _(planned for Mx)_ is not built yet, so it is listed apart from the built functions.
    private static List<(List<string> Built, List<string> Planned)> FunctionTables(string headerPattern)
    {
        var tables = new List<(List<string> Built, List<string> Planned)>();
        foreach (var lines in DocsFiles().Select(File.ReadAllLines))
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], headerPattern))
                {
                    continue;
                }

                var built = new List<string>();
                var planned = new List<string>();
                for (var j = i + 1; j < lines.Length && lines[j].StartsWith('|'); j++)
                {
                    var match = Regex.Match(lines[j], @"^\|\s*`([A-Za-z]+)\(");
                    if (!match.Success)
                    {
                        continue;
                    }

                    var isPlanned = Regex.IsMatch(lines[j], @"_\(planned for M\d+\)_");
                    (isPlanned ? planned : built).Add(match.Groups[1].Value);
                }

                tables.Add((built, planned));
            }
        }

        return tables;
    }

    private static IEnumerable<string> DocsFiles() =>
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Docs"), "*.md", SearchOption.AllDirectories);

    private static bool IsHeadingAtOrAbove(string line, int level)
    {
        var heading = Regex.Match(line, @"^(#{1,6})\s");
        return heading.Success && heading.Groups[1].Length <= level;
    }

    private static List<string> Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();
}
