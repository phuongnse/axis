using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Resources;
using Axis.Configuration.Storage;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Functions;
using Microsoft.EntityFrameworkCore;

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
                // Other tables, such as the form condition checks, also begin with a Code column but are not this table.
                if (!Regex.IsMatch(lines[i], @"^\|\s*Code\s*\|\s*Meaning\s*\|"))
                {
                    continue;
                }

                var built = new List<string>();
                var planned = new List<string>();
                for (var j = i + 1; j < lines.Length && lines[j].StartsWith('|'); j++)
                {
                    var match = Regex.Match(lines[j], @"^\|\s*`(AXC\d{4})`\s*\|");
                    if (match.Success)
                    {
                        // A row marked _(planned for Mx)_ is not built yet, so it is listed apart from the built codes.
                        var isPlanned = Regex.IsMatch(lines[j], @"^\|[^|]*\|\s*_\(planned for M\d+\)_");
                        (isPlanned ? planned : built).Add(match.Groups[1].Value);
                    }
                }

                tables.Add((built, planned));
            }
        }

        Assert.True(tables.Count == 1, $"Expected exactly one table with header '| Code | Meaning |' under docs/, found {tables.Count}.");
        var (builtCodes, plannedCodes) = tables[0];
        var documented = builtCodes.Concat(plannedCodes).ToList();
        // Configuration and expression codes share one number range and one table.
        var declared = Constants(typeof(DiagnosticCodes)).Concat(Constants(typeof(ExpressionDiagnosticCodes))).ToList();
        var duplicates = declared.GroupBy(code => code).Where(group => group.Count() > 1).Select(group => group.Key).ToList();

        Assert.True(duplicates.Count == 0, $"Declared more than once: {string.Join(", ", duplicates)}.");
        Assert.True(!declared.Except(documented).Any(), $"Missing from the docs table: {string.Join(", ", declared.Except(documented))}.");
        Assert.True(!plannedCodes.Intersect(declared).Any(), $"Marked planned in the docs but already declared: {string.Join(", ", plannedCodes.Intersect(declared))}.");
        Assert.True(!builtCodes.Except(declared).Any(), $"Not in DiagnosticCodes or ExpressionDiagnosticCodes: {string.Join(", ", builtCodes.Except(declared))}. Mark the row _(planned for Mx)_ or declare the code.");
        Assert.Equal(documented.Count, documented.Distinct().Count());
    }

    [Fact]
    public void System_tables_table_matches_the_tables_of_ConfigurationDbContext()
    {
        var documented = new List<string>();
        var tables = 0;
        foreach (var lines in DocsFiles().Select(File.ReadAllLines))
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^\|\s*Table\s*\|\s*Module\s*\|\s*One row is\s*\|"))
                {
                    continue;
                }

                tables++;
                for (var j = i + 1; j < lines.Length && lines[j].StartsWith('|'); j++)
                {
                    var cells = lines[j].Split('|');
                    if (cells.Length > 3 && cells[2].Trim() == "`Axis.Configuration`")
                    {
                        documented.AddRange(Regex.Matches(cells[1], @"`axis\.([a-z_]+)`").Select(match => match.Groups[1].Value));
                    }
                }
            }
        }

        Assert.True(tables == 1, $"Expected exactly one table with header '| Table | Module | One row is |' under docs/, found {tables}.");
        using var context = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql("Host=unused").Options);
        var declared = context.Model.GetEntityTypes().Select(type => type.GetTableName()!).ToList();

        Assert.True(!declared.Except(documented).Any(), $"Missing from the System tables table in docs/reference/storage.md: {string.Join(", ", declared.Except(documented))}.");
        Assert.True(!documented.Except(declared).Any(), $"Not a table of ConfigurationDbContext: {string.Join(", ", documented.Except(declared))}.");
        Assert.Equal(documented.Count, documented.Distinct().Count());
    }

    [Fact]
    public void System_tables_diagram_matches_the_foreign_keys_of_ConfigurationDbContext()
    {
        var drawn = ErDiagramRelationships("System tables");
        using var context = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql("Host=unused").Options);
        var tables = context.Model.GetEntityTypes().Select(type => type.GetTableName()!).ToList();
        var foreignKeys = context.Model.GetEntityTypes()
            .SelectMany(type => type.GetForeignKeys())
            .Select(key => (Principal: key.PrincipalEntityType.GetTableName()!, Dependent: key.DeclaringEntityType.GetTableName()!))
            .ToList();
        var drawnTables = drawn.SelectMany(relationship => new[] { relationship.Principal, relationship.Dependent }).Distinct().ToList();

        Assert.True(!drawnTables.Except(tables).Any(), $"Not a table of ConfigurationDbContext: {string.Join(", ", drawnTables.Except(tables))}.");
        Assert.True(!drawn.Except(foreignKeys).Any(), $"Drawn in docs/reference/storage.md but not a foreign key: {string.Join(", ", drawn.Except(foreignKeys))}.");
        var missing = foreignKeys.Where(key => drawnTables.Contains(key.Principal) && drawnTables.Contains(key.Dependent)).Except(drawn).ToList();
        Assert.True(missing.Count == 0, $"Foreign keys between drawn tables missing from the diagram in docs/reference/storage.md: {string.Join(", ", missing)}.");
    }

    [Fact]
    public void Function_tables_SQL_column_matches_ExpressionFunctions()
    {
        const string docs = "docs/reference/expressions.md";
        var sqlRows = new List<(string Name, string Sql)>();
        var found = 0;
        foreach (var lines in DocsFiles().Select(File.ReadAllLines))
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^\|\s*Function\s*\|.*\|\s*SQL\s*\|\s*$"))
                {
                    continue;
                }

                found++;
                for (var j = i + 1; j < lines.Length && lines[j].StartsWith('|'); j++)
                {
                    var match = Regex.Match(lines[j], @"^\|\s*`([A-Za-z]+)\(");
                    if (match.Success)
                    {
                        sqlRows.Add((match.Groups[1].Value, lines[j].Split('|')[^2].Trim()));
                    }
                }
            }
        }

        Assert.True(found == 1, $"Expected exactly one table with header '| Function | … | SQL |' under docs/, found {found}.");
        var invalid = sqlRows.Where(row => row.Sql is not ("Yes" or "No")).Select(row => row.Name).ToList();
        Assert.True(invalid.Count == 0, $"The SQL column in {docs} must be Yes or No, but it is not for: {string.Join(", ", invalid)}.");

        var documented = sqlRows.Where(row => row.Sql == "Yes").Select(row => row.Name).Order().ToList();
        var declared = ExpressionFunctions.Names.Where(ExpressionFunctions.IsInSqlSubset).Order().ToList();
        Assert.True(
            documented.SequenceEqual(declared),
            $"The SQL column in {docs} says Yes for [{string.Join(", ", documented)}], but ExpressionFunctions marks [{string.Join(", ", declared)}] as in the SQL subset.");
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

    // The relationships of the one erDiagram under a '## ' heading of storage.md, as (principal table, dependent table).
    private static List<(string Principal, string Dependent)> ErDiagramRelationships(string heading)
    {
        var storage = DocsFiles().Single(file => file.EndsWith(Path.Combine("reference", "storage.md"), StringComparison.Ordinal));
        var lines = File.ReadAllLines(storage);
        var start = Array.IndexOf(lines, $"## {heading}");
        Assert.True(start >= 0, $"Expected a '## {heading}' heading in docs/reference/storage.md.");
        var section = string.Join('\n', lines.Skip(start + 1).TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal)));
        var diagrams = Regex.Matches(section, @"```mermaid\n(.*?)```", RegexOptions.Singleline).Select(match => match.Groups[1].Value).ToList();

        Assert.True(diagrams.Count == 1 && diagrams[0].TrimStart().StartsWith("erDiagram", StringComparison.Ordinal), $"Expected one erDiagram under '## {heading}' in docs/reference/storage.md, found {diagrams.Count} mermaid block(s).");
        return Regex.Matches(diagrams[0], @"^\s*([a-z_]+)\s+[|}][o|](?:--|\.\.)[o|][|{]\s+([a-z_]+)\s*:", RegexOptions.Multiline)
            .Select(match => (Principal: match.Groups[1].Value, Dependent: match.Groups[2].Value))
            .ToList();
    }

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
