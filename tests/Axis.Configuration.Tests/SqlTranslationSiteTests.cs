using System.Text.RegularExpressions;
using Axis.Configuration.Diagnostics;

namespace Axis.Configuration.Tests;

/// <summary>
/// A sensitive field's stored value is encrypted, so an expression that is translated to SQL must
/// not read one. Compilation refuses such a read with <see cref="DiagnosticCodes.SensitiveFieldUse"/>.
/// This test finds every call in <c>src</c> that translates an expression to SQL and fails on one that
/// no compile-time check covers, so a new call cannot skip the rule.
/// </summary>
public sealed partial class SqlTranslationSiteTests
{
    private const string Translator = "Axis.Expressions/Sql/SqlTranslator.cs";
    private const string Itself = "";

    private const string Compiler = "Axis.Configuration/Compilation/ApplicationCompiler.cs";
    private const string Migrations = "Axis.Configuration/Compilation/MigrationChecker.cs";

    /// <summary>
    /// Each file that calls <c>SqlTranslator.Translate(</c>: how many calls it holds, and the
    /// compile-time file that refuses a sensitive read in the expression, which is the file itself
    /// when it compiles the expression.
    /// </summary>
    private static readonly Dictionary<string, (int Calls, string Checker)> _sites = new()
    {
        // The compiler translates a data source or policy filter once, and refuses the sensitive read before it does.
        [Compiler] = (1, Itself),
        [Migrations] = (2, Itself),

        // A policy filter is compiled by ApplicationCompiler.CheckFilter, and so is a data source or measure filter.
        ["Axis.Data/Records/RecordFilters.cs"] = (2, Compiler),
        ["Axis.Data/DataSources/DataSourceQueries.cs"] = (2, Compiler),

        // Activation translates the value of a migration that MigrationChecker has already checked.
        ["Axis.Data/Schema/MigrationPlanning.cs"] = (2, Migrations),
    };

    [Fact]
    public void Every_call_that_translates_an_expression_to_sql_is_covered_by_a_sensitive_field_check()
    {
        var problems = Scan(Sources());

        Assert.Empty(problems);
    }

    [Fact]
    public void A_call_without_a_listed_check_is_reported()
    {
        var sources = Sources();
        sources["Axis.Server/Unchecked.cs"] = "var translated = SqlTranslator.Translate(expression, Column);";

        var problems = Scan(sources);

        var problem = Assert.Single(problems);
        Assert.Contains("Axis.Server/Unchecked.cs", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_call_in_a_listed_file_is_reported()
    {
        var sources = Sources();
        sources["Axis.Data/Records/RecordFilters.cs"] += "\nvar again = SqlTranslator.Translate(expression, Column);";

        var problems = Scan(sources);

        var problem = Assert.Single(problems);
        Assert.Contains("Axis.Data/Records/RecordFilters.cs", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_checking_file_without_the_diagnostic_is_reported()
    {
        var sources = Sources();
        sources[Migrations] = sources[Migrations].Replace("DiagnosticCodes.SensitiveFieldUse", "DiagnosticCodes.Other", StringComparison.Ordinal);

        var problems = Scan(sources);

        Assert.Contains(problems, problem => problem.Contains(Migrations, StringComparison.Ordinal));
    }

    [Fact]
    public void A_call_in_a_comment_is_not_a_call()
    {
        var sources = Sources();
        sources["Axis.Server/Comment.cs"] = "// SqlTranslator.Translate(expression, Column) is not called here.\n/// <see cref=\"SqlTranslator.Translate(\"/>";

        Assert.Empty(Scan(sources));
    }

    /// <summary>The problems with the calls to <c>SqlTranslator.Translate(</c> in <paramref name="sources"/>, keyed by path below <c>src</c>.</summary>
    private static List<string> Scan(Dictionary<string, string> sources)
    {
        var problems = new List<string>();
        var counts = sources
            .Where(source => source.Key != Translator)
            .ToDictionary(source => source.Key, source => CountCalls(source.Value))
            .Where(count => count.Value > 0)
            .ToDictionary(count => count.Key, count => count.Value);

        foreach (var (file, calls) in counts)
        {
            if (!_sites.TryGetValue(file, out var site))
            {
                problems.Add($"{file} has {calls} call(s) to SqlTranslator.Translate that no sensitive-field check covers. Check the expression with SensitiveReads before it, and list the file here.");
            }
            else if (site.Calls != calls)
            {
                problems.Add($"{file} has {calls} call(s) to SqlTranslator.Translate, but {site.Calls} are listed. Check each new call for a sensitive field, and update the count here.");
            }
        }

        foreach (var (file, site) in _sites)
        {
            if (!counts.ContainsKey(file))
            {
                problems.Add($"{file} is listed but no longer calls SqlTranslator.Translate. Remove its row.");
            }

            var checker = site.Checker.Length == 0 ? file : site.Checker;
            if (!sources.TryGetValue(checker, out var checkerSource)
                || !checkerSource.Contains("DiagnosticCodes.SensitiveFieldUse", StringComparison.Ordinal))
            {
                problems.Add($"{file} relies on {checker} to refuse a sensitive field, but that file does not report DiagnosticCodes.SensitiveFieldUse.");
            }
        }

        return problems;
    }

    private static int CountCalls(string source) =>
        source.Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Sum(line => TranslateCall().Count(line));

    private static Dictionary<string, string> Sources()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Source");
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .ToDictionary(
                file => Path.GetRelativePath(root, file).Replace('\\', '/'),
                File.ReadAllText);
    }

    [GeneratedRegex(@"SqlTranslator\.Translate\(")]
    private static partial Regex TranslateCall();
}
