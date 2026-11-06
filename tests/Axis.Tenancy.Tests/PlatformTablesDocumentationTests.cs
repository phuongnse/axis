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

    [GeneratedRegex(@"^\|\s*Table\s*\|\s*Columns\s*\|")]
    private static partial Regex TableHeader();

    [GeneratedRegex(@"`platform\.([a-z_]+)`")]
    private static partial Regex PlatformTable();

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
