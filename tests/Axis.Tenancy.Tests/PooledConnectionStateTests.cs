using System.Text.RegularExpressions;

namespace Axis.Tenancy.Tests;

/// <summary>
/// The guard for <c>docs/reference/cells.md#connections</c>. A connection of the runtime role may go through PgBouncer in
/// transaction mode, which hands the next transaction to any server connection. So no code in <c>src/</c> may leave state
/// on a session: a session advisory lock, a plain <c>SET</c>, <c>set_config(..., false)</c>, <c>LISTEN</c> or
/// <c>CREATE TEMP TABLE</c>. This scans <c>src/</c> for those statements and fails on a file that is not on
/// <see cref="_allowed"/>. It is a text scan. It does not see SQL built from variables, and it judges a <c>SET</c> inside
/// a raw string by where the statement starts.
/// </summary>
public sealed class PooledConnectionStateTests
{
    private static readonly (string Name, Regex Pattern)[] _patterns =
    [
        ("session advisory lock", new(@"\bpg_(try_)?advisory_lock(_shared)?\(", RegexOptions.CultureInvariant)),
        ("set_config for the session", new(@"set_config\([^)]*,\s*false\s*\)", RegexOptions.CultureInvariant)),
        ("LISTEN", new(@"(?<![\w])LISTEN\s+\w", RegexOptions.CultureInvariant)),
        ("CREATE TEMP TABLE", new(@"\bCREATE\s+TEMP(ORARY)?\s+TABLE\b", RegexOptions.CultureInvariant)),
    ];

    // A SET starts a statement after an opening quote or a semicolon. SET LOCAL, SET TRANSACTION and SET CONSTRAINTS end with the transaction.
    private static readonly Regex _plainSet = new(@"(?:""|;\s*)SET\s+(?!LOCAL\b|TRANSACTION\b|CONSTRAINTS\b)", RegexOptions.CultureInvariant);

    // A raw string can start its statement on the line after the opening quotes, where UPDATE ... SET also puts a SET.
    private static readonly Regex _lineStartSet = new(@"^\s*SET\s+(?!LOCAL\b|TRANSACTION\b|CONSTRAINTS\b)", RegexOptions.CultureInvariant);

    private static readonly Regex _runtimeSource = new(@"\b(I?TenantConnectionFactory|TenantDatabase)\b", RegexOptions.CultureInvariant);

    private static readonly Regex _externalHandler = new(@":\s*I(External|Delivery)WorkItemHandler\b", RegexOptions.CultureInvariant);

    /// <summary>The files that keep session state on a direct connection, each with why that is safe.</summary>
    private static readonly Dictionary<string, string> _allowed = new()
    {
        ["Axis.Data/EntityIndexBuilder.cs"] = "It takes a session advisory lock with the schema role, against an activation, on a direct connection.",
        ["Axis.Scenarios.Runner/ScenarioRunner.cs"] = "It sets axis.now for the session on the scenario's own direct connection.",
    };

    /// <summary>One statement that leaves state on a session.</summary>
    internal sealed record Finding(string File, int Line, string Pattern);

    [Fact]
    public void No_file_in_src_leaves_state_on_a_pooled_connection_unless_it_is_allowed()
    {
        var findings = new List<Finding>();
        var runtimeFiles = new HashSet<string>();
        foreach (var (relative, text) in SourceFiles())
        {
            findings.AddRange(Scan(relative, text));
            if (_runtimeSource.IsMatch(text))
            {
                runtimeFiles.Add(relative);
            }
        }

        var unlisted = findings
            .Where(finding => !_allowed.ContainsKey(finding.File))
            .Select(finding => $"{finding.File}:{finding.Line} uses {finding.Pattern}")
            .ToList();
        Assert.True(
            unlisted.Count == 0,
            "State on a session outlives a transaction and reaches the next user of a pooled connection. Use a transaction-level form, such as SET LOCAL or pg_advisory_xact_lock:"
            + Environment.NewLine + string.Join(Environment.NewLine, unlisted));

        var pooled = _allowed.Keys.Where(runtimeFiles.Contains).ToList();
        Assert.True(pooled.Count == 0, "These allowed files name a runtime-role connection source, so they are not direct: " + string.Join(", ", pooled));

        var found = findings.Select(finding => finding.File).ToHashSet();
        var stale = _allowed.Keys.Where(key => !found.Contains(key)).ToList();
        Assert.True(stale.Count == 0, "These allow-list entries match no statement in src/: " + string.Join(", ", stale));
    }

