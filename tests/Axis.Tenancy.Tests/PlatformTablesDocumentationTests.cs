using System.Text.RegularExpressions;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Tenancy.Tests;

/// <summary>The platform tables that docs/reference/storage.md lists are the tables of <see cref="PlatformDbContext"/>, no more and no fewer.</summary>
public sealed partial class PlatformTablesDocumentationTests
{
    [Fact]
    public void Platform_tables_list_matches_the_tables_of_PlatformDbContext()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "reference", "storage.md"));
        var documented = new List<string>();
        var tables = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!TableHeader().IsMatch(lines[i]))
            {
                continue;
            }

            tables++;
            for (var j = i + 1; j < lines.Length && lines[j].StartsWith('|'); j++)
            {
                var cells = lines[j].Split('|');
                if (cells.Length > 2)
                {
                    documented.AddRange(PlatformTable().Matches(cells[1]).Select(match => match.Groups[1].Value));
                }
            }
        }

        Assert.True(tables == 1, $"Expected exactly one table with header '| Table | Columns |' in docs/reference/storage.md, found {tables}.");
        using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=unused").Options);
        var declared = context.Model.GetEntityTypes().Select(type => type.GetTableName()!).ToList();

        Assert.True(!declared.Except(documented).Any(), $"Missing from the platform tables list in docs/reference/storage.md: {string.Join(", ", declared.Except(documented))}.");
        Assert.True(!documented.Except(declared).Any(), $"Not a table of PlatformDbContext: {string.Join(", ", documented.Except(declared))}.");
        Assert.Equal(documented.Count, documented.Distinct().Count());
    }

    [Fact]
    public void Platform_database_diagram_matches_the_foreign_keys_of_PlatformDbContext()
    {
        var drawn = ErDiagramRelationships("Platform database");
        using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=unused").Options);
        var tables = context.Model.GetEntityTypes().Select(type => type.GetTableName()!).ToList();
        var foreignKeys = context.Model.GetEntityTypes()
            .SelectMany(type => type.GetForeignKeys())
            .Select(key => (Principal: key.PrincipalEntityType.GetTableName()!, Dependent: key.DeclaringEntityType.GetTableName()!))
            .ToList();
        var drawnTables = drawn.SelectMany(relationship => new[] { relationship.Principal, relationship.Dependent }).Distinct().ToList();

        Assert.True(!drawnTables.Except(tables).Any(), $"Not a table of PlatformDbContext: {string.Join(", ", drawnTables.Except(tables))}.");
        Assert.True(!drawn.Except(foreignKeys).Any(), $"Drawn in docs/reference/storage.md but not a foreign key: {string.Join(", ", drawn.Except(foreignKeys))}.");
        var missing = foreignKeys.Where(key => drawnTables.Contains(key.Principal) && drawnTables.Contains(key.Dependent)).Except(drawn).ToList();
        Assert.True(missing.Count == 0, $"Foreign keys between drawn tables missing from the diagram in docs/reference/storage.md: {string.Join(", ", missing)}.");
    }

    [GeneratedRegex(@"^\|\s*Table\s*\|\s*Columns\s*\|")]
    private static partial Regex TableHeader();

    [GeneratedRegex(@"`platform\.([a-z_]+)`")]
    private static partial Regex PlatformTable();

    [GeneratedRegex("```mermaid\\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex MermaidBlock();

    [GeneratedRegex(@"^\s*([a-z_]+)\s+[|}][o|](?:--|\.\.)[o|][|{]\s+([a-z_]+)\s*:", RegexOptions.Multiline)]
    private static partial Regex ErRelationship();

    // The relationships of the one erDiagram under a '## ' heading of storage.md, as (principal table, dependent table).
    private static List<(string Principal, string Dependent)> ErDiagramRelationships(string heading)
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "reference", "storage.md"));
        var start = Array.IndexOf(lines, $"## {heading}");
        Assert.True(start >= 0, $"Expected a '## {heading}' heading in docs/reference/storage.md.");
        var section = string.Join('\n', lines.Skip(start + 1).TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal)));
        var diagrams = MermaidBlock().Matches(section).Select(match => match.Groups[1].Value).ToList();

        Assert.True(diagrams.Count == 1 && diagrams[0].TrimStart().StartsWith("erDiagram", StringComparison.Ordinal), $"Expected one erDiagram under '## {heading}' in docs/reference/storage.md, found {diagrams.Count} mermaid block(s).");
        return ErRelationship().Matches(diagrams[0])
            .Select(match => (Principal: match.Groups[1].Value, Dependent: match.Groups[2].Value))
            .ToList();
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
