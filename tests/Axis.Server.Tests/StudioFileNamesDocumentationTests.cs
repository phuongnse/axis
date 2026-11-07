using System.Text.RegularExpressions;
using Axis.Server.Studio;

namespace Axis.Server.Tests;

/// <summary>The kind folders table of docs/reference/authoring.md lists exactly the folders of <see cref="StudioFileNames"/>, no more and no fewer.</summary>
public sealed partial class StudioFileNamesDocumentationTests
{
    [Fact]
    public void The_kind_folders_table_lists_the_folder_of_each_kind_Studio_creates()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "reference", "authoring.md"));
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
                if (KindRow().Match(lines[j]) is { Success: true } row)
                {
                    documented.Add($"{row.Groups[1].Value} -> {row.Groups[2].Value}");
                }
            }
        }

        Assert.True(tables == 1, $"Expected exactly one table with header '| Kind | Folder |' in docs/reference/authoring.md, found {tables}.");
        Assert.NotEmpty(documented);

        var declared = StudioFileNames.Folders.Select(pair => $"{pair.Key} -> {pair.Value}").ToList();
        Assert.True(!declared.Except(documented).Any(), $"Missing from the kind folders table in docs/reference/authoring.md: {string.Join(", ", declared.Except(documented))}.");
        Assert.True(!documented.Except(declared).Any(), $"Not a kind folder of StudioFileNames: {string.Join(", ", documented.Except(declared))}.");
        Assert.True(documented.Distinct().Count() == documented.Count, "The kind folders table in docs/reference/authoring.md lists a kind twice.");
    }

    [GeneratedRegex(@"^\|\s*Kind\s*\|\s*Folder\s*\|")]
    private static partial Regex TableHeader();

    [GeneratedRegex(@"^\|\s*`([A-Za-z]+)`\s*\|\s*`([A-Za-z-]+)`\s*\|")]
    private static partial Regex KindRow();

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
