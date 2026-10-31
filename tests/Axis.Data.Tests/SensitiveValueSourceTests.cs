using System.Text.RegularExpressions;

namespace Axis.Data.Tests;

/// <summary>
/// The guard for <c>docs/reference/data-encryption.md#sensitive-field-values</c>. A sensitive field value must reach an
/// entity column only through the cipher. This scans <c>src/</c> for SQL over entity tables and columns and fails on a
/// method that neither names the cipher nor sits on <see cref="_allowed"/>. It is a text scan. It does not see SQL
/// built through other helpers, such as a table name passed in a variable, and a method that names the cipher counts as
/// routed even if one of its statements bypasses it.
/// </summary>
public sealed class SensitiveValueSourceTests
{
    private const string Queries = "Axis.Data/Records/RecordQueries.cs";

    private const string Commands = "Axis.Data/Records/RecordCommands.cs";

    private static readonly (string Name, Regex Pattern, string? OnlyIn)[] _patterns =
    [
        ("RecordQueries.Table(", new(@"RecordQueries\.Table\(", RegexOptions.CultureInvariant), null),
        ("EntityNaming.QualifiedTable(EntityNaming.Table(", new(@"EntityNaming\.QualifiedTable\(EntityNaming\.Table\(", RegexOptions.CultureInvariant), null),
        ("EntityNaming.Column", new(@"EntityNaming\.Column\b", RegexOptions.CultureInvariant), null),
        ("RecordQueries.ReadValue(", new(@"RecordQueries\.ReadValue\(", RegexOptions.CultureInvariant), null),
        ("RecordCommands.AddValue(", new(@"RecordCommands\.AddValue\(", RegexOptions.CultureInvariant), null),
        ("Table(", new(@"(?<![.\w])Table\(", RegexOptions.CultureInvariant), Queries),
        ("ReadValue(", new(@"(?<![.\w])ReadValue\(", RegexOptions.CultureInvariant), Queries),
        ("AddValue(", new(@"(?<![.\w])AddValue\(", RegexOptions.CultureInvariant), Commands),
    ];

    private static readonly Regex _routed = new(@"\b(RecordCipher|SensitiveValues|cipher)\b", RegexOptions.CultureInvariant);

    private static readonly Regex _memberStart = new(@"^( {4}| {8})(public|internal|private|protected)\b", RegexOptions.CultureInvariant);

    /// <summary>Why a method that builds SQL over an entity column never touches a sensitive value.</summary>
    internal enum Untouched
    {
        /// <summary>It reads or writes ids, versions, counts, locks, release stamps or the existence of a row.</summary>
        Ids,

        /// <summary>It deletes rows and writes no value.</summary>
        Deletes,

        /// <summary>It plans DDL, an index, a constraint or a name, and moves no value.</summary>
        SchemaPlanning,

        /// <summary>
        /// It uses a display, sort, search, filter, group, aggregate, unique, reference or file column. Compilation
        /// refuses a sensitive field there (AXC0308), or the field type cannot be sensitive.
        /// </summary>
        NeverSensitive,

        /// <summary>It names the columns of a read whose rows the caller opens with the cipher.</summary>
        SelectList,

        /// <summary>It is the declaration of the table or value helper itself.</summary>
        Helper,
    }

    /// <summary>One use of an entity table or column, in the member that holds it.</summary>
    internal sealed record Use(string File, string Member, int Line, string Pattern, bool Routed);