    [Fact]
    public void An_external_work_item_handler_begins_each_transaction_through_the_queue()
    {
        var direct = SourceFiles()
            .Where(file => _externalHandler.IsMatch(file.Text) && CodeLines(file.Text).Any(line => line.Text.Contains("BeginTransactionAsync(", StringComparison.Ordinal)))
            .Select(file => file.Relative)
            .ToList();

        Assert.True(
            direct.Count == 0,
            "A handler that calls an outside system must begin its transactions with WorkItemQueue.BeginAsync, so they run under the item's statement limit: "
            + string.Join(", ", direct));
    }

    [Theory]
    [InlineData("var sql = \"SELECT pg_advisory_lock(1)\";", "session advisory lock")]
    [InlineData("var sql = \"SELECT pg_try_advisory_lock_shared(1)\";", "session advisory lock")]
    [InlineData("var sql = \"SET statement_timeout = '5s'\";", "plain SET")]
    [InlineData("var sql = \"SELECT 1; SET search_path = x\";", "plain SET")]
    [InlineData("var sql = \"SELECT set_config('x', 'y', false)\";", "set_config for the session")]
    [InlineData("var sql = \"LISTEN changes\";", "LISTEN")]
    [InlineData("var sql = \"CREATE TEMP TABLE t (id int)\";", "CREATE TEMP TABLE")]
    [InlineData("var sql = \"\"\"\n    SET statement_timeout = '5s'\n    \"\"\";", "plain SET")]
    public void A_statement_that_leaves_state_on_a_session_is_reported(string code, string pattern)
    {
        var finding = Assert.Single(Scan("Axis.Fake/New.cs", code));

        Assert.Equal("Axis.Fake/New.cs", finding.File);
        Assert.Equal(pattern, finding.Pattern);
    }

    [Theory]
    [InlineData("var sql = \"UPDATE t SET a = 1\";")]
    [InlineData("var sql = \"\"\"\n    UPDATE t\n    SET a = 1\n    \"\"\";")]
    [InlineData("var sql = \"\"\"\n    INSERT INTO t (id) VALUES (1)\n    ON CONFLICT (id) DO UPDATE\n    SET a = 1\n    \"\"\";")]
    [InlineData("var sql = \"SET LOCAL lock_timeout = '10s'\";")]
    [InlineData("var sql = \"SET TRANSACTION READ ONLY\";")]
    [InlineData("var sql = \"SET CONSTRAINTS ALL DEFERRED\";")]
    [InlineData("var sql = \"SELECT set_config('x', 'y', true)\";")]
    [InlineData("var sql = \"SELECT pg_advisory_xact_lock(1)\";")]
    [InlineData("// SET statement_timeout = 0 is not run")]
    public void A_statement_that_ends_with_its_transaction_is_not_reported(string code) =>
        Assert.Empty(Scan("Axis.Fake/New.cs", code));

    /// <summary>Finds the statements in a file that leave state on a session.</summary>
    internal static IReadOnlyList<Finding> Scan(string relativePath, string source)
    {
        var findings = new List<Finding>();
        var lines = CodeLines(source).ToList();
        var previous = "";
        foreach (var (number, line) in lines.Select(code => (code.Number, code.Text)))
        {
            foreach (var (name, pattern) in _patterns)
            {
                if (pattern.IsMatch(line))
                {
                    findings.Add(new Finding(relativePath, number, name));
                }
            }

            if (_plainSet.IsMatch(line) || (previous.EndsWith("\"\"\"", StringComparison.Ordinal) && _lineStartSet.IsMatch(line)))
            {
                findings.Add(new Finding(relativePath, number, "plain SET"));
            }

            if (line.Trim().Length > 0)
            {
                previous = line.TrimEnd();
            }
        }

        return findings;
    }

    private static IEnumerable<(int Number, string Text)> CodeLines(string source) =>
        source.Replace("\r\n", "\n").Split('\n')
            .Select((text, index) => (Number: index + 1, Text: text))
            .Where(line => !line.Text.TrimStart().StartsWith("//", StringComparison.Ordinal));

    private static List<(string Relative, string Text)> SourceFiles()
    {
        var source = Path.Combine(RepositoryRoot(), "src");
        var files = new List<(string, string)>();
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file).Replace(Path.DirectorySeparatorChar, '/');
            if (!relative.Split('/').Any(segment => segment is "Migrations" or "bin" or "obj"))
            {
                files.Add((relative, File.ReadAllText(file)));
            }
        }

        Assert.NotEmpty(files);
        return files;
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
