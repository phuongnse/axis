using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Resources;
using Axis.Expressions.Diagnostics;

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
        var tables = new List<List<string>>();
        foreach (var lines in DocsFiles().Select(File.ReadAllLines))
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^\|\s*Code\s*\|"))
                {
                    continue;
                }

                var codes = new List<string>();
                for (var j = i + 1; j < lines.Length && lines[j].StartsWith('|'); j++)
                {
                    var match = Regex.Match(lines[j], @"^\|\s*`(AXC\d{4})`\s*\|");
                    if (match.Success)
                    {
                        codes.Add(match.Groups[1].Value);
                    }
                }

                tables.Add(codes);
            }
        }

        Assert.True(tables.Count == 1, $"Expected exactly one table with header '| Code |' under docs/, found {tables.Count}.");
        var documented = tables[0];
        // Configuration and expression codes share one number range and one table.
        var declared = Constants(typeof(DiagnosticCodes)).Concat(Constants(typeof(ExpressionDiagnosticCodes))).ToList();
        var duplicates = declared.GroupBy(code => code).Where(group => group.Count() > 1).Select(group => group.Key).ToList();

        Assert.True(duplicates.Count == 0, $"Declared more than once: {string.Join(", ", duplicates)}.");
        Assert.True(!declared.Except(documented).Any(), $"Missing from the docs table: {string.Join(", ", declared.Except(documented))}.");
        Assert.True(!documented.Except(declared).Any(), $"Not in DiagnosticCodes or ExpressionDiagnosticCodes: {string.Join(", ", documented.Except(declared))}.");
        Assert.Equal(documented.Count, documented.Distinct().Count());
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
