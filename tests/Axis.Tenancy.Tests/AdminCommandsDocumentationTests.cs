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

    [Fact]
    public void Audit_table_lists_each_command_of_Axis_Admin_with_its_mark()
    {
        var documented = DocumentedMarks(DocFiles());
        var dispatched = DispatchedMarks(File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Axis.Admin", "AdminCommands.cs")));

        Assert.True(dispatched.Count > 0, "No dispatcher rows were found in src/Axis.Admin/AdminCommands.cs.");
        var difference = MarkDifference(documented, dispatched);
        Assert.True(difference is null, difference);
    }

    [Fact]
    public void A_dispatched_command_missing_from_the_audit_table_is_named()
    {
        var documented = DocumentedMarks([AuditTable(("`tenant create`", "One per tenant"))]);
        var dispatched = DispatchedMarks(MarkedDispatcher(("tenant create", "OnePerTenant"), ("cell operator client add", "OnePerRun")));

        var difference = MarkDifference(documented, dispatched);

        Assert.Contains("Missing from the audit table in docs/: cell operator client add.", difference);
    }

    [Fact]
    public void An_audit_table_command_that_is_not_dispatched_is_named()
    {
        var documented = DocumentedMarks([AuditTable(("`tenant create`", "One per tenant"), ("`tenant purge`", "One per tenant"))]);
        var dispatched = DispatchedMarks(MarkedDispatcher(("tenant create", "OnePerTenant")));

        var difference = MarkDifference(documented, dispatched);

        Assert.Contains("Not a command that Axis.Admin dispatches: tenant purge.", difference);
    }

    [Fact]
    public void A_command_whose_audit_mark_differs_is_named()
    {
        var documented = DocumentedMarks([AuditTable(("`platform rekey`", "One per tenant"))]);
        var dispatched = DispatchedMarks(MarkedDispatcher(("platform rekey", "OnePerRun")));

        var difference = MarkDifference(documented, dispatched);

        Assert.Contains("Audit mark differs for platform rekey: docs/ says OnePerTenant, Axis.Admin says OnePerRun.", difference);
    }

    [Fact]
    public void A_planned_row_is_not_compared_until_its_command_is_built()
    {
        var documented = DocumentedMarks([AuditTable(("`tenant create`", "One per tenant"), ("`tenant files move` _(planned for Mx)_", "One per tenant"))]);
        var dispatched = DispatchedMarks(MarkedDispatcher(("tenant create", "OnePerTenant")));

        var difference = MarkDifference(documented, dispatched);

        Assert.Null(difference);
    }

    [Fact]
    public void An_unknown_records_label_fails_and_is_named()
    {
        var error = Assert.Throws<Xunit.Sdk.TrueException>(() => DocumentedMarks([AuditTable(("`tenant create`", "Once per tenant"))]));

        Assert.Contains("'Once per tenant'", error.Message);
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

    internal static IReadOnlyList<(string Name, string Mark)> DocumentedMarks(IEnumerable<(string Path, string[] Lines)> docs)
    {
        var marks = new List<(string Name, string Mark)>();
        var found = new List<string>();
        foreach (var (path, lines) in docs)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!AuditTableHeader().IsMatch(lines[i]))
                {
                    continue;
                }

                found.Add(path);
                for (var j = i + 2; j < lines.Length && lines[j].StartsWith('|'); j++)
                {
                    var cells = lines[j].Split('|');
                    if (cells.Length <= 2 || cells[1].Contains("_(planned for M", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var label = cells[2].Trim();
                    Assert.True(_recordsMarks.ContainsKey(label), $"{path} has the Records label '{label}' in the row '{cells[1].Trim()}'. Use One per tenant, One per run or None.");
                    marks.AddRange(CommandSpan().Matches(cells[1]).Select(match => (Name: match.Groups[1].Value, Mark: _recordsMarks[label])));
                }
            }
        }

        Assert.True(found.Count == 1, $"Expected exactly one audit table with header '| Command | Records | When |' under docs/, found {found.Count}: {string.Join(", ", found)}.");
        return marks;
    }

    internal static IReadOnlyList<string> DispatchedCommands(string source) =>
        DispatchedMarks(source).Select(row => row.Name).ToList();

    internal static IReadOnlyList<(string Name, string Mark)> DispatchedMarks(string source)
    {
        var start = source.IndexOf("IReadOnlyList<AdminCommand> Commands { get; } =", StringComparison.Ordinal);
        Assert.True(start >= 0, "The Commands list was not found in src/Axis.Admin/AdminCommands.cs.");
        var end = source.IndexOf("\n    ];", start, StringComparison.Ordinal);
        Assert.True(end >= 0, "The Commands list in src/Axis.Admin/AdminCommands.cs does not end with '    ];'.");
        return DispatchRow().Matches(source.Substring(start, end - start)).Select(match => (Name: match.Groups[1].Value, Mark: match.Groups[2].Value)).ToList();
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

    internal static string? MarkDifference(IReadOnlyList<(string Name, string Mark)> documented, IReadOnlyList<(string Name, string Mark)> dispatched)
    {
        Assert.True(documented.Select(row => row.Name).Distinct().Count() == documented.Count, "The audit table under docs/ lists a command twice.");
        Assert.True(dispatched.Select(row => row.Name).Distinct().Count() == dispatched.Count, "The dispatcher in src/Axis.Admin/AdminCommands.cs lists a command twice.");
        var documentedMarks = documented.ToDictionary(row => row.Name, row => row.Mark, StringComparer.Ordinal);
        var dispatchedMarks = dispatched.ToDictionary(row => row.Name, row => row.Mark, StringComparer.Ordinal);
        var missing = dispatchedMarks.Keys.Except(documentedMarks.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var extra = documentedMarks.Keys.Except(dispatchedMarks.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var differing = dispatchedMarks.Keys
            .Intersect(documentedMarks.Keys, StringComparer.Ordinal)
            .Where(name => documentedMarks[name] != dispatchedMarks[name])
            .Order(StringComparer.Ordinal)
            .ToList();

        var parts = new List<string>();
        if (missing.Count > 0)
        {
            parts.Add($"Missing from the audit table in docs/: {string.Join(", ", missing)}.");
        }

        if (extra.Count > 0)
        {
            parts.Add($"Not a command that Axis.Admin dispatches: {string.Join(", ", extra)}.");
        }

        parts.AddRange(differing.Select(name => $"Audit mark differs for {name}: docs/ says {documentedMarks[name]}, Axis.Admin says {dispatchedMarks[name]}."));
        return parts.Count == 0 ? null : string.Join(" ", parts);
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

    private static (string Path, string[] Lines) AuditTable(params (string Command, string Records)[] rows) =>
        ("fixture.md", ["| Command | Records | When |", "| --- | --- | --- |", .. rows.Select(row => $"| {row.Command} | {row.Records} | When |")]);

    private static string Dispatcher(params string[] commands) =>
        MarkedDispatcher(commands.Select(command => (Name: command, Mark: "None")).ToArray());

    private static string MarkedDispatcher(params (string Name, string Mark)[] rows) =>
        "    internal static IReadOnlyList<AdminCommand> Commands { get; } =\n    [\n"
        + string.Concat(rows.Select(row => $"        new(\"{row.Name}\", AuditMark.{row.Mark}, VersionAsync),\n"))
        + "    ];\n";

    private static readonly Dictionary<string, string> _recordsMarks = new(StringComparer.Ordinal)
    {
        ["One per tenant"] = "OnePerTenant",
        ["One per run"] = "OnePerRun",
        ["None"] = "None",
    };

    [GeneratedRegex(@"^\|\s*Commands\s*\|\s*Described in\s*\|")]
    private static partial Regex TableHeader();

    [GeneratedRegex(@"^\|\s*Command\s*\|\s*Records\s*\|\s*When\s*\|")]
    private static partial Regex AuditTableHeader();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex CommandSpan();

    [GeneratedRegex(@"^\s*new\(""([a-z][a-z -]*)"", AuditMark\.(\w+),", RegexOptions.Multiline)]
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
