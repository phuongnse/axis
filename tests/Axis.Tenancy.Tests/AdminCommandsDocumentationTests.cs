using System.Text.RegularExpressions;

namespace Axis.Tenancy.Tests;

/// <summary>The commands that the commands table under docs/ lists are the commands that <c>Axis.Admin</c> dispatches, no more and no fewer.</summary>
public sealed partial class AdminCommandsDocumentationTests
{
    [Fact]
    public void Commands_table_lists_exactly_the_commands_that_Axis_Admin_dispatches()
    {
        var documented = DocumentedCommands(DocFiles());
        var dispatched = DispatchedCommands(File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Axis.Admin", "AdminCommands.cs")));

        Assert.True(dispatched.Count > 0, "No dispatcher rows were found in src/Axis.Admin/AdminCommands.cs.");
        Assert.True(documented.Distinct().Count() == documented.Count, "The commands table under docs/ lists a command twice.");
        Assert.True(dispatched.Distinct().Count() == dispatched.Count, "The dispatcher in src/Axis.Admin/AdminCommands.cs lists a command twice.");
        var difference = Difference(documented, dispatched);
        Assert.True(difference is null, difference);
    }

    [Fact]
    public void A_dispatched_command_missing_from_the_table_is_named()
    {
        var documented = DocumentedCommands([Table("tenant create", "version")]);
        var dispatched = DispatchedCommands(Dispatcher("tenant create", "tenant list", "version"));

        var difference = Difference(documented, dispatched);

        Assert.Contains("Missing from the commands table under docs/: tenant list.", difference);
    }

    [Fact]
    public void A_table_command_that_is_not_dispatched_is_named()
    {
        var documented = DocumentedCommands([Table("tenant create", "tenant purge", "version")]);
        var dispatched = DispatchedCommands(Dispatcher("tenant create", "version"));

        var difference = Difference(documented, dispatched);

        Assert.Contains("Not a command that Axis.Admin dispatches: tenant purge.", difference);
    }

    internal static IReadOnlyList<string> DocumentedCommands(IEnumerable<(string Path, string[] Lines)> docs)
    {
        var commands = new List<string>();
        var found = new List<string>();
        foreach (var (path, lines) in docs)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!TableHeader().IsMatch(lines[i]))
                {
                    continue;
                }

                found.Add(path);
                for (var j = i + 2; j < lines.Length && lines[j].StartsWith('|'); j++)
                {
                    var cells = lines[j].Split('|');
                    if (cells.Length > 2)
                    {
                        commands.AddRange(CommandSpan().Matches(cells[1]).Select(match => match.Groups[1].Value));
                    }
                }
            }
        }

        Assert.True(found.Count == 1, $"Expected exactly one table with header '| Commands | Described in |' under docs/, found {found.Count}: {string.Join(", ", found)}.");
        return commands;
    }

    internal static IReadOnlyList<string> DispatchedCommands(string source)
    {
        var start = source.IndexOf("IReadOnlyList<AdminCommand> Commands { get; } =", StringComparison.Ordinal);
        Assert.True(start >= 0, "The Commands list was not found in src/Axis.Admin/AdminCommands.cs.");
        var end = source.IndexOf("\n    ];", start, StringComparison.Ordinal);
        Assert.True(end >= 0, "The Commands list in src/Axis.Admin/AdminCommands.cs does not end with '    ];'.");
        return DispatchRow().Matches(source.Substring(start, end - start)).Select(match => match.Groups[1].Value).ToList();
    }

    internal static string? Difference(IReadOnlyList<string> documented, IReadOnlyList<string> dispatched)
    {
        var missing = dispatched.Except(documented, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var extra = documented.Except(dispatched, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (missing.Count == 0 && extra.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        if (missing.Count > 0)
        {
            parts.Add($"Missing from the commands table under docs/: {string.Join(", ", missing)}.");
        }

        if (extra.Count > 0)
        {
            parts.Add($"Not a command that Axis.Admin dispatches: {string.Join(", ", extra)}.");
        }

        return string.Join(" ", parts);
    }

    private static List<(string Path, string[] Lines)> DocFiles()
    {
        var root = RepositoryRoot();
        return Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories)
            .Select(file => (Path.GetRelativePath(root, file), File.ReadAllLines(file)))
            .ToList();
    }

    private static (string Path, string[] Lines) Table(params string[] commands) =>
        ("fixture.md", ["| Commands | Described in |", "| --- | --- |", .. commands.Select(command => $"| `{command}` | [Section](section.md) |")]);

    private static string Dispatcher(params string[] commands) =>
        "    internal static IReadOnlyList<AdminCommand> Commands { get; } =\n    [\n"
        + string.Concat(commands.Select(command => $"        new(\"{command}\", AuditMark.None, VersionAsync),\n"))
        + "    ];\n";

    [GeneratedRegex(@"^\|\s*Commands\s*\|\s*Described in\s*\|")]
    private static partial Regex TableHeader();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex CommandSpan();

    [GeneratedRegex(@"^\s*new\(""([a-z][a-z -]*)"", AuditMark\.", RegexOptions.Multiline)]
    private static partial Regex DispatchRow();

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
