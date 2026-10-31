using System.Text.RegularExpressions;

namespace Axis.Server.Tests;

/// <summary>
/// D109: the audit records, the release history and the publish requests keep no display name in new rows. This scans the
/// source for the SQL that inserts into them. A write through an entity would not be seen.
/// </summary>
public sealed class HistoryNameSourceTests
{
    private static readonly string[] _tables = ["audit_records", "release_activations", "publish_requests"];
    private static readonly string[] _nameColumns = ["actor_name", "requested_by_name"];

    private static readonly Regex _insert = new(
        @"INSERT\s+INTO\s+(?:""?\w+""?\.)?""?(audit_records|release_activations|publish_requests)""?\s*\(([^)]*)\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void No_insert_into_the_audit_records_release_history_or_publish_requests_writes_a_name()
    {
        var root = RepositoryRoot();
        var source = Path.Combine(root, "src");
        var found = _tables.ToDictionary(table => table, _ => 0);
        var writers = new List<string>();

        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var segments = Path.GetRelativePath(source, file).Split(Path.DirectorySeparatorChar);
            if (segments.Any(segment => segment is "Migrations" or "bin" or "obj"))
            {
                continue;
            }

            foreach (Match match in _insert.Matches(File.ReadAllText(file)))
            {
                var table = match.Groups[1].Value.ToLowerInvariant();
                found[table]++;
                var columns = match.Groups[2].Value.Split(',').Select(column => column.Trim().Trim('"').ToLowerInvariant());
                foreach (var column in columns.Where(_nameColumns.Contains))
                {
                    writers.Add($"{Path.GetRelativePath(root, file)} writes {column} into {table}");
                }
            }
        }

        Assert.All(found, entry => Assert.True(entry.Value > 0, $"No INSERT into {entry.Key} was found in src/, so the scan checks nothing for it."));
        Assert.True(writers.Count == 0, string.Join(Environment.NewLine, writers));
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