    private static readonly Dictionary<string, Untouched> _allowed = new()
    {
        ["Axis.Data/DataSources/DataSourceQueries.cs:Statement"] = Untouched.SelectList,
        ["Axis.Data/DataSources/DataSourceQueries.cs:SelectList"] = Untouched.SelectList,
        ["Axis.Data/DataSources/DataSourceQueries.cs:GroupedSelectList"] = Untouched.NeverSensitive,
        ["Axis.Data/DataSources/DataSourceQueries.cs:ReadGroupedRow"] = Untouched.NeverSensitive,
        ["Axis.Data/DataSources/DataSourceQueries.cs:Sql"] = Untouched.NeverSensitive,
        ["Axis.Data/DataSources/DataSourceQueries.cs:ColumnOf"] = Untouched.SelectList,

        ["Axis.Data/EntityProvisioner.cs:CountRowsAsync"] = Untouched.Ids,

        ["Axis.Data/Records/ComputedBackfill.cs:TryEndAsync"] = Untouched.Ids,
        ["Axis.Data/Records/ComputedBackfill.cs:CountOutOfDateAsync"] = Untouched.Ids,
        ["Axis.Data/Records/ComputedBackfill.cs:ReadOutOfDateIdsAsync"] = Untouched.Ids,
        ["Axis.Data/Records/ComputedBackfill.cs:OutOfDateBatchSql"] = Untouched.Ids,

        ["Axis.Data/Records/ComputedRecompute.cs:DeleteErrorsOfAsync"] = Untouched.Deletes,

        ["Axis.Data/Records/RecordCommands.cs:LockAsync"] = Untouched.Ids,
        ["Axis.Data/Records/RecordCommands.cs:DeleteAsync"] = Untouched.Deletes,
        ["Axis.Data/Records/RecordCommands.cs:MapViolation"] = Untouched.SchemaPlanning,
        ["Axis.Data/Records/RecordCommands.cs:AddMissingLinksAsync"] = Untouched.Ids,
        ["Axis.Data/Records/RecordCommands.cs:IsMissingReferenceAsync"] = Untouched.Ids,
        // SeedApplier passes only plain fields, and AddValue throws on a plain value of a sensitive field.
        ["Axis.Data/Records/RecordCommands.cs:FindVersionAsync"] = Untouched.Ids,

        ["Axis.Data/Records/RecordFilters.cs:MatchAsync"] = Untouched.Ids,
        ["Axis.Data/Records/RecordFilters.cs:Column"] = Untouched.NeverSensitive,
        ["Axis.Data/Records/RecordFilters.cs:Collection"] = Untouched.Ids,

        ["Axis.Data/Records/RecordQueries.cs:ListStatement"] = Untouched.SelectList,
        ["Axis.Data/Records/RecordQueries.cs:ReadDisplayLabelsAsync"] = Untouched.NeverSensitive,
        ["Axis.Data/Records/RecordQueries.cs:FindByDisplayValueAsync"] = Untouched.NeverSensitive,
        ["Axis.Data/Records/RecordQueries.cs:FindByValueAsync"] = Untouched.NeverSensitive,
        ["Axis.Data/Records/RecordQueries.cs:ReadLinksAsync"] = Untouched.NeverSensitive,
        ["Axis.Data/Records/RecordQueries.cs:ReadRowIdsAsync"] = Untouched.Ids,
        ["Axis.Data/Records/RecordQueries.cs:Table"] = Untouched.Helper,
        ["Axis.Data/Records/RecordQueries.cs:SelectList"] = Untouched.SelectList,
        ["Axis.Data/Records/RecordQueries.cs:ColumnSql"] = Untouched.SelectList,
        ["Axis.Data/Records/RecordQueries.cs:LabelJoins"] = Untouched.NeverSensitive,
        ["Axis.Data/Records/RecordQueries.cs:ReadValue"] = Untouched.Helper,

        ["Axis.Data/Records/RecordSearch.cs:Sql"] = Untouched.NeverSensitive,

        ["Axis.Data/Schema/EntityIndexes.cs:Expected"] = Untouched.SchemaPlanning,

        ["Axis.Data/Schema/MigrationPlanning.cs:Fill"] = Untouched.SchemaPlanning,
        ["Axis.Data/Schema/MigrationPlanning.cs:Convert"] = Untouched.SchemaPlanning,
        ["Axis.Data/Schema/MigrationPlanning.cs:Rename"] = Untouched.SchemaPlanning,
        ["Axis.Data/Schema/MigrationPlanning.cs:Drop"] = Untouched.SchemaPlanning,

        ["Axis.Data/Schema/SchemaPlanner.cs:PlanNewTable"] = Untouched.SchemaPlanning,
        ["Axis.Data/Schema/SchemaPlanner.cs:PlanExistingTable"] = Untouched.SchemaPlanning,
        ["Axis.Data/Schema/SchemaPlanner.cs:PlanNewColumn"] = Untouched.SchemaPlanning,
        ["Axis.Data/Schema/SchemaPlanner.cs:AddForeignKey"] = Untouched.SchemaPlanning,

        ["Axis.Processes/Instances/InstanceQueries.cs:FindAsync"] = Untouched.Ids,
        ["Axis.Processes/Instances/InstanceQueries.cs:Where"] = Untouched.NeverSensitive,
        ["Axis.Processes/Instances/InstanceQueries.cs:ListPageSql"] = Untouched.NeverSensitive,
    };

    [Fact]
    public void Every_entity_column_use_in_src_goes_through_the_cipher_or_is_allowed()
    {
        var root = RepositoryRoot();
        var source = Path.Combine(root, "src");
        var uses = new List<Use>();

        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.Split('/').Any(segment => segment is "Migrations" or "bin" or "obj"))
            {
                continue;
            }

            uses.AddRange(Scan(relative, File.ReadAllText(file)));
        }

        var unlisted = uses
            .Where(use => !use.Routed && !_allowed.ContainsKey(Key(use)))
            .Select(use => $"{use.File}:{use.Line} {use.Member} uses {use.Pattern} without the cipher and is not allowed")
            .Distinct()
            .ToList();
        Assert.True(
            unlisted.Count == 0,
            "Route the value through RecordCipher, or add an allow-list entry that says why it touches no sensitive value:"
            + Environment.NewLine + string.Join(Environment.NewLine, unlisted));

        var unseen = _patterns.Where(pattern => uses.All(use => use.Pattern != pattern.Name)).Select(pattern => pattern.Name).ToList();
        Assert.True(unseen.Count == 0, "No use was found in src/, so the scan checks nothing for: " + string.Join(", ", unseen));

        var used = uses.Select(Key).ToHashSet();
        var stale = _allowed.Keys.Where(key => !used.Contains(key)).ToList();
        Assert.True(stale.Count == 0, "These allow-list entries match no use in src/: " + string.Join(", ", stale));
    }

    [Fact]
    public void A_raw_insert_into_an_entity_column_is_reported()
    {
        var uses = Scan("Axis.Data/Fixture.cs", Fixture(sealing: false));

        var unrouted = Assert.Single(uses.Where(use => !use.Routed).Select(use => use.Member).Distinct());
        Assert.Equal("Write", unrouted);
    }

    [Fact]
    public void A_write_that_seals_with_the_cipher_is_not_reported()
    {
        var uses = Scan("Axis.Data/Fixture.cs", Fixture(sealing: true));

        Assert.NotEmpty(uses);
        Assert.DoesNotContain(uses, use => !use.Routed);
    }

    private static string Key(Use use) => $"{use.File}:{use.Member}";

    private static string Fixture(bool sealing) =>
        $$"""
        namespace Axis.Data.Fixtures;

        internal static class Fixture
        {
            internal static async Task Write(NpgsqlCommand command, EntityModel entity, FieldModel field)
            {
        {{(sealing ? "        var sealedValue = await RecordCipher.SealAsync(cipher, entity, field, value);" : "")}}
                command.CommandText = $"INSERT INTO {RecordQueries.Table(entity)} ({EntityNaming.Quote(EntityNaming.Column(field.Name))}) VALUES (@p0)";
                await command.ExecuteNonQueryAsync();
            }
        }
        """;

    /// <summary>Finds the uses of an entity table or column in a file, each with the member that holds it.</summary>
    internal static IReadOnlyList<Use> Scan(string relativePath, string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var uses = new List<Use>();
        var members = new List<(string Name, int Start, int End)>();
        var start = -1;
        var name = "";

        for (var index = 0; index < lines.Length; index++)
        {
            if (!_memberStart.IsMatch(lines[index]))
            {
                continue;
            }

            if (start >= 0)
            {
                members.Add((name, start, index));
            }

            start = index;
            name = MemberName(lines[index]);
        }

        if (start >= 0)
        {
            members.Add((name, start, lines.Length));
        }

        foreach (var (member, from, to) in members)
        {
            var text = string.Join('\n', lines[from..to]);
            var routed = _routed.IsMatch(text);
            for (var index = from; index < to; index++)
            {
                if (lines[index].TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var (patternName, pattern, onlyIn) in _patterns)
                {
                    if ((onlyIn is null || relativePath == onlyIn) && pattern.IsMatch(lines[index]))
                    {
                        uses.Add(new Use(relativePath, member, index + 1, patternName, routed));
                    }
                }
            }
        }

        return uses;
    }

    /// <summary>The identifier before the first <c>(</c>, <c>{</c> or <c>=</c> outside generic arguments and a tuple type.</summary>
    private static string MemberName(string line)
    {
        var depth = 0;
        var name = "";
        var current = new System.Text.StringBuilder();

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '<')
            {
                depth++;
            }
            else if (character == '>')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0 && (char.IsLetterOrDigit(character) || character == '_'))
            {
                current.Append(character);
                continue;
            }
            else if (depth == 0 && character is '{' or '=')
            {
                break;
            }
            else if (character == '(')
            {
                // A tuple return type opens with a parenthesis that follows a space. A parameter list follows the name.
                if (depth == 0 && (current.Length > 0 || (index > 0 && line[index - 1] == '>')))
                {
                    break;
                }

                depth++;
            }
            else if (character == ')')
            {
                depth = Math.Max(0, depth - 1);
            }

            if (current.Length > 0)
            {
                name = current.ToString();
                current.Clear();
            }
        }

        return current.Length > 0 ? current.ToString() : name;
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
